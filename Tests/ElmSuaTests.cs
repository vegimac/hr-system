using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>Foundation F07 — CSR/Subject-DN und Statusmeldungen (Walter/Cursor 24.09.2026).</summary>
public class ElmSuaTests
{
    [Fact]
    public void SubjectDn_KommtAusEmpfaengerFeldern_NichtSelbstErfunden()
    {
        var s = new ElmSuaSubject
        {
            CommonName = "CHE-123.456.789",
            OrganizationName = "Muster AG",
            LocalityName = "Bern",
            StateOrProvinceName = "BE",
            CountryName = "CH",
        };
        var dn = s.AlsDn();
        Assert.Contains("CN=CHE-123.456.789", dn);
        Assert.Contains("O=Muster AG", dn);
        Assert.Contains("L=Bern", dn);
        Assert.Contains("S=BE", dn);
        Assert.Contains("C=CH", dn);
    }

    [Fact]
    public void Csr_WirdAlsPemBase64Erzeugt()
    {
        var s = new ElmSuaSubject
        {
            CommonName = "Test",
            OrganizationName = "Org",
            LocalityName = "Ort",
            StateOrProvinceName = "LU",
            CountryName = "CH",
        };
        var (b64, key) = ElmSuaService.ErzeugeCsrPemBase64(s);
        Assert.False(string.IsNullOrWhiteSpace(b64));
        var pem = System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(b64));
        Assert.Contains("BEGIN CERTIFICATE REQUEST", pem);
        Assert.NotNull(key.ExportParameters(true).D);
        key.Dispose();
    }

    [Fact]
    public void SignBlock_EnthaeltOneTimePassword_RenewNicht()
    {
        var s = new ElmSuaSubject
        {
            CommonName = "CN", OrganizationName = "O", LocalityName = "L",
            StateOrProvinceName = "S", CountryName = "CH",
        };
        var (sign, k1) = ElmSuaService.BaueSignBlock(s, "geheim-otp");
        Assert.Equal("SignCertificate", sign.Name.LocalName);
        Assert.Contains(sign.Elements(), e => e.Name.LocalName == "OneTimePassword" && e.Value == "geheim-otp");
        Assert.Contains(sign.Elements(), e => e.Name.LocalName == "PEM");
        k1.Dispose();

        var (renew, k2) = ElmSuaService.BaueRenewBlock(s);
        Assert.Equal("RenewCertificate", renew.Name.LocalName);
        Assert.DoesNotContain(renew.Elements(), e => e.Name.LocalName == "OneTimePassword");
        k2.Dispose();
    }

    [Theory]
    [InlineData("processing")]
    [InlineData("registered")]
    [InlineData("rejected")]
    [InlineData("verified")]
    [InlineData("expired")]
    public void StateMeldung_DecktAlleSechsZustaendeAb(string state)
    {
        var m = ElmSuaService.StateMeldung(state);
        Assert.Contains(state, m, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PemAusBase64_AkzeptiertDerUndPem()
    {
        using var rsa = RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=T", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var z = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        var der = z.RawData;
        var pem = ElmSuaService.PemAusBase64(Convert.ToBase64String(der));
        Assert.Contains("BEGIN CERTIFICATE", pem);

        var alsPemText = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(pem));
        var wieder = ElmSuaService.PemAusBase64(alsPemText);
        Assert.Contains("BEGIN CERTIFICATE", wieder);
    }

    [Fact]
    public void AbgelehntesZertifikat_WirdErkannt()
    {
        var r = new ElmTransmitterClient.ElmCallResult(false, 500, 10, "<req/>",
            "<Fault><faultcode>Client.security</faultcode>"
            + "<faultstring>non-certified digital certificate</faultstring></Fault>",
            "HTTP 500")
        {
            FaultCode = "Client.security",
            FaultText = "non-certified digital certificate",
        };
        var deutung = ElmTransmitterClient.DeuteSicherheitsFault(r);
        Assert.NotNull(deutung);
        Assert.Equal(ElmWsSecurity.Befund.ZertifikatNichtVertrauenswuerdig, deutung!.Befund);
        Assert.Contains("Swissdec", ElmZertifikatStore.ZertifikatAbgelehntHinweis);
    }

    [Fact]
    public void ImportiereErpPfx_SpeichertUndLaedtMitPrivatemSchluessel()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "elm-pfx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var store = new ElmZertifikatStore(new MiniConfig(tmp));

            using var rsa = RSA.Create(2048);
            var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                "CN=Swissdec-Test, O=Test, C=CH", rsa,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var selbst = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
            var pwd = "geheim-test";
            var pfx = selbst.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, pwd);

            var importiert = store.ImportiereErpPfx(pfx, pwd);
            Assert.True(importiert.HasPrivateKey);
            Assert.Contains("Swissdec-Test", importiert.Subject);

            var geladen = store.LadeErp();
            Assert.NotNull(geladen);
            Assert.True(geladen!.HasPrivateKey);
            Assert.Equal(importiert.Thumbprint, geladen.Thumbprint);

            var json = System.Text.Json.JsonSerializer.Serialize(store.ErpInfo());
            Assert.Contains("\"vorhanden\":true", json);
            Assert.Contains("\"selbstSigniert\":true", json);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void ArchiviereKlartext_SchreibtUnterArchiv()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "elm-arch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var store = new ElmZertifikatStore(new MiniConfig(tmp));
            store.ArchiviereKlartext("check-interop-request", "<Envelope>test</Envelope>");
            var dateien = Directory.GetFiles(Path.Combine(tmp, "archiv"), "*.xml");
            Assert.Single(dateien);
            Assert.Contains("test", File.ReadAllText(dateien[0]));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* temp */ }
        }
    }

    /// <summary>Nur CertStoragePath — ohne Memory-Config-Paket.</summary>
    private sealed class MiniConfig : Microsoft.Extensions.Configuration.IConfiguration
    {
        private readonly string _pfad;
        public MiniConfig(string pfad) => _pfad = pfad;
        public string? this[string key]
        {
            get => key == "Swissdec:CertStoragePath" ? _pfad : null;
            set { }
        }
        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() =>
            Array.Empty<Microsoft.Extensions.Configuration.IConfigurationSection>();
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => new NoopToken();
        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) =>
            throw new NotSupportedException();

        private sealed class NoopToken : Microsoft.Extensions.Primitives.IChangeToken
        {
            public bool HasChanged => false;
            public bool ActiveChangeCallbacks => false;
            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) =>
                Empty.Instance;
            private sealed class Empty : IDisposable
            {
                public static readonly Empty Instance = new();
                public void Dispose() { }
            }
        }
    }

    // ── F07_06: der CSR braucht die ORG_ID (Walter-Befund 28.09.2026) ─────────
    // Swissdec wies den Antrag mit «NOT_plausible 2052» ab, weil im Subject der
    // organizationIdentifier fehlte. Transmitter-Richtlinien Anhang C.3.2/C.3.3:
    // ORG_ID = «NTRCH-» + UID, als OID 2.5.4.97.

    private static ElmSuaSubject MusterSubject() => new()
    {
        CommonName = "NTRCH-CHE-999.999.996@swissdec.ch",
        OrganizationName = "Muster AG",
        LocalityName = "Luzern",
        StateOrProvinceName = "nA",
        CountryName = "CH",
        CompanyUidBfs = "CHE-999.999.996",
    };

    [Fact]
    public void Csr_KodiertSubjectInDerReihenfolgeDerRichtlinie()
    {
        var (b64, key) = ElmSuaService.ErzeugeCsrPemBase64(MusterSubject());
        using (key)
        {
            var pem = System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(b64));
            var csr = CertificateRequest.LoadSigningRequestPem(
                pem, HashAlgorithmName.SHA256, signerSignaturePadding: RSASignaturePadding.Pkcs1);
            var oids = csr.SubjectName.EnumerateRelativeDistinguishedNames(reversed: false)
                .Select(r => r.GetSingleElementType().Value).ToList();
            Assert.Equal(new[] { "2.5.4.6", "2.5.4.8", "2.5.4.7", "2.5.4.3", "2.5.4.10", "2.5.4.97" }, oids);
        }
    }

    [Fact]
    public void OrgId_IstNtrchPlusUid()
    {
        Assert.Equal("NTRCH-CHE-999.999.996", MusterSubject().OrganizationIdentifier);
        Assert.Null(new ElmSuaSubject { CompanyUidBfs = null }.OrganizationIdentifier);
    }

    [Fact]
    public void Csr_TraegtOrganizationIdentifier_UndAlleFelderDerQuittung()
    {
        var (b64, key) = ElmSuaService.ErzeugeCsrPemBase64(MusterSubject());
        using (key)
        {
            var pem = System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(b64));
            var csr = CertificateRequest.LoadSigningRequestPem(
                pem, HashAlgorithmName.SHA256, signerSignaturePadding: RSASignaturePadding.Pkcs1);
            var dn = csr.SubjectName.Name;
            Assert.Contains("OID.2.5.4.97=NTRCH-CHE-999.999.996", dn, StringComparison.Ordinal);
            Assert.Contains("CN=NTRCH-CHE-999.999.996@swissdec.ch", dn, StringComparison.Ordinal);
            Assert.Contains("O=Muster AG", dn, StringComparison.Ordinal);
            Assert.Contains("L=Luzern", dn, StringComparison.Ordinal);
            Assert.Contains("S=nA", dn, StringComparison.Ordinal);
            Assert.Contains("C=CH", dn, StringComparison.Ordinal);
            Assert.Equal(HashAlgorithmName.SHA256, csr.HashAlgorithm);
            Assert.Equal(2048, csr.PublicKey.GetRSAPublicKey()!.KeySize);
        }
    }

    [Fact]
    public void Csr_OhneStateOrProvince_LaesstNurStWeg()
    {
        var (b64, key) = ElmSuaService.ErzeugeCsrPemBase64(MusterSubject(), ohneStateOrProvince: true);
        using (key)
        {
            var pem = System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(b64));
            var csr = CertificateRequest.LoadSigningRequestPem(
                pem, HashAlgorithmName.SHA256, signerSignaturePadding: RSASignaturePadding.Pkcs1);
            var dn = csr.SubjectName.Name;
            Assert.DoesNotContain("S=nA", dn, StringComparison.Ordinal);
            Assert.Contains("OID.2.5.4.97=NTRCH-CHE-999.999.996", dn, StringComparison.Ordinal);
            Assert.Contains("CN=NTRCH-CHE-999.999.996@swissdec.ch", dn, StringComparison.Ordinal);
            Assert.Contains("O=Muster AG", dn, StringComparison.Ordinal);
        }
    }

    // ── CSR-Varianten (Walter 29.09.2026): mit frischem Antrag «mit ORG_ID»
    //    abgewiesen (2052); Anhang C.3.3 verlangt den CSR «gemäss Quittung». ───

    [Fact]
    public void Csr_OhneOrgId_IstExaktDasQuittungsSubject()
    {
        var (b64, key) = ElmSuaService.ErzeugeCsrPemBase64(MusterSubject(), ohneOrgId: true);
        using (key)
        {
            var pem = System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(b64));
            var csr = CertificateRequest.LoadSigningRequestPem(
                pem, HashAlgorithmName.SHA256, signerSignaturePadding: RSASignaturePadding.Pkcs1);
            var oids = csr.SubjectName.EnumerateRelativeDistinguishedNames(reversed: false)
                .Select(r => r.GetSingleElementType().Value).ToList();
            Assert.Equal(new[] { "2.5.4.6", "2.5.4.8", "2.5.4.7", "2.5.4.3", "2.5.4.10" }, oids);
            Assert.DoesNotContain("2.5.4.97", csr.SubjectName.Name, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("2.5.4.97", MusterSubject().AlsDn(ohneOrgId: true), StringComparison.Ordinal);
    }

    [Fact]
    public void Varianten_ZuerstOhneOrgId_DieBereitsAbgewiesenenNichtMehr()
    {
        var fall = new ElmSuaFall { Subject = MusterSubject() };
        Assert.Equal(new[] { "quittung", "quittung-ohne-st", "richtlinie", "richtlinie-ohne-st" },
            ElmCsrVariante.Offene(fall).Select(v => v.Kennung));

        fall.CsrAbgewiesen.AddRange(new[] { "quittung", "richtlinie" });
        Assert.Equal(new[] { "quittung-ohne-st", "richtlinie-ohne-st" },
            ElmCsrVariante.Offene(fall).Select(v => v.Kennung));

        fall.CsrAbgewiesen.AddRange(new[] { "quittung-ohne-st", "richtlinie-ohne-st" });
        Assert.Empty(ElmCsrVariante.Offene(fall));
    }

    [Fact]
    public void Varianten_OhneStUndOhneUid_KeineDoppeltenCsr()
    {
        var s = MusterSubject();
        s.StateOrProvinceName = "";
        s.CompanyUidBfs = null;
        var fall = new ElmSuaFall { Subject = s };
        Assert.Equal(new[] { "quittung" }, ElmCsrVariante.Offene(fall).Select(v => v.Kennung));
    }

    [Fact]
    public void Erneuerung_NimmtDieAngenommeneVariante()
    {
        Assert.Equal("richtlinie", ElmCsrVariante.Angenommen(new ElmSuaFall()).Kennung);
        Assert.Equal("quittung-ohne-st", ElmCsrVariante.Angenommen(
            new ElmSuaFall { CsrOhneStateOrProvince = true, CsrOhneOrgId = true }).Kennung);
    }

    // ── Antwort lesen: Code, DescriptionCode, Description, Einmalpasswort ─────

    private const string AntwortMitOtp = """
        <Envelope><Body><SynchronizeResponse>
          <Comment>
            <Notification><QualityLevel>Information</QualityLevel>
              <DescriptionCode>9998</DescriptionCode>
              <Description>One time password: DW8K-4F2A-9C31</Description></Notification>
          </Comment>
        </SynchronizeResponse></Body></Envelope>
        """;

    private const string FaultNichtPlausibel = """
        <Envelope><Body><Fault>
          <faultcode>NOT_plausible</faultcode>
          <faultstring>request is not plausible</faultstring>
          <detail><Notification><QualityLevel>Error</QualityLevel>
            <DescriptionCode>2052</DescriptionCode>
            <Description>Certificate request not plausible</Description></Notification></detail>
        </Fault></Body></Envelope>
        """;

    [Fact]
    public void Einmalpasswort_WirdAusCode9998Gelesen()
    {
        var m = ElmSuaService.LiesMeldungen(AntwortMitOtp);
        Assert.Equal("DW8K-4F2A-9C31", m.Einmalpasswort);
        Assert.Equal("9998", m.DescriptionCode);
        Assert.False(ElmSuaService.IstNichtPlausibel(m));
    }

    [Fact]
    public void Fault2052_WirdAlsNichtPlausibelErkannt()
    {
        var m = ElmSuaService.LiesMeldungen(FaultNichtPlausibel, "NOT_plausible");
        Assert.True(ElmSuaService.IstNichtPlausibel(m));
        Assert.Equal("2052", m.DescriptionCode);
        Assert.Equal("Certificate request not plausible", m.Description);
        Assert.Equal("NOT_plausible", m.Code);
        Assert.Null(m.Einmalpasswort);
    }

    [Fact]
    public void OhneMeldungen_BleibtAllesLeer_StattZuRaten()
    {
        var m = ElmSuaService.LiesMeldungen("<Envelope/>");
        Assert.Empty(m.Zeilen);
        Assert.Null(m.DescriptionCode);
        Assert.Null(m.Einmalpasswort);
        Assert.False(ElmSuaService.IstNichtPlausibel(m));
    }

    [Fact]
    public void ZertifikatPem_UeberspringtUserAgentCertificateOhnePem()
    {
        var doc = System.Xml.Linq.XDocument.Parse("""
            <Envelope><Body><SynchronizeResponse>
              <UserAgent><Producer>Swissdec</Producer><Certificate>swissdec</Certificate></UserAgent>
              <State>signed</State>
              <Certificate><PEM>LS0tLS1CRUdJTg==</PEM></Certificate>
            </SynchronizeResponse></Body></Envelope>
            """);
        Assert.Equal("LS0tLS1CRUdJTg==", ElmSuaService.ZertifikatPemAus(doc));
    }

    [Fact]
    public void ZertifikatPem_OhneZertifikat_IstNull()
    {
        var doc = System.Xml.Linq.XDocument.Parse(
            "<Envelope><UserAgent><Certificate>swissdec</Certificate></UserAgent></Envelope>");
        Assert.Null(ElmSuaService.ZertifikatPemAus(doc));
    }
}

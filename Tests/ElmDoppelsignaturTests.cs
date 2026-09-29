using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Doppelsignatur ERP + SUA (Foundation F07_07 / F07_08, Walter 29.09.2026).
///
/// Sicherheitsrichtlinie Kap. 3.3.1 Punkt 6 und Tabelle 4.6: erst mit dem ERP-, dann
/// mit dem UID-/SUA-Zertifikat signieren; beide Signaturen decken Body UND Timestamp
/// ab. Pflicht für RenewCertificate und für CheckInterop mit installiertem SUA.
/// </summary>
public class ElmDoppelsignaturTests
{
    private static X509Certificate2 Zertifikat(string name)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var zert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(zert.Export(X509ContentType.Pfx, "test"), "test",
            X509KeyStorageFlags.Exportable);
    }

    private static XmlDocument Nachricht()
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml($"""
            <soap:Envelope xmlns:soap="{ElmWsSecurity.NsSoap}">
              <soap:Header />
              <soap:Body>
                <CheckInteroperability xmlns="urn:ch:swissdec:elm:v6:20260306:salarydeclaration:service:types">
                  <UmlautString>ÄËÖÜ</UmlautString>
                  <FirstOperand>1234.55</FirstOperand>
                </CheckInteroperability>
              </soap:Body>
            </soap:Envelope>
            """);
        return doc;
    }

    private static XmlDocument Lade(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        return doc;
    }

    /// <summary>Wie `ElmWsSecurity.WsuSignedXml` — findet Body/Timestamp über wsu:Id.</summary>
    private sealed class WsuSigned : SignedXml
    {
        public WsuSigned(XmlDocument doc) : base(doc) { }
        public override XmlElement? GetIdElement(XmlDocument? doc, string id)
        {
            var t = base.GetIdElement(doc, id);
            if (t != null || doc == null) return t;
            var nsm = new XmlNamespaceManager(doc.NameTable);
            nsm.AddNamespace("wsu", ElmWsSecurity.NsWsu);
            return doc.SelectSingleNode($"//*[@wsu:Id='{id}']", nsm) as XmlElement;
        }
    }

    /// <summary>Prüft die n-te Signatur mit dem Zertifikat aus dem BinarySecurityToken, auf das sie zeigt.</summary>
    private static (bool Gueltig, X509Certificate2 Zert) PruefeSignatur(XmlDocument doc, int index)
    {
        var sig = (XmlElement)doc.GetElementsByTagName("Signature", ElmWsSecurity.NsDs)[index]!;
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("wsse", ElmWsSecurity.NsWsse);
        nsm.AddNamespace("wsu", ElmWsSecurity.NsWsu);
        var uri = ((XmlElement)sig.SelectSingleNode(".//wsse:Reference", nsm)!).GetAttribute("URI").TrimStart('#');
        var bst = (XmlElement)doc.SelectSingleNode($"//wsse:BinarySecurityToken[@wsu:Id='{uri}']", nsm)!;
        var zert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(bst.InnerText));
        var signed = new WsuSigned(doc);
        signed.LoadXml(sig);
        return (signed.CheckSignature(zert, verifySignatureOnly: true), zert);
    }

    [Fact]
    public void Doppelsignatur_EinTimestamp_ZweiTokens_ZweiSignaturen()
    {
        var doc = Nachricht();
        ElmWsSecurity.Signiere(doc, new[] { Zertifikat("CN=ERP"), Zertifikat("CN=SUA") });

        Assert.Single(doc.GetElementsByTagName("Timestamp", ElmWsSecurity.NsWsu).Cast<XmlNode>());
        Assert.Equal(2, doc.GetElementsByTagName("BinarySecurityToken", ElmWsSecurity.NsWsse).Count);
        Assert.Equal(2, doc.GetElementsByTagName("Signature", ElmWsSecurity.NsDs).Count);

        // Jede Signatur referenziert genau Timestamp und Body — dieselben Kennungen.
        var sigs = doc.GetElementsByTagName("Signature", ElmWsSecurity.NsDs).Cast<XmlElement>().ToList();
        var uris = sigs.Select(s => string.Join(",", s.GetElementsByTagName("Reference", ElmWsSecurity.NsDs)
            .Cast<XmlElement>().Select(r => r.GetAttribute("URI")).OrderBy(u => u))).ToList();
        Assert.Equal(uris[0], uris[1]);
        Assert.Equal(2, uris[0].Split(',').Length);
    }

    [Fact]
    public void Doppelsignatur_ErstErp_DannSua_BeideGueltigNachDemVersand()
    {
        var erp = Zertifikat("CN=ERP");
        var sua = Zertifikat("CN=SUA");
        var doc = Nachricht();
        ElmWsSecurity.Signiere(doc, new[] { erp, sua });

        // Einmal durch Text und zurück — so kommt die Nachricht beim Empfänger an.
        var empfangen = Lade(doc.OuterXml);
        var (g1, z1) = PruefeSignatur(empfangen, 0);
        var (g2, z2) = PruefeSignatur(empfangen, 1);

        Assert.True(g1, "ERP-Signatur ungültig");
        Assert.True(g2, "SUA-Signatur ungültig");
        Assert.Equal(erp.Thumbprint, z1.Thumbprint);
        Assert.Equal(sua.Thumbprint, z2.Thumbprint);
    }

    [Fact]
    public void Doppelsignatur_Verfaelscht_BeideSignaturenUngueltig()
    {
        var doc = Nachricht();
        ElmWsSecurity.Signiere(doc, new[] { Zertifikat("CN=ERP"), Zertifikat("CN=SUA") });
        var verfaelscht = Lade(doc.OuterXml.Replace("1234.55", "9999.99"));

        Assert.False(PruefeSignatur(verfaelscht, 0).Gueltig);
        Assert.False(PruefeSignatur(verfaelscht, 1).Gueltig);
    }

    [Fact]
    public void Einfachsignatur_BleibtWieBisher()
    {
        var doc = Nachricht();
        ElmWsSecurity.Signiere(doc, Zertifikat("CN=ERP"));

        Assert.Single(doc.GetElementsByTagName("BinarySecurityToken", ElmWsSecurity.NsWsse).Cast<XmlNode>());
        Assert.Single(doc.GetElementsByTagName("Signature", ElmWsSecurity.NsDs).Cast<XmlNode>());
        Assert.True(PruefeSignatur(Lade(doc.OuterXml), 0).Gueltig);
    }

    [Fact]
    public void Doppelsignatur_OhneSchluessel_WirdAbgelehnt()
    {
        var sua = Zertifikat("CN=SUA");
        var ohneSchluessel = X509CertificateLoader.LoadCertificate(sua.RawData);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ElmWsSecurity.Signiere(Nachricht(), new[] { Zertifikat("CN=ERP"), ohneSchluessel }));
        Assert.Contains("SUA", ex.Message);
    }

    /// <summary>
    /// Die Antwort der Gegenseite muss am Original geprüft werden (Walter 29.09.2026:
    /// «Signatur der Antwort ist ungültig» bei jeder RefApps-Antwort). Umformatiert
    /// für die Anzeige stimmen die Digests nicht mehr.
    /// </summary>
    [Fact]
    public void Antwort_NurImOriginalGueltig_NichtUmformatiert()
    {
        var doc = Nachricht();
        ElmWsSecurity.Signiere(doc, Zertifikat("CN=Distributor"));
        var roh = doc.OuterXml;
        var umformatiert = System.Xml.Linq.XDocument.Parse(roh).ToString();

        Assert.True(PruefeSignatur(Lade(roh), 0).Gueltig);
        Assert.False(PruefeSignatur(Lade(umformatiert), 0).Gueltig);
    }

    [Fact]
    public void Csr_Pem_NachRfc7468_64ZeichenNurLf()
    {
        var subject = new ElmSuaSubject
        {
            CommonName = "NTRCH-CHE-999.999.996@swissdec.ch",
            OrganizationName = "Muster AG",
            LocalityName = "Luzern",
            StateOrProvinceName = "nA",
            CountryName = "CH",
            CompanyUidBfs = "CHE-999.999.996",
        };
        var (b64, key) = ElmSuaService.ErzeugeCsrPemBase64(subject);
        using (key)
        {
            var pem = System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(b64));
            Assert.DoesNotContain('\r', pem);
            Assert.StartsWith("-----BEGIN CERTIFICATE REQUEST-----\n", pem);
            Assert.EndsWith("-----END CERTIFICATE REQUEST-----\n", pem);
            Assert.All(pem.Split('\n', StringSplitOptions.RemoveEmptyEntries),
                zeile => Assert.True(zeile.Length <= 64, $"Zeile zu lang: {zeile.Length}"));
        }
    }

    private static X509Certificate2 ZertifikatMitSki(string name)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        var zert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(zert.Export(X509ContentType.Pfx, "test"), "test",
            X509KeyStorageFlags.Exportable);
    }

    /// <summary>
    /// Live 29.09.2026 (CheckInterop F07_08): nach der doppelt signierten Anfrage kam die
    /// Antwort für das SUA-Zertifikat verschlüsselt — mit dem ERP-Schlüssel «oaep decoding error».
    /// </summary>
    [Fact]
    public void Antwort_FuerSuaVerschluesselt_WirdMitSuaSchluesselEntschluesselt()
    {
        var erp = ZertifikatMitSki("CN=ERP");
        var sua = ZertifikatMitSki("CN=SUA");
        var distributor = Zertifikat("CN=Distributor");

        var doc = Nachricht();
        ElmWsSecurity.Signiere(doc, new[] { distributor });
        ElmWsSecurity.Verschluessele(doc, sua);
        var antwort = doc.OuterXml;

        Assert.Same(sua, ElmWsSecurity.WaehleEntschluesselungsZertifikat(Lade(antwort), new[] { erp, sua }));

        var nurErp = ElmWsSecurity.Pruefe(Lade(antwort), erp);
        Assert.Equal(ElmWsSecurity.Befund.EntschluesselungFehlgeschlagen, nurErp.Befund);

        var beide = Lade(antwort);
        var pruef = ElmWsSecurity.PruefeMitSchluesseln(beide, new[] { erp, sua });
        Assert.True(pruef.Ok, pruef.Meldung);
        Assert.Contains("1234.55", beide.OuterXml);
    }
}

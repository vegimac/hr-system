using HrSystem.Services.Elm;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Die SUA-Meldungen gegen die echten ELM-6.0-Schemas prüfen (Walter 24.09.2026).
///
/// Anlass: Die erste Fassung schickte drei schema-ungültige Meldungen los — `TestCase`
/// im Addressee statt im Job, den Synchronize-Addressee im Aufbau des Register-Addressee
/// und `Case` im falschen Namensraum. Sichtbar wurde das erst am abgewiesenen Aufruf,
/// und weil gleichzeitig die Signatur fraglich war, wurde die Ursache falsch zugeordnet.
///
/// Der Validator lag bereit (`ElmXmlValidator`) — er wurde nur nicht benutzt. Diese Tests
/// schliessen die Lücke: Jede Meldung, die wir bauen, wird hier gegen die Schemas gehalten.
/// </summary>
public class ElmSuaSchemaTests
{
    private static readonly ElmXmlValidator Validator = new();

    /// <summary>Dienst nur zum Bauen der Meldungen — DB und Netz werden dabei nie berührt.</summary>
    private static ElmSuaService Dienst(string? monitoringId = "onecrew-test")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Swissdec:MonitoringId"] = monitoringId })
            .Build();
        return new ElmSuaService(null!, null!, null!, new ElmEinstellungen(config), Validator);
    }

    private static void IstGueltig(System.Xml.Linq.XElement body)
    {
        var fehler = Validator.Validate(body.ToString());
        Assert.True(fehler.Count == 0, string.Join("\n", fehler));
    }

    private static ElmSuaFall Fall(bool testfall = false, string? state = "processing") => new()
    {
        CertificateRequestId = "REQ-1",
        CredentialKey = "schluessel",
        CredentialPassword = "passwort",
        AddresseeIdentification = "1234",
        Uid = "CHE-123.456.789",
        CompanyName = "Schaub Restaurants GmbH",
        Domain = "UVG-LAA",
        AlsTestfall = testfall,
        LetzterState = state,
    };

    private static System.Xml.Linq.XElement Register(bool testfall) =>
        Dienst().BaueRegisterBody("CHE-123.456.789", "Schaub Restaurants GmbH", "Walter Schaub",
            "1234", "6210", "Sursee", "UVG-LAA", "Versicherer", "K-1", "V-1", testfall);

    // ── Register ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Register_IstSchemaGueltig(bool testfall) => IstGueltig(Register(testfall));

    [Fact]
    public void Register_TestCase_StehtImJob_NichtImAddressee()
    {
        var job = Register(testfall: true).Elements()
            .First(e => e.Name.LocalName == "Job");
        var testCase = job.Elements().FirstOrDefault(e => e.Name.LocalName == "TestCase");
        Assert.NotNull(testCase);
        // Container-Namensraum, nicht der Komponenten-Namensraum.
        Assert.EndsWith("salarydeclaration:container", testCase!.Name.NamespaceName);
        // Und NICHT im Addressee — dort kennt das Schema das Element nicht.
        var addressee = job.Elements().First(e => e.Name.LocalName == "Addressee");
        Assert.DoesNotContain(addressee.Elements(), e => e.Name.LocalName == "TestCase");
    }

    [Fact]
    public void Register_OhneTestfall_HatKeineTestMarke()
        => Assert.DoesNotContain(Register(testfall: false).Descendants(),
                                 e => e.Name.LocalName == "TestCase");

    [Theory]
    [InlineData("UVG-LAA")]
    [InlineData("UVGZ-LAAC")]
    [InlineData("KTG-AMC")]
    [InlineData("BVG-LPP")]
    public void Register_JederVersicherungszweig_IstGueltig(string domain)
        => IstGueltig(Dienst().BaueRegisterBody("CHE-123.456.789", "Firma", "Kontakt",
            "1234", "6210", "Sursee", domain, "Versicherer", "K-1", "V-1", false));

    // ── Synchronize ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Synchronize_IstSchemaGueltig(bool testfall)
        => IstGueltig(Dienst().BaueSynchronizeBody(Fall(testfall), null));

    [Fact]
    public void Synchronize_OhneBisherigenZustand_IstGueltig()
        => IstGueltig(Dienst().BaueSynchronizeBody(Fall(state: null), null));

    [Fact]
    public void Synchronize_MitZertifikatsantrag_IstGueltig()
    {
        var subject = new ElmSuaSubject
        {
            CommonName = "Schaub Restaurants GmbH",
            OrganizationName = "Schaub Restaurants GmbH",
            LocalityName = "Sursee",
            StateOrProvinceName = "LU",
            CountryName = "CH",
        };
        var (block, key) = ElmSuaService.BaueSignBlock(subject, "einmalpasswort");
        using (key) IstGueltig(Dienst().BaueSynchronizeBody(Fall(state: "verified"), block));
    }

    [Fact]
    public void Synchronize_MitErneuerung_IstGueltig()
    {
        var subject = new ElmSuaSubject
        {
            CommonName = "Schaub Restaurants GmbH", OrganizationName = "Schaub Restaurants GmbH",
            LocalityName = "Sursee", StateOrProvinceName = "LU", CountryName = "CH",
        };
        var (block, key) = ElmSuaService.BaueRenewBlock(subject);
        using (key) IstGueltig(Dienst().BaueSynchronizeBody(Fall(state: "registered"), block));
    }

    [Fact]
    public void Synchronize_Addressee_FuehrtDenVersicherungszweig()
    {
        var addressee = Dienst().BaueSynchronizeBody(Fall(), null).Elements()
            .First(e => e.Name.LocalName == "Addressee");
        Assert.Equal("UVG-LAA", addressee.Elements().First(e => e.Name.LocalName == "Domain").Value);
        // Der Register-Addressee-Aufbau gehört hier NICHT hin.
        Assert.Null(addressee.Attribute("addresseeID"));
        Assert.DoesNotContain(addressee.Elements(), e => e.Name.LocalName == "ProcessByDistributor");
    }

    [Fact]
    public void Synchronize_TestMarke_FolgtDerAnmeldung()
    {
        // Ein Testfall lässt sich laut Richtlinie C.2.1.2 nie abschliessen — die Marke
        // darf darum nur mitgehen, wenn die Anmeldung wirklich ein Testfall war.
        Assert.DoesNotContain(Dienst().BaueSynchronizeBody(Fall(testfall: false), null).Descendants(),
                              e => e.Name.LocalName == "TestCase");
        Assert.Contains(Dienst().BaueSynchronizeBody(Fall(testfall: true), null).Descendants(),
                        e => e.Name.LocalName == "TestCase");
    }

    // ── MonitoringID ─────────────────────────────────────────────────────────

    [Fact]
    public void MonitoringId_StehtImRequestContext()
    {
        var kontext = Register(false).Elements().First(e => e.Name.LocalName == "RequestContext");
        var id = kontext.Elements().FirstOrDefault(e => e.Name.LocalName == "MonitoringID");
        Assert.NotNull(id);
        Assert.Equal("onecrew-test", id!.Value);
        // Muss das LETZTE Element des Kontexts sein — das Schema schreibt die Reihenfolge vor.
        Assert.Equal("MonitoringID", kontext.Elements().Last().Name.LocalName);
    }

    [Fact]
    public void OhneMonitoringId_BleibtDasElementWeg()
    {
        // In der Produktion soll die ID fehlen — und ohne Element muss die Meldung gültig bleiben.
        var body = Dienst(monitoringId: "").BaueRegisterBody("CHE-123.456.789", "Firma", "Kontakt",
            "1234", "6210", "Sursee", "UVG-LAA", "V", "K-1", "V-1", false);
        Assert.DoesNotContain(body.Descendants(), e => e.Name.LocalName == "MonitoringID");
        IstGueltig(body);
    }

    [Theory]
    [InlineData("  onecrew  ", "onecrew")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void MonitoringId_WirdBereinigt(string eingabe, string? erwartet)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Swissdec:MonitoringId"] = eingabe })
            .Build();
        Assert.Equal(erwartet, new ElmEinstellungen(config).MonitoringId);
    }

    [Fact]
    public void MonitoringId_WirdAufDreissigzweiZeichenGekuerzt()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
                { ["Swissdec:MonitoringId"] = new string('x', 40) })
            .Build();
        Assert.Equal(32, new ElmEinstellungen(config).MonitoringId!.Length);
    }

    // ── Versicherungszweig ───────────────────────────────────────────────────

    [Theory]
    [InlineData("uvg-laa", "UVG-LAA")]
    [InlineData("BVG-LPP", "BVG-LPP")]
    [InlineData("Quatsch", "UVG-LAA")]
    [InlineData(null, "UVG-LAA")]
    public void UnbekannterZweig_FaelltAufDenStandardZurueck(string? eingabe, string erwartet)
        => Assert.Equal(erwartet, ElmSuaService.DomainOderStandard(eingabe));
}

/// <summary>
/// Abgewiesene Aufrufe nur so weit deuten, wie die Antwort es hergibt (Walter 24.09.2026).
/// Die frühere Fassung erklärte jede Sicherheits-Ablehnung zum «Fault 100 — Zertifikat nicht
/// von der Swissdec-CA», auch wenn im Text «has not been signed» stand. Das führte zur
/// falschen Schlussfolgerung, es fehle ein Zertifikat von Swissdec.
/// </summary>
public class ElmFaultDeutungTests
{
    private static ElmTransmitterClient.ElmCallResult Fault(string code, string text)
        => new(false, 500, 10, "<req/>", "<Fault/>", "HTTP 500") { FaultCode = code, FaultText = text };

    [Theory]
    [InlineData("non-certified digital certificate")]
    [InlineData("Unknown CA")]
    [InlineData("certificate path building failed")]
    public void NichtAnerkanntesZertifikat(string text)
        => Assert.Equal(ElmWsSecurity.Befund.ZertifikatNichtVertrauenswuerdig,
                        ElmTransmitterClient.DeuteSicherheitsFault(Fault("Client.security", text))!.Befund);

    [Theory]
    [InlineData("The request has not been signed")]
    [InlineData("missing signature in header")]
    public void FehlendeSignatur_IstNichtDasselbe(string text)
    {
        var d = ElmTransmitterClient.DeuteSicherheitsFault(Fault("Client.security", text))!;
        Assert.Equal(ElmWsSecurity.Befund.SignaturFehlt, d.Befund);
        Assert.DoesNotContain("Swissdec-CA", d.Meldung);
    }

    [Fact]
    public void FehlendeVerschluesselung_WirdEigenGemeldet()
        => Assert.Equal(ElmWsSecurity.Befund.VerschluesselungFehlt,
            ElmTransmitterClient.DeuteSicherheitsFault(
                Fault("Client.security", "message has not been encrypted"))!.Befund);

    [Fact]
    public void NichtEntschluesselbar_WirdEigenGemeldet()
        => Assert.Equal(ElmWsSecurity.Befund.EntschluesselungFehlgeschlagen,
            ElmTransmitterClient.DeuteSicherheitsFault(
                Fault("Client.security", "unable to decrypt message"))!.Befund);

    [Theory]
    [InlineData("NOT_valid", "Schema-Fehler in Zeile 12")]
    [InlineData("Server", "internal error")]
    [InlineData("", "")]
    public void OhneBeleg_KeinUrteil(string code, string text)
        => Assert.Null(ElmTransmitterClient.DeuteSicherheitsFault(Fault(code, text)));
}

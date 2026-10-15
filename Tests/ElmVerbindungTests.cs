using HrSystem.Services.Elm;
using Xunit;
using static HrSystem.Services.Elm.ElmTransmitterClient;

namespace HrSystem.Tests;

/// <summary>Bereich «Verbindung» der Seite Lohnmeldungen: drei Ampeln + Archiv-Download (Walter 15.10.2026).</summary>
public class ElmVerbindungTests
{
    private static ElmCallResult Antwort(bool ok = true, int http = 200, double? diff = 0.4, string? fehler = null)
        => new(ok, http, 120, "", "", fehler) { DiffSekunden = diff };

    [Fact]
    public void Erreichbar_MitAntwort_Gruen()
    {
        var a = ElmVerbindung.Erreichbar(Antwort());
        Assert.Equal(ElmVerbindung.Ok, a.Stufe);
        Assert.Contains("120 ms", a.Text);
    }

    [Fact]
    public void Erreichbar_OhneAntwort_RotMitTipp()
    {
        var a = ElmVerbindung.Erreichbar(Antwort(ok: false, http: 0, diff: null, fehler: "Name or service not known"));
        Assert.Equal(ElmVerbindung.Rot, a.Stufe);
        Assert.Equal("Swissdec antwortet nicht.", a.Text);
        Assert.Equal("Name or service not known", a.Detail);
    }

    [Theory]
    [InlineData(0.4, "ok", "weniger als 1 Sekunde")]
    [InlineData(59.6, "ok", "60 Sekunden")]
    [InlineData(75, "rot", "75 Sekunden vor")]
    [InlineData(-90, "rot", "90 Sekunden nach")]
    public void Uhrzeit_AbEinerMinuteRot(double diff, string stufe, string text)
    {
        var a = ElmVerbindung.Uhrzeit(Antwort(diff: diff), null);
        Assert.Equal(stufe, a.Stufe);
        Assert.Contains(text, a.Text);
    }

    [Fact]
    public void Uhrzeit_OhnePingZeit_NimmtTestnachricht()
    {
        var a = ElmVerbindung.Uhrzeit(Antwort(diff: null), Antwort(diff: 120));
        Assert.Equal(ElmVerbindung.Rot, a.Stufe);
    }

    [Fact]
    public void Uhrzeit_SwissdecNichtErreichbar_Grau()
    {
        var a = ElmVerbindung.Uhrzeit(Antwort(ok: false, http: 0, diff: null), null);
        Assert.Equal(ElmVerbindung.Grau, a.Stufe);
    }

    [Fact]
    public void Sicher_NichtErreichbar_GrauStattRot()
    {
        Assert.Equal(ElmVerbindung.Grau, ElmVerbindung.Sicher(null, true, true, erreichbar: false).Stufe);
    }

    [Fact]
    public void Sicher_OhneZertifikat_Rot()
    {
        var a = ElmVerbindung.Sicher(null, zertifikatDa: false, suaDa: false, erreichbar: true);
        Assert.Equal(ElmVerbindung.Rot, a.Stufe);
        Assert.Contains("Zertifikat", a.Text);
    }

    [Fact]
    public void Sicher_Abgewiesen_RotMitTechnik()
    {
        var r = Antwort() with { FaultCode = "Client.security", FaultText = "security requirements not met" };
        var a = ElmVerbindung.Sicher(r, true, true, true);
        Assert.Equal(ElmVerbindung.Rot, a.Stufe);
        Assert.Contains("Client.security", a.Detail);
    }

    [Fact]
    public void Pruefen_HoechstensAlle30Sekunden()
    {
        var jetzt = new DateTime(2026, 10, 15, 18, 0, 0);
        Assert.Equal(0, ElmVerbindung.WartenSekunden(null, jetzt));
        Assert.Equal(20, ElmVerbindung.WartenSekunden(new ElmVerbindungStand { GeprueftAm = jetzt.AddSeconds(-10) }, jetzt));
        Assert.Equal(0, ElmVerbindung.WartenSekunden(new ElmVerbindungStand { GeprueftAm = jetzt.AddSeconds(-31) }, jetzt));
    }

    [Fact]
    public void Download_BlendetZugangsdatenAus_RestBleibtGleich()
    {
        const string xml = "<s:Body><ep:JobKey>J1</ep:JobKey><ds:KeyInfo>K</ds:KeyInfo>"
            + "<ep:Credentials><ep:Key>abc</ep:Key><ep:Password>geheim</ep:Password></ep:Credentials>"
            + "<Key>x</Key></s:Body>";
        var aus = ElmVerbindung.OhneZugangsdaten(xml);
        Assert.Equal("<s:Body><ep:JobKey>J1</ep:JobKey><ds:KeyInfo>K</ds:KeyInfo>"
            + "<ep:Credentials><ep:Key>***</ep:Key><ep:Password>***</ep:Password></ep:Credentials>"
            + "<Key>***</Key></s:Body>", aus);
    }

    [Fact]
    public void Download_MeldungOhneZugangsdaten_ByteGleich()
    {
        const string xml = "<sd:DeclareSalary><sd:Person><sd:Lastname>Herz</sd:Lastname></sd:Person></sd:DeclareSalary>";
        Assert.Equal(xml, ElmVerbindung.OhneZugangsdaten(xml));
    }
}

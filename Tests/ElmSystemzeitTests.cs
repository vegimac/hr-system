using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Swissdec Foundation-Test **F01_03 «Systemzeit»** (Walter 24.09.2026).
///
/// Erwartet: «Die Systemzeit des Distributors wird mit der lokalen Systemzeit
/// verglichen. Bei einer Abweichung &gt;1 Minute wird der Zeitunterschied in Form
/// einer Fehlermeldung dargestellt.» Im Test setzt Swissdec in den RefApps eine
/// falsche Zeit («Fake Ping Time») — unsere Antwort darauf muss die Abweichung
/// erkennen und beziffern.
/// </summary>
public class ElmSystemzeitTests
{
    /// <summary>Echte Ping-Antwort der RefApps mit variabler Systemzeit.</summary>
    private static string Antwort(string systemDateTime) => $"""
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
          <soap:Body>
            <ns6:PingResponse xmlns="urn:ch:swissdec:basis:v1:20260306:components" xmlns:ns6="urn:ch:swissdec:elm:v6:20260306:salarydeclaration:service:types">
              <UserAgent>
                <Producer>swissdec</Producer>
                <Name>swissdec refapps</Name>
                <Version>4.0.88 RefApps stable</Version>
                <StandardVersion>6.0</StandardVersion>
                <Certificate>swissdec</Certificate>
              </UserAgent>
              <SystemDateTime>{systemDateTime}</SystemDateTime>
            </ns6:PingResponse>
          </soap:Body>
        </soap:Envelope>
        """;

    private static ElmTransmitterClient.ElmCallResult Pruefe(string systemDateTime)
        => ElmTransmitterClient.MitZeitvergleich(
            new ElmTransmitterClient.ElmCallResult(true, 200, 120, "<req/>", Antwort(systemDateTime), null));

    [Fact]
    public void GleicheZeit_KeineAbweichung()
    {
        var r = Pruefe(DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"));
        Assert.NotNull(r.DistributorZeit);
        Assert.False(r.ZeitAbweichung);
        Assert.True(Math.Abs(r.DiffSekunden!.Value) < 5);
    }

    [Theory]
    [InlineData(30)]    // eine halbe Minute — noch in der Toleranz
    [InlineData(-45)]
    public void InnerhalbEinerMinute_IstInOrdnung(int sekunden)
    {
        var r = Pruefe(DateTimeOffset.Now.AddSeconds(sekunden).ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"));
        Assert.False(r.ZeitAbweichung);
    }

    [Theory]
    [InlineData(90)]      // Empfänger geht 1.5 Minuten nach
    [InlineData(-3600)]   // Empfänger geht eine Stunde vor
    public void UeberEinerMinute_WirdGemeldet(int sekunden)
    {
        var r = Pruefe(DateTimeOffset.Now.AddSeconds(-sekunden).ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"));
        Assert.True(r.ZeitAbweichung, "Abweichung über einer Minute muss gemeldet werden (F01_03).");
        Assert.InRange(Math.Abs(r.DiffSekunden!.Value), Math.Abs(sekunden) - 5, Math.Abs(sekunden) + 5);
    }

    [Fact]
    public void ZeitzonenVersatz_IstKeineAbweichung()
    {
        // Derselbe Moment, einmal als Zürcher Sommerzeit, einmal als UTC:
        // der Vergleich läuft über DateTimeOffset und darf das nicht anmerken.
        var jetzt = DateTimeOffset.Now;
        var r = Pruefe(jetzt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        Assert.False(r.ZeitAbweichung);
    }

    [Fact]
    public void AntwortOhneSystemzeit_MeldetNichts()
    {
        var ohne = "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body/></soap:Envelope>";
        var r = ElmTransmitterClient.MitZeitvergleich(
            new ElmTransmitterClient.ElmCallResult(true, 200, 10, "<req/>", ohne, null));
        Assert.Null(r.DiffSekunden);
        Assert.False(r.ZeitAbweichung);
    }

    [Fact]
    public void UnlesbareAntwort_AendertNichts()
    {
        var r = ElmTransmitterClient.MitZeitvergleich(
            new ElmTransmitterClient.ElmCallResult(false, 500, 10, "<req/>", "kein XML", "HTTP 500"));
        Assert.Null(r.DiffSekunden);
        Assert.Equal("HTTP 500", r.Error);
    }

    [Fact]
    public void Toleranz_IstEineMinute()
        => Assert.Equal(60, ElmTransmitterClient.ZeitToleranzSekunden);
}

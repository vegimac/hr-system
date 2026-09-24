using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Swissdec Foundation-Test **F03 «Interoperabilität»** (Walter 24.09.2026).
///
/// Geprüft wird, dass wir die vorgegebenen Konstanten senden (F03_01), den zweiten
/// Operanden im richtigen Format übergeben (F03_02/F03_03) und eine verfälschte
/// Antwort ERKENNEN (F03_04/F03_05) — statt sie einfach zu glauben.
/// </summary>
public class ElmInteropTests
{
    /// <summary>Antwort der RefApps, mit einstellbaren Werten für die Fehlerfälle.</summary>
    private static string Antwort(string umlaute, string addition, string subtraktion,
                                  string umlautOk = "true", string firstOk = "true") => $"""
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
          <soap:Body>
            <ns6:CheckInteroperabilityResponse xmlns="urn:ch:swissdec:basis:v1:20260306:components" xmlns:ns6="urn:ch:swissdec:elm:v6:20260306:salarydeclaration:service:types">
              <UserAgent><Producer>swissdec</Producer><Name>swissdec refapps</Name><Version>4.0.88</Version><StandardVersion>6.0</StandardVersion><Certificate>swissdec</Certificate></UserAgent>
              <UmlautStringIsCorrect>{umlautOk}</UmlautStringIsCorrect>
              <FirstOperandIsCorrect>{firstOk}</FirstOperandIsCorrect>
              <UmlautString>{umlaute}</UmlautString>
              <AdditionResult>{addition}</AdditionResult>
              <SubtractionResult>{subtraktion}</SubtractionResult>
              <SystemDateTime>2026-09-24T11:08:59.123+02:00</SystemDateTime>
            </ns6:CheckInteroperabilityResponse>
          </soap:Body>
        </soap:Envelope>
        """;

    /// <summary>Eine in jeder Hinsicht korrekte Antwort zum gegebenen zweiten Operanden.</summary>
    private static string Korrekt(decimal zweiter) => Antwort(
        ElmInterop.UmlautErwartet,
        ElmInterop.Betrag(ElmInterop.FirstOperand + zweiter),
        ElmInterop.Betrag(ElmInterop.FirstOperand - zweiter));

    // ── F03_01 Konstanten ────────────────────────────────────────────────────

    [Fact]
    public void ErsterOperand_IstImmer999Milliarden()
    {
        // Laut XSD-Kommentar fest vorgegeben: «use following value for the
        // FirstOperand: 999000000000.00 (999 Milliarden)».
        Assert.Equal(999000000000.00m, ElmInterop.FirstOperand);
        Assert.Equal("999000000000.00", ElmInterop.Betrag(ElmInterop.FirstOperand));
    }

    [Fact]
    public void UmlautKette_EntsprichtDerVorgabe()
    {
        Assert.Equal("ÄËÖÜÁÉÓÚÀÈÒÙÂÊÔÛ", ElmInterop.UmlautString);
        Assert.Equal("äëöüáéóúàèòùâêôû", ElmInterop.UmlautErwartet);
        // Die Antwort ist dieselbe Kette in Kleinbuchstaben — sonst prüften wir das Falsche.
        Assert.Equal(ElmInterop.UmlautErwartet, ElmInterop.UmlautString.ToLowerInvariant());
    }

    // ── F03_03 Zahlformat ────────────────────────────────────────────────────

    [Theory]
    [InlineData("0.01", 0.01)]
    [InlineData("0", 0.00)]
    [InlineData("0,5", 0.50)]                       // Komma wie auf der Schweizer Tastatur
    [InlineData("-999'000'000'000", -999000000000)] // mit Tausendertrennern
    [InlineData(" 12.345 ", 12.35)]                 // dritte Stelle wird gerundet
    public void Eingabe_WirdAufZweiStellenGebracht(string eingabe, decimal erwartet)
    {
        var w = ElmInterop.LiesBetrag(eingabe);
        Assert.Equal(erwartet, w);
        Assert.True(ElmInterop.IstBetragsFormat(ElmInterop.Betrag(w!.Value)),
            "Gesendet wird immer mit genau zwei Nachkommastellen (F03_03).");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void UnlesbareEingabe_GibtNichts(string eingabe) => Assert.Null(ElmInterop.LiesBetrag(eingabe));

    [Theory]
    [InlineData("1.00", true)]
    [InlineData("-999000000000.00", true)]
    [InlineData("1.0", false)]    // eine Stelle zu wenig
    [InlineData("1.000", false)]
    [InlineData("1", false)]
    [InlineData("1,00", false)]   // Komma ist im XML nicht zulässig
    public void Betragsformat_FolgtDemSchema(string text, bool gueltig)
        => Assert.Equal(gueltig, ElmInterop.IstBetragsFormat(text));

    // ── F03_02 Die drei Prüfwerte ────────────────────────────────────────────

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.00)]
    [InlineData(-999000000000.00)]
    public void DieDreiPruefwerte_WerdenBestaetigt(decimal zweiter)
    {
        var b = ElmInterop.Pruefe(Korrekt(zweiter), zweiter);
        Assert.NotNull(b);
        Assert.True(b!.Ok, string.Join(" | ", b.Abweichungen));
        Assert.Empty(b.Abweichungen);
        Assert.Equal(999000000000.00m + zweiter, b.ErwarteteAddition);
        Assert.Equal(999000000000.00m - zweiter, b.ErwarteteSubtraktion);
    }

    [Fact]
    public void Vorschlaege_EnthaltenDieDreiGefordertenWerte()
        => Assert.Equal(new[] { 0.01m, 0.00m, -999000000000.00m }, ElmInterop.ZweiterOperandVorschlaege);

    [Fact]
    public void MinusDerGrossenZahl_ErgibtNullUndDasDoppelte()
    {
        // Der dritte Prüfwert ist der interessante: Addition 0.00, Subtraktion 1.998 Billionen.
        var b = ElmInterop.Pruefe(Korrekt(-999000000000.00m), -999000000000.00m)!;
        Assert.Equal(0.00m, b.ErwarteteAddition);
        Assert.Equal(1998000000000.00m, b.ErwarteteSubtraktion);
        Assert.True(b.Ok);
    }

    // ── F03_04 Verfälschte Umlaute ───────────────────────────────────────────

    [Fact]
    public void VerfaelschteUmlaute_WerdenErkannt()
    {
        var b = ElmInterop.Pruefe(Antwort("äëöüáéóúàèòùâêô?", "999000000000.01", "998999999999.99"), 0.01m)!;
        Assert.False(b.Ok);
        Assert.Contains(b.Abweichungen, t => t.Contains("Umlaute der Antwort"));
    }

    [Fact]
    public void FalschesEncoding_WirdErkannt()
    {
        // So sähe es aus, wenn unterwegs von UTF-8 nach Latin-1 gestolpert würde.
        var b = ElmInterop.Pruefe(Antwort("Ã¤Ã«Ã¶Ã¼", "999000000000.01", "998999999999.99"), 0.01m)!;
        Assert.False(b.Ok);
    }

    // ── F03_05 Verfälschte Zahlen ────────────────────────────────────────────

    [Fact]
    public void FalscheAddition_WirdErkannt()
    {
        var b = ElmInterop.Pruefe(Antwort(ElmInterop.UmlautErwartet, "999000000009.01", "998999999999.99"), 0.01m)!;
        Assert.False(b.Ok);
        Assert.Contains(b.Abweichungen, t => t.Contains("Addition stimmt nicht"));
    }

    [Fact]
    public void FalscheSubtraktion_WirdErkannt()
    {
        var b = ElmInterop.Pruefe(Antwort(ElmInterop.UmlautErwartet, "999000000000.01", "0.99"), 0.01m)!;
        Assert.False(b.Ok);
        Assert.Contains(b.Abweichungen, t => t.Contains("Subtraktion stimmt nicht"));
    }

    [Fact]
    public void AbgeschnitteneNachkommastellen_WerdenErkannt()
    {
        // Klassischer Zahlformat-Fehler: der Empfänger liefert «999000000000» ohne Rappen.
        var b = ElmInterop.Pruefe(Antwort(ElmInterop.UmlautErwartet, "999000000000", "998999999999.99"), 0.01m)!;
        Assert.False(b.Ok);
        Assert.Contains(b.Abweichungen, t => t.Contains("Zahlformat"));
    }

    [Fact]
    public void EmpfaengerMeldetFehler_WirdUebernommen()
    {
        var b = ElmInterop.Pruefe(Korrekt(0.01m).Replace(
            "<UmlautStringIsCorrect>true", "<UmlautStringIsCorrect>false"), 0.01m)!;
        Assert.False(b.Ok);
        Assert.False(b.UmlautBestaetigt);
        Assert.Contains(b.Abweichungen, t => t.Contains("Umlaute seien nicht korrekt"));
    }

    [Fact]
    public void EmpfaengerMeldetZahlFehler_WirdUebernommen()
    {
        var b = ElmInterop.Pruefe(Korrekt(0.01m).Replace(
            "<FirstOperandIsCorrect>true", "<FirstOperandIsCorrect>false"), 0.01m)!;
        Assert.False(b.Ok);
        Assert.False(b.FirstOperandBestaetigt);
    }

    // ── Kein Befund ohne Antwort ─────────────────────────────────────────────

    [Fact]
    public void Fault_GibtKeinenBefund()
    {
        const string fault = """
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>
              <soap:Fault><faultcode>soap:Client.security</faultcode><faultstring>security requirements not met</faultstring></soap:Fault>
            </soap:Body></soap:Envelope>
            """;
        Assert.Null(ElmInterop.Pruefe(fault, 0.01m));
    }

    [Theory]
    [InlineData("")]
    [InlineData("kein XML")]
    public void KeineAntwort_GibtKeinenBefund(string xml) => Assert.Null(ElmInterop.Pruefe(xml, 0.01m));
}

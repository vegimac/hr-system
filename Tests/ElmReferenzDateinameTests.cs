using HrSystem.Controllers;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Monat und Jahr aus dem Namen einer Swissdec-Referenz (Walter 27.09.2026).
/// Beide Schreibweisen kommen im Ordner SWISSCEC/RefXML nebeneinander vor —
/// wer sie falsch liest, vergleicht November gegen Dezember und erzeugt
/// hunderte Scheinunterschiede.
/// </summary>
public class ElmReferenzDateinameTests
{
    [Theory]
    [InlineData("RefXML_202411_MONTHLY.xml", 2024, 11)]
    [InlineData("RefXML_2024-12_MONTHLY.xml", 2024, 12)]
    [InlineData("RefXML_2026-02_MONTHLY.xml", 2026, 2)]
    [InlineData("refxml_2025-07_monthly.xml", 2025, 7)]
    [InlineData("RefXML_2025-01_MONTHLY (1).xml", 2025, 1)]
    public void LiestJahrUndMonat(string datei, int jahr, int monat)
        => Assert.Equal((jahr, monat), ElmController.MonatAusDateiname(datei));

    [Theory]
    [InlineData("RefXML_2025-01_RETROSPECTIVE.xml")]   // andere Meldungsart
    [InlineData("RefXML_2025-01_EMA.xml")]
    [InlineData("irgendwas.xml")]
    [InlineData("")]
    [InlineData("RefXML_2024-13_MONTHLY.xml")]         // Monat 13 gibt es nicht
    [InlineData("RefXML_1999-05_MONTHLY.xml")]         // vor unserem Bereich
    public void OhneErkennbarenMonatNull(string datei)
        => Assert.Equal((0, 0), ElmController.MonatAusDateiname(datei));
}

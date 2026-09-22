using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// AHV-Freibetrag kumuliert (Walter 21.09.2026, AHVV Art. 6quater, Swissdec TF16 Aebi):
/// 1'400 pro Beschäftigungsmonat ab Freibetrag-Beginn, angebrochene Monate voll; nicht
/// ausgeschöpfter Freibetrag wird innerhalb des Jahres nachgeholt.
/// </summary>
public class AhvFreibetragKumuliertTests
{
    [Fact]
    public void Aebi_JanuarOhneLohn_FebruarZweiFreibetraege()
    {
        // Wiedereintritt 15.1. ohne Lohn (Snapshot Basis 0), Honorar Februar 19'850.60
        var basis = PayrollCalculations.AhvFreibetragKumuliert(19850.60m, 1400m, ytdBasen: 0m, monateBisher: 1);
        Assert.Equal(17050.60m, basis);                       // Swissdec: AHV 903.68 = 17'050.60 × 5.3 %
        Assert.Equal(903.68m, Math.Round(basis * 0.053m, 2));
    }

    [Fact]
    public void NachzahlungNachAustritt_KeinNeuerFreibetragMonat()
    {
        // 2 Anstellungsmonate mit Freibetrag (2 × 1'400), Lohn je 2'000 → 1'200 verbeitragt.
        // Nachzahlung 5'000 nach Austritt: voll pflichtig (kein dritter Freibetrag).
        var basis = PayrollCalculations.AhvFreibetragKumuliert(5000m, 1400m, ytdBasen: 4000m, monateBisher: 2, neuerMonat: false);
        Assert.Equal(5000m, basis);
        // Nicht ausgeschöpfter Freibetrag der Anstellung wird auf der Nachzahlung nachgeholt.
        var basis2 = PayrollCalculations.AhvFreibetragKumuliert(5000m, 1400m, ytdBasen: 2000m, monateBisher: 2, neuerMonat: false);
        Assert.Equal(4200m, basis2);   // 7'000 − 2'800 = 4'200, bisher 0
    }

    [Fact]
    public void ErsterMonat_WieFlach()
    {
        Assert.Equal(600m, PayrollCalculations.AhvFreibetragKumuliert(2000m, 1400m, 0m, 0));   // Estermann
    }

    [Fact]
    public void RegelmaessigerLohn_JedenMonat1400()
    {
        Assert.Equal(600m, PayrollCalculations.AhvFreibetragKumuliert(2000m, 1400m, ytdBasen: 4000m, monateBisher: 2));
    }

    [Fact]
    public void MonatOhneLohn_NachGrossemMonat_Rueckerstattung()
    {
        // Vormonat 10'000 (pflichtig 8'600), dieser Monat 0 → Freibetrag des Monats nachgeholt
        Assert.Equal(-1400m, PayrollCalculations.AhvFreibetragKumuliert(0m, 1400m, ytdBasen: 10000m, monateBisher: 1));
    }

    [Fact]
    public void KleineLoehne_NieNegativUnterNull()
    {
        // Vormonat 1'000 (pflichtig 0), dieser Monat 1'000 → kumuliert 2'000 < 2'800 → 0
        Assert.Equal(0m, PayrollCalculations.AhvFreibetragKumuliert(1000m, 1400m, ytdBasen: 1000m, monateBisher: 1));
    }
}

public class MonateAlsTextTests
{
    [Fact] public void Zusammenhaengend() => Assert.Equal("Jan.–Feb.", PayrollCalculations.MonateAlsText(new[] { 1, 2 }));
    [Fact] public void MitLuecke() => Assert.Equal("Jan., März", PayrollCalculations.MonateAlsText(new[] { 1, 3 }));
    [Fact] public void Einzeln() => Assert.Equal("März", PayrollCalculations.MonateAlsText(new[] { 3 }));
}

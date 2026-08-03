using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// FLEX Ferien-Geld bei Bezug = Pott-Logik (Walter 01.08.2026):
/// Vormonats-Saldo Tage + Geld sind Pflicht; Auszahlung im Verhältnis Pott Tage.
/// </summary>
public class FlexFerienPottTests
{
    [Fact]
    public void CalcFerienGeld_Bezug_AusPottInklusiveAktuellemMonat()
    {
        // Wie Mirus/Walter: Vortr. Tage 10.12, Zuwachs 1.85 → Pott Tage 11.97
        // Vortr. Geld 800, Accrual 200 → Pott CHF 1000 → Tagessatz ≈ 83.54
        var lohnLines = new List<object>();
        decimal totalLohn = 0m;

        var (ausz, neu) = PayrollCalculations.CalcFerienGeld(
            prevGeld: 800m,
            accrual: 200m,
            prevTage: 10.12m,
            tageAccrual: 1.85m,
            tageGenommen: 6m,
            ref lohnLines,
            ref totalLohn,
            vacationPct: 10.64m,
            basis: 0m);

        // Tagessatz = 1000 / 11.97 ≈ 83.5422 → × 6 ≈ 501.25
        Assert.Equal(501.25m, ausz);
        Assert.Equal(498.75m, neu); // 1000 − 501.25
        Assert.Equal(501.25m, totalLohn);
        Assert.Single(lohnLines);
    }

    [Fact]
    public void CalcFerienGeld_OhneVormonatAberMitAccrual_BezugTrotzdem()
    {
        // Früherer Bug: nur prevTage > 0 → Bezug fehlte komplett.
        var lohnLines = new List<object>();
        decimal totalLohn = 0m;

        var (ausz, neu) = PayrollCalculations.CalcFerienGeld(
            prevGeld: 0m,
            accrual: 100m,
            prevTage: 0m,
            tageAccrual: 2m,
            tageGenommen: 1m,
            ref lohnLines,
            ref totalLohn,
            vacationPct: 10.64m,
            basis: 0m);

        Assert.Equal(50.00m, ausz); // 100/2 × 1
        Assert.Equal(50.00m, neu);
    }

    [Fact]
    public void CalcFerienGeld_CapAufPott_KeinVorbezug()
    {
        var lohnLines = new List<object>();
        decimal totalLohn = 0m;

        var (ausz, neu) = PayrollCalculations.CalcFerienGeld(
            prevGeld: 100m,
            accrual: 0m,
            prevTage: 2m,
            tageAccrual: 0m,
            tageGenommen: 10m, // mehr Tage als im Pott
            ref lohnLines,
            ref totalLohn,
            vacationPct: 10.64m,
            basis: 0m);

        Assert.Equal(100.00m, ausz);
        Assert.Equal(0.00m, neu);
    }

    [Fact]
    public void CalcFerienGeld_KeinBezug_NurAkkumulation()
    {
        var lohnLines = new List<object>();
        decimal totalLohn = 0m;

        var (ausz, neu) = PayrollCalculations.CalcFerienGeld(
            prevGeld: 500m,
            accrual: 80m,
            prevTage: 10.12m,
            tageAccrual: 1.85m,
            tageGenommen: 0m,
            ref lohnLines,
            ref totalLohn,
            vacationPct: 10.64m,
            basis: 0m);

        Assert.Equal(0m, ausz);
        Assert.Equal(580.00m, neu);
        Assert.Empty(lohnLines);
    }
}

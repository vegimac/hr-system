using Xunit;
using static HrSystem.Services.PayrollCalculations;

namespace HrSystem.Tests;

/// <summary>
/// Rappen-Rundung in der Lohnberechnung: kaufmännisch, die Hälfte immer AUFWÄRTS.
/// Anlass Walter 27.09.2026: `Math.Round(x, 2)` rundet in .NET «zur geraden Ziffer»
/// (Banker's Rounding). TF14 Egli, Oktober 2025, KTG 11: 1.309 % auf 12'500 = 163.625
/// ergab 163.62 statt 163.63 — und damit ein Netto von 2'038.90 statt 2'038.85.
/// </summary>
public class RappenRundungTests
{
    [Theory]
    [InlineData(163.625, 163.63)]   // TF14 Egli, KTG 11 auf 12'500
    [InlineData(47.355, 47.36)]
    [InlineData(19.635, 19.64)]
    [InlineData(163.615, 163.62)]   // Gegenprobe: Banker's haette hier auch 163.62 gegeben
    [InlineData(2.925, 2.93)]       // gilt auch fuer Stunden und Tage
    public void HaelfteImmerAufwaerts(decimal roh, decimal erwartet)
        => Assert.Equal(erwartet, Rappen(roh));

    [Theory]
    [InlineData(-163.625, -163.63)]
    [InlineData(-47.355, -47.36)]
    public void NegativeBetraegeRundenVomNullpunktWeg(decimal roh, decimal erwartet)
        => Assert.Equal(erwartet, Rappen(roh));

    [Fact]
    public void Egli_Oktober_KtgAufZwoelftausendfuenfhundert()
    {
        // 1.309 % auf 12'500.00 — genau ein halber Rappen.
        Assert.Equal(163.63m, Rappen(12500m * 1.309m / 100m));
    }

    [Fact]
    public void BankersRounding_WaereFalschGewesen()
    {
        Assert.Equal(163.62m, System.Math.Round(163.625m, 2));   // .NET-Standard
        Assert.NotEqual(System.Math.Round(163.625m, 2), Rappen(163.625m));
    }

    [Fact]
    public void Round05_BleibtFuerDenSchlussbetrag()
    {
        // Die 5-Rappen-Rundung des Nettos ist eine andere Stufe und bleibt.
        Assert.Equal(2038.85m, Round05(2038.87m));
        Assert.Equal(2038.90m, Round05(2038.88m));
    }
}

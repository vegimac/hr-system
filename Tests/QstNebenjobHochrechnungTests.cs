using HrSystem.Models;
using Xunit;
using static HrSystem.Services.PayrollCalculations;

namespace HrSystem.Tests;

/// <summary>
/// KS 45 Variante B: satzbestimmend = IST × min(Eigen+Andere, 100) / Eigen.
/// TF17 Binggeli Jan 2025: 70 % hier + 30 % andere AG = 100 % → 4'550 → 6'500.
/// </summary>
public class QstNebenjobHochrechnungTests
{
    private static EmployeeQuellensteuer Qst(decimal anderePct) => new()
    {
        WeitereBeschaftigungen = true,
        GesamtpensumWeitereAg = anderePct,
    };

    private static readonly CompanyProfile Firma = new();

    [Fact]
    public void Binggeli_70_plus_30_gibt_Vollpensum_6500()
    {
        var satz = ComputeSatzBruttoForNebenjob(Qst(30), 4550m, 0, Firma, pensumPct: 70);
        Assert.Equal(6500.00m, satz);
    }

    [Fact]
    public void Unter_100_Prozent_rechnet_auf_die_Summe()
    {
        var satz = ComputeSatzBruttoForNebenjob(Qst(20), 2000m, 0, Firma, pensumPct: 40);
        Assert.Equal(3000.00m, satz);
    }

    [Fact]
    public void Ueber_100_Prozent_deckt_auf_Vollpensum()
    {
        var satz = ComputeSatzBruttoForNebenjob(Qst(50), 2000m, 0, Firma, pensumPct: 60);
        Assert.Equal(3333.33m, satz);
    }

    [Fact]
    public void Arbenz_Bonus_bleibt_100_Prozent()
    {
        // 6'000 × 80/60 + 20'000 Bonus = 28'000, nicht 26'000 × 80/60.
        var satz = ComputeSatzBruttoForNebenjob(
            Qst(20), 26000m, 0, Firma, pensumPct: 60, einmaligNichtHochrechnen: 20000);
        Assert.Equal(28000.00m, satz);
    }

    [Fact]
    public void Ohne_Bonus_nur_Monatslohn_hochrechnen()
    {
        var satz = ComputeSatzBruttoForNebenjob(Qst(20), 6000m, 0, Firma, pensumPct: 60);
        Assert.Equal(8000.00m, satz);
    }
}

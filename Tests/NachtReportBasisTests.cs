using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Walter 02.08.2026: Ferien/Nacht-Liste — Vortrag 904 + Stempel ab Vortrags-Monat.
/// </summary>
public class NachtReportBasisTests
{
    [Fact]
    public void Ohne_Vortrag_Fenster_ab_Jahresbeginn()
    {
        var yearStart = new DateOnly(2026, 1, 1);
        var stich = new DateOnly(2026, 7, 31);
        var (from, vortrag) = PayrollCalculations.ResolveNachtReportBasis(yearStart, stich, null, 0.41m);
        Assert.Equal(yearStart, from);
        Assert.Equal(0m, vortrag);
    }

    [Fact]
    public void Vortrag_Juli_oeffnet_Fenster_ab_1_Juli()
    {
        var yearStart = new DateOnly(2026, 1, 1);
        var stich = new DateOnly(2026, 7, 31);
        var (from, vortrag) = PayrollCalculations.ResolveNachtReportBasis(
            yearStart, stich, "2026-07", 0.411666m);
        Assert.Equal(new DateOnly(2026, 7, 1), from);
        Assert.Equal(0.411666m, vortrag);
    }

    [Fact]
    public void Vortrag_nach_Stichtag_wird_ignoriert()
    {
        var yearStart = new DateOnly(2026, 1, 1);
        var stich = new DateOnly(2026, 6, 30);
        var (from, vortrag) = PayrollCalculations.ResolveNachtReportBasis(
            yearStart, stich, "2026-07", 0.41m);
        Assert.Equal(yearStart, from);
        Assert.Equal(0m, vortrag);
    }
}

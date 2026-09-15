using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Verzicht auf AHV-Freibetrag 1'400 (Walter 15.09.2026, TF44 Lusser März).
/// </summary>
public class AhvFreibetragVerzichtTests
{
    private static DeductionRule AhvMitFreibetrag() => new()
    {
        CategoryCode = "AHV",
        Name = "AHV / IV / EO (65+)",
        Type = "percent",
        Rate = 5.3m,
        FreibetragMonthly = 1400m,
    };

    [Fact]
    public void Verzicht_HebtFreibetragAuf_LaesstAlvUnangetastet()
    {
        var rules = new List<DeductionRule>
        {
            AhvMitFreibetrag(),
            new() { CategoryCode = "ALV", Rate = 1.1m, FreibetragMonthly = null },
        };
        var eintraege = new[]
        {
            new EmployeeVersicherungCode
            {
                Art = EmployeeVersicherungCode.ArtAhv,
                Code = EmployeeVersicherungCode.CodeFreibetragVerzicht,
                ValidFrom = new DateOnly(2025, 3, 1),
            },
        };

        PayrollCalculations.WendeAhvFreibetragVerzichtAn(rules, eintraege, ueberReferenzalter: true);

        Assert.Null(rules[0].FreibetragMonthly);
        Assert.True(rules[0].AhvFreibetragVerzicht);
        Assert.Equal("ALV", rules[1].CategoryCode);
        Assert.Null(rules[1].FreibetragMonthly);
        Assert.False(rules[1].AhvFreibetragVerzicht);
    }

    [Fact]
    public void OhneEintrag_FreibetragBleibt()
    {
        var rules = new List<DeductionRule> { AhvMitFreibetrag() };
        Assert.Equal(1400m, rules[0].FreibetragMonthly);
        Assert.False(rules[0].AhvFreibetragVerzicht);
    }

    [Fact]
    public void VorReferenzalter_WunschAendertNichts()
    {
        var rules = new List<DeductionRule> { AhvMitFreibetrag() };
        var eintraege = new[]
        {
            new EmployeeVersicherungCode
            {
                Art = EmployeeVersicherungCode.ArtAhv,
                Code = EmployeeVersicherungCode.CodeFreibetragVerzicht,
                ValidFrom = new DateOnly(2025, 1, 1),
            },
        };
        PayrollCalculations.WendeAhvFreibetragVerzichtAn(rules, eintraege, ueberReferenzalter: false);
        Assert.Equal(1400m, rules[0].FreibetragMonthly);
        Assert.False(rules[0].AhvFreibetragVerzicht);
    }
}

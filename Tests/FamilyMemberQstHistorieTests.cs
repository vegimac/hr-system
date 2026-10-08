using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Partner-Bewilligung/Erwerb am Stichtag: Wirkung + Wissen (Walter 08.10.2026).
/// </summary>
public class FamilyMemberQstHistorieTests
{
    static bool IstC(string? code) => string.Equals(code, "C", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void C_Ausweis_zaehlt_erst_ab_ErfahrenAm()
    {
        var spouse = new EmployeeFamilyMember { Id = 1, PermitTypeId = 9 };
        var hist = new List<FamilyMemberPermitHistory>
        {
            new()
            {
                Id = 1, FamilyMemberId = 1, PermitTypeId = 9,
                ValidFrom = new DateOnly(2026, 1, 1),
                ErfahrenAm = new DateOnly(2026, 6, 1),
                ValidTo = new DateOnly(2030, 1, 1),
                PermitType = new PermitType { Id = 9, Code = "C" },
            },
        };
        Assert.False(FamilyMemberQstHistorie.SpouseHatCAmStichtag(spouse, hist, new DateOnly(2026, 3, 1), IstC));
        Assert.True(FamilyMemberQstHistorie.SpouseHatCAmStichtag(spouse, hist, new DateOnly(2026, 6, 1), IstC));
    }

    [Fact]
    public void Erwerb_B_zu_C_erst_ab_Wissen()
    {
        var spouse = new EmployeeFamilyMember { Id = 1, Erwerbstaetig = true };
        var hist = new List<FamilyMemberErwerbHistory>
        {
            new() { Id = 1, FamilyMemberId = 1, Erwerbstaetig = false, ValidFrom = new DateOnly(2025, 1, 1) },
            new()
            {
                Id = 2, FamilyMemberId = 1, Erwerbstaetig = true,
                ValidFrom = new DateOnly(2026, 7, 28),
                ErfahrenAm = new DateOnly(2026, 9, 1),
            },
        };
        Assert.False(FamilyMemberQstHistorie.ErwerbstaetigAmStichtag(spouse, hist, new DateOnly(2026, 8, 1)));
        Assert.True(FamilyMemberQstHistorie.ErwerbstaetigAmStichtag(spouse, hist, new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public void Ohne_Historie_Snapshot_Fallback()
    {
        var spouse = new EmployeeFamilyMember
        {
            PermitType = new PermitType { Code = "C" },
            PermitExpiryDate = new DateTime(2030, 1, 1),
            Erwerbstaetig = true,
        };
        Assert.True(FamilyMemberQstHistorie.SpouseHatCAmStichtag(
            spouse, Array.Empty<FamilyMemberPermitHistory>(), new DateOnly(2026, 5, 1), IstC));
        Assert.True(FamilyMemberQstHistorie.ErwerbstaetigAmStichtag(
            spouse, Array.Empty<FamilyMemberErwerbHistory>(), new DateOnly(2026, 5, 1)));
    }
}

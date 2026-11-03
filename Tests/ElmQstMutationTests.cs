using HrSystem.Models;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>Gründe der QST-Mutation aus dem Tarifcode (RefXML Juni/Juli 2025).</summary>
public class ElmQstMutationTests
{
    [Theory]
    [InlineData("A0N", "B0N", "civilstate")]                  // TF23 Koller Heirat
    [InlineData("A0N", "C0N", "partnerWork,civilstate")]      // TF22 Bucher Heirat + Partner erwerbstätig
    [InlineData("B0Y", "C0Y", "partnerWork")]                 // TF36 Maldini August
    [InlineData("B0Y", "B1Y", "childrenDeduction")]           // TF33 Châtelain Kind
    [InlineData("A0N", "A0Y", "churchTax")]
    [InlineData("A0N", "A0N", "")]
    public void Gruende(string alt, string neu, string erwartet)
        => Assert.Equal(erwartet, string.Join(",", ElmMonthlyDeclarationBuilder.MutationsGruende(alt, neu)));
}

/// <summary>Reihenfolge der CorrectionMonth-Totale wie die Swissdec-Referenz (Java-HashMap).</summary>
public class ElmKorrekturReihenfolgeTests
{
    [Theory]
    [InlineData("2025-04,2025-05", "2025-05,2025-04")]   // RefXML Juni 2025
    [InlineData("2025-05,2025-06", "2025-05,2025-06")]   // RefXML Juli 2025
    public void Wie_Referenz(string ein, string aus)
        => Assert.Equal(aus, string.Join(",", ElmMonthlyDeclarationBuilder.JavaHashReihenfolge(ein.Split(','))));
}

/// <summary>Quality Tool Sep 2025: TF36 Maldini, Arbeitsort der Partnerin TI → EX ab 01.09.</summary>
public class ElmPartnerArbeitsortTests
{
    private static EmployeeFamilyMember Sandra() => new()
    {
        Id = 7, MemberType = "Ehepartner", FirstName = "Sandra", LastName = "Maldini", DateOfBirth = new DateTime(1997, 9, 27),
        Erwerbstaetig = true, ArbeitgeberKanton = "EX", Stellenantritt = new DateTime(2025, 7, 28),
    };

    private static List<FamilyMemberErwerbHistory> Verlauf() => new()
    {
        new() { Id = 1, FamilyMemberId = 7, Erwerbstaetig = true, ArbeitgeberKanton = "TI", ValidFrom = new DateOnly(2025, 7, 28) },
        new() { Id = 2, FamilyMemberId = 7, Erwerbstaetig = true, ArbeitgeberKanton = "EX", ValidFrom = new DateOnly(2025, 9, 1) },
    };

    [Theory]
    [InlineData(2025, 8, 31, "TI")]
    [InlineData(2025, 9, 30, "EX")]
    public void Arbeitsort_aus_dem_Verlauf(int j, int m, int t, string soll)
        => Assert.Equal(soll, ElmMonthlyDeclarationBuilder.PartnerArbeitsort(Sandra(), Verlauf(), new DateOnly(j, m, t)));

    [Fact]
    public void Ohne_Verlauf_gilt_das_Feld()
        => Assert.Equal("EX", ElmMonthlyDeclarationBuilder.PartnerArbeitsort(Sandra(), new List<FamilyMemberErwerbHistory>(), new DateOnly(2025, 8, 31)));

    [Fact]
    public void Ehepartner_meldet_Arbeitsort_am_Stichtag()
    {
        var e = new Employee { FirstName = "Fabio", LastName = "Maldini", Street = "Via Milano 26", ZipCode = "22100", City = "Como", Country = "IT" };
        var aug = ElmMonthlyDeclarationBuilder.Ehepartner(e, Sandra(), null, new List<string>(), new DateOnly(2025, 8, 31), "TI")!;
        Assert.Equal("TI", aug.Element(ElmGemeinsam.Sd + "WorkOrCompensatory")?.Element(ElmGemeinsam.Sd + "Workplace")?.Value);
    }
}

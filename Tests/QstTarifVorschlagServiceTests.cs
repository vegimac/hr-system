using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class QstTarifVorschlagServiceTests
{
    private static readonly DateOnly RefDate = new(2026, 6, 1);

    [Fact]
    public void LedigOhneKinder_SchlaegtA0NVor()
    {
        var emp = Emp(maritalStatus: "ledig", religion: "keine");
        var result = QstTarifVorschlagService.Build(
            emp,
            Array.Empty<EmployeeFamilyMember>(),
            Combos(("A", 0, false)),
            RefDate);

        Assert.Equal("A", result.TarifCode);
        Assert.Equal(0, result.AnzahlKinder);
        Assert.False(result.Kirchensteuer);
        Assert.Equal("A0N", result.QstCode);
        Assert.True(result.InTariftabelleGefunden);
    }

    [Fact]
    public void LedigMitQstBerechtigtemKindImHaushalt_SchlaegtHVor()
    {
        var emp = Emp(maritalStatus: "ledig", religion: "keine");
        var child = Kind(qstFrom: new DateTime(2026, 1, 1), qstUntil: new DateTime(2026, 12, 31));

        var result = QstTarifVorschlagService.Build(
            emp,
            new[] { child },
            Combos(("H", 1, false)),
            RefDate);

        Assert.Equal("H", result.TarifCode);
        Assert.Equal(1, result.AnzahlKinder);
        Assert.Equal(1, result.KinderImSelbenHaushalt);
        Assert.Equal("H1N", result.QstCode);
    }

    [Fact]
    public void KindAusserhalbHaushaltZaehltAberMachtNichtAlleinerziehend()
    {
        var emp = Emp(maritalStatus: "geschieden", religion: "keine");
        var child = Kind(qstFrom: new DateTime(2026, 1, 1), qstUntil: new DateTime(2026, 12, 31), alternativeAddressId: 7);

        var result = QstTarifVorschlagService.Build(
            emp,
            new[] { child },
            Combos(("A", 1, false), ("H", 1, false)),
            RefDate);

        Assert.Equal("A", result.TarifCode);
        Assert.Equal(1, result.AnzahlKinder);
        Assert.Equal(0, result.KinderImSelbenHaushalt);
        Assert.Equal("A1N", result.QstCode);
    }

    [Fact]
    public void VerheiratetMitKirchensteuerUndKindern_SchlaegtC2YVor()
    {
        var emp = Emp(maritalStatus: "verheiratet", religion: "roemisch_katholisch");
        var explicitChild = Kind(qstFrom: new DateTime(2026, 1, 1), qstUntil: new DateTime(2027, 1, 1));
        var fallbackChild = Kind(dateOfBirth: new DateTime(2015, 2, 3));

        var result = QstTarifVorschlagService.Build(
            emp,
            new[] { explicitChild, fallbackChild },
            Combos(("C", 2, true)),
            RefDate);

        Assert.Equal("C", result.TarifCode);
        Assert.Equal(2, result.AnzahlKinder);
        Assert.True(result.Kirchensteuer);
        Assert.Equal("C2Y", result.QstCode);
    }

    [Fact]
    public void ExpliziteQstKinderfristGewinntVorGeburtsdatumFallback()
    {
        var emp = Emp(maritalStatus: "ledig", religion: "keine");
        var validByExplicitPeriod = Kind(qstFrom: new DateTime(2026, 1, 1), qstUntil: new DateTime(2026, 12, 31));
        var expiredButUnder18 = Kind(
            dateOfBirth: new DateTime(2015, 1, 1),
            qstFrom: new DateTime(2024, 1, 1),
            qstUntil: new DateTime(2025, 12, 31));
        var validByAgeFallback = Kind(dateOfBirth: new DateTime(2014, 1, 1));

        var result = QstTarifVorschlagService.Build(
            emp,
            new[] { validByExplicitPeriod, expiredButUnder18, validByAgeFallback },
            Combos(("H", 2, false)),
            RefDate);

        Assert.Equal(2, result.BerechneteKinder);
        Assert.Equal("H2N", result.QstCode);
    }

    [Fact]
    public void WennKinderzahlInTabelleFehlt_NimmtHoechsteVorhandeneStufe()
    {
        var emp = Emp(maritalStatus: "ledig", religion: "keine");
        var children = Enumerable.Range(1, 10)
            .Select(i => Kind(dateOfBirth: new DateTime(2020, 1, Math.Min(i, 28))))
            .ToArray();

        var result = QstTarifVorschlagService.Build(
            emp,
            children,
            Combos(("H", 0, false), ("H", 9, false)),
            RefDate);

        Assert.Equal(10, result.BerechneteKinder);
        Assert.Equal(9, result.AnzahlKinder);
        Assert.Equal("H9N", result.QstCode);
        Assert.Contains(result.Warnings, w => w.Contains("10 Kindern"));
    }

    private static Employee Emp(string maritalStatus, string religion) => new()
    {
        Id = 123,
        CantonCode = "LU",
        MaritalStatus = maritalStatus,
        Religion = religion
    };

    private static EmployeeFamilyMember Kind(
        DateTime? dateOfBirth = null,
        DateTime? qstFrom = null,
        DateTime? qstUntil = null,
        int? alternativeAddressId = null) => new()
    {
        MemberType = "Kind",
        DateOfBirth = dateOfBirth,
        QstDeductibleFrom = qstFrom,
        QstDeductibleUntil = qstUntil,
        AlternativeAddressId = alternativeAddressId
    };

    private static IReadOnlyList<QstTarifInfo> Combos(params (string Tarif, int Kinder, bool Kirche)[] combos)
        => combos.Select(c => new QstTarifInfo("LU", c.Tarif, c.Kinder, c.Kirche)).ToList();
}

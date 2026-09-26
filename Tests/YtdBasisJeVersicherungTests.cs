using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Höchstlohn-Aufrollung je Versicherungsart auf der EIGENEN ungedeckelten Basis
/// (Walter 26.09.2026). Muster AG TF12 Casanova, Eintritt 27.02.2025:
/// Das EO-Taggeld im Juni (2'000) ist AHV-, aber nicht UVG-pflichtig — deshalb
/// laufen die Jahresbasen auseinander (per Juli AHV 63'700, UVG 61'700).
/// Kumulierter Höchstlohn bis August = 12'350 × (4/30 + 6) = 75'746.67.
/// Mit der UVG-Basis: August 14'046.67 → UVGZ 0.774 % = 108.72 (RefXML).
/// Mit der AHV-Basis als Proxy wären es 12'350.00 / 95.59 gewesen.
/// </summary>
public class YtdBasisJeVersicherungTests
{
    private static Employee Emp() => new() { Id = 12, FirstName = "Renato", LastName = "Casanova", Street = "Test", ZipCode = "6003", City = "Luzern" };
    private static Employment Contract() => new() { Id = 1, EmployeeId = 12, EmploymentModel = "FIX", MonthlySalary = 12000m, IsActive = true };
    private static CompanyProfile Company() => new() { Id = 1, CompanyName = "Muster AG", BranchName = "LU", Street = "Bahnhofstrasse", HouseNumber = "1", ZipCode = "6003", City = "Luzern" };

    private static SaldoBlock Saldo() => new(
        VormonatHourSaldo: 0, NeuerHourSaldo: 0, WorkedHours: 0, SollStunden: 0, Mehrstunden: 0, AbsenzGutschrift: 0,
        NightHours: 0, NightBonus: 0, NachtKompStunden: 0, VormonatNachtSaldo: 0, NeuerNachtSaldo: 0,
        VacationWeeks: 6, VormonatFerienTage: 0, FerienTageAccrual: 0, FerienTageGenommen: 0, FerienTageSaldoNeu: 0,
        VormonatFerienGeld: 0, FerienGeldSaldoNeu: 0, FerienGeldAuszahlung: 0,
        VormonatFeiertagTage: 0, FeiertagTageAccrual: 0, FeiertagTageGenommen: 0, FeiertagTageSaldoNeu: 0,
        ThirteenthPct: 0, PrevThirteenth: 0, Basis13ml: 0);

    /// <summary>UVGZ 11 «UVG-Lohn», Band 0–148'200/Jahr = 12'350/Mt., Satz 0.774 %.</summary>
    private static DeductionRule Uvgz(List<decimal>? ytdEigen) => new()
    {
        Id = -4, CompanyProfileId = 1, CategoryCode = "UVGZ", CategoryName = "UVGZ", Name = "UVG-Zusatz 11 — UVG-Lohn",
        Type = "percent", Rate = 0.774m, BasisType = "gross", MaxBaseMonthly = 12350m, IsActive = true,
        YtdBasenEigen = ytdEigen,
    };

    // Feb (Eintritt 27.02.) bis Juli, je Versicherungsart.
    private static readonly List<decimal> YtdAhv  = new() { 2450m, 12850m, 12850m, 12850m,  9850m, 12850m };
    private static readonly List<decimal> YtdUvg  = new() { 2450m, 12850m, 12850m, 12850m,  7850m, 12850m };
    private const decimal MonatAugust = 19850m;
    private const decimal MonateBisher = 4m / 30m + 5m;   // 27.02. → 4 Tage auf 30er-Basis + Mär–Jul

    private static Dictionary<string, object?> UvgzZeile(List<decimal>? ytdEigen)
    {
        var anon = PayrollCalculations.BuildResult(
            Emp(), Contract(), Company(), 2025, 8, new DateOnly(2025, 8, 1), new DateOnly(2025, 8, 31),
            new List<object>(), new List<object>(),
            new List<DeductionRule> { Uvgz(ytdEigen) },
            0m,
            new SvBases(MonatAugust, MonatAugust, MonatAugust, 0m, 0m),
            new List<object>(), 0, new List<object>(), 0, new List<object>(), 0, Saldo(),
            new List<EmployeeLohnAssignment>(), new List<EmployeeBankAccount>(),
            ytdSvBasesDezember: YtdAhv,                 // gemeinsame Liste = AHV (wie im Lohnlauf)
            ausgleichMonate: MonateBisher + 1m,
            ausgleichMonateBisher: MonateBisher);

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in anon.GetType().GetProperties()) dict[p.Name] = p.GetValue(anon);
        foreach (var l in (System.Collections.IEnumerable)dict["abzugLines"]!)
        {
            var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in l.GetType().GetProperties()) d[p.Name] = p.GetValue(l);
            if ((string?)d["categoryCode"] == "UVGZ") return d;
        }
        throw new InvalidOperationException("UVGZ-Zeile fehlt.");
    }

    [Fact]
    public void Casanova_August_MitUvgBasis()
    {
        var z = UvgzZeile(YtdUvg);
        Assert.Equal(14046.67m, (decimal)z["basis"]!);
        Assert.Equal(-108.72m, (decimal)z["betrag"]!);
    }

    [Fact]
    public void Casanova_August_MitAhvProxy_WaereFalsch()
    {
        // Gegenprobe: ohne eigene Basis greift die gemeinsame AHV-Liste — der alte Wert.
        var z = UvgzZeile(null);
        Assert.Equal(12350.00m, (decimal)z["basis"]!);
        Assert.Equal(-95.59m, (decimal)z["betrag"]!);
    }
}

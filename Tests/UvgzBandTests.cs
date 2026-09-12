using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Lohnband «ab» (Walter 07.09.2026) und kumulierte Höchstlohn-Methode (11.09.2026):
/// UVGZ Code 12 / KTG 12 = Überschusslohn 12'350–25'000/Mt. Unter dem Band → Basis 0,
/// Betrag 0, Zeile bleibt (ELM: contributory 0). Über dem Band → nur der Überschuss.
/// Summe der Abzugszeilen == totalAbzuege (Zeile und Total sind dieselbe Rechnung).
/// Swissdec TF40 Farine Jan 2025: Brutto 4'000 → UVGZ 12 Basis 0.00.
/// </summary>
public class UvgzBandTests
{
    private static Employee Emp() => new() { Id = 40, FirstName = "Corinne", LastName = "Farine", Street = "Test", ZipCode = "3000", City = "Bern" };
    private static Employment Contract() => new() { Id = 1, EmployeeId = 40, EmploymentModel = "FIX", MonthlySalary = 4000m, IsActive = true };
    private static CompanyProfile Company() => new() { Id = 1, CompanyName = "Muster AG", BranchName = "BE", Street = "Bahnhof", HouseNumber = "1", ZipCode = "3000", City = "Bern" };

    private static SaldoBlock Saldo() => new(
        VormonatHourSaldo: 0, NeuerHourSaldo: 0, WorkedHours: 0, SollStunden: 0, Mehrstunden: 0, AbsenzGutschrift: 0,
        NightHours: 0, NightBonus: 0, NachtKompStunden: 0, VormonatNachtSaldo: 0, NeuerNachtSaldo: 0,
        VacationWeeks: 5, VormonatFerienTage: 0, FerienTageAccrual: 0, FerienTageGenommen: 0, FerienTageSaldoNeu: 0,
        VormonatFerienGeld: 0, FerienGeldSaldoNeu: 0, FerienGeldAuszahlung: 0,
        VormonatFeiertagTage: 0, FeiertagTageAccrual: 0, FeiertagTageGenommen: 0, FeiertagTageSaldoNeu: 0,
        ThirteenthPct: 0, PrevThirteenth: 0, Basis13ml: 0);

    private static DeductionRule Uvgz12() => new()
    {
        Id = -1, CompanyProfileId = 1, CategoryCode = "UVGZ", CategoryName = "UVG-Zusatz", Name = "UVG-Zusatz 12 — Überschusslohn",
        Type = "percent", Rate = 0.508m, BasisType = "gross", BandVonMonthly = 12350m, MaxBaseMonthly = 25000m, IsActive = true,
    };
    private static DeductionRule Ktg12() => new()
    {
        Id = -2, CompanyProfileId = 1, CategoryCode = "KTG", CategoryName = "KTG", Name = "KTG 12 — Überschusslohn",
        Type = "percent", Rate = 1.0m, BasisType = "gross", BandVonMonthly = 12350m, MaxBaseMonthly = 25000m, IsActive = true,
    };

    private static (List<Dictionary<string, object?>> Lines, decimal Total) Run(decimal brutto, List<decimal>? ytd = null, decimal monate = 12m, decimal bisher = -1m)
    {
        var anon = PayrollCalculations.BuildResult(
            Emp(), Contract(), Company(), 2025, 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31),
            new List<object>(), new List<object>(),
            new List<DeductionRule> { Uvgz12(), Ktg12() }, brutto,
            new SvBases(brutto, brutto, brutto, brutto, brutto),
            new List<object>(), 0, new List<object>(), 0, new List<object>(), 0, Saldo(),
            new List<EmployeeLohnAssignment>(), new List<EmployeeBankAccount>(),
            ytdSvBasesDezember: ytd, ausgleichMonate: monate, ausgleichMonateBisher: bisher);
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in anon.GetType().GetProperties()) dict[p.Name] = p.GetValue(anon);
        var lines = new List<Dictionary<string, object?>>();
        foreach (var l in (System.Collections.IEnumerable)dict["abzugLines"]!)
        {
            var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in l.GetType().GetProperties()) d[p.Name] = p.GetValue(l);
            lines.Add(d);
        }
        return (lines, (decimal)dict["totalAbzuege"]!);
    }

    private static Dictionary<string, object?> Line(List<Dictionary<string, object?>> lines, string cat)
        => lines.First(l => (string?)l["categoryCode"] == cat);

    [Fact]
    public void UnterDemBand_BasisNull_BetragNull_ZeileBleibt()
    {
        var (lines, total) = Run(4000m, ytd: new List<decimal>(), monate: 1m, bisher: 0m);
        var uvgz = Line(lines, "UVGZ");
        Assert.Equal(0m, (decimal)uvgz["basis"]!);
        Assert.Equal(0m, (decimal)uvgz["betrag"]!);
        var ktg = Line(lines, "KTG");
        Assert.Equal(0m, (decimal)ktg["basis"]!);
        Assert.Equal(0m, (decimal)ktg["betrag"]!);
        Assert.Equal(0m, total);
    }

    [Fact]
    public void UeberDemBand_NurUeberschuss_SummeGleichTotal()
    {
        var (lines, total) = Run(15000m, ytd: new List<decimal>(), monate: 1m, bisher: 0m);
        var uvgz = Line(lines, "UVGZ");
        Assert.Equal(2650m, (decimal)uvgz["basis"]!);
        Assert.Equal(-Math.Round(2650m * 0.508m / 100m, 2), (decimal)uvgz["betrag"]!);
        decimal summe = lines.Sum(l => (decimal)l["betrag"]!);
        Assert.Equal(summe, total);
    }

    [Fact]
    public void FlachOhneKumulation_Gleich()
    {
        // ohne ytd (null) → flaches Band wie bisher
        var (lines, total) = Run(15000m);
        Assert.Equal(2650m, (decimal)Line(lines, "UVGZ")["basis"]!);
        Assert.Equal(lines.Sum(l => (decimal)l["betrag"]!), total);
    }

    [Fact]
    public void Kumuliert_Teilmonat_AebiDezember()
    {
        // Nov 12'958.35 (voll), Dez 9'395 bei Austritt 20.12. → Höchstlohn 20/30 → Überschuss 1'161.67
        var (lines, _) = Run(9395m, ytd: new List<decimal> { 12958.35m }, monate: 1m + 20m / 30m, bisher: 1m);
        Assert.Equal(1161.67m, (decimal)Line(lines, "UVGZ")["basis"]!);
    }
}

using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Nachzahlung nach Austritt: Höchstlohn-Aufrollung darf unter 0 (Rückerstattung).
/// TF07 Burri Feb 2025: Jan-Überzeit 15'000 + Nov/Dez 8'000/8'000, dann −9'500
/// → ALV-Basis −3'200 (24'700 schon verbeitragt, YTD-AHV jetzt 21'500).
/// </summary>
public class NachzahlungHoechstlohnTests
{
    private static Employee Emp() => new() { Id = 7, FirstName = "Heidi", LastName = "Burri", Street = "Test", ZipCode = "3000", City = "Bern" };
    private static Employment Contract() => new() { Id = 1, EmployeeId = 7, EmploymentModel = "FIX", MonthlySalary = 8000m, IsActive = false };
    private static CompanyProfile Company() => new() { Id = 1, CompanyName = "Muster AG", BranchName = "BE", Street = "Bahnhof", HouseNumber = "1", ZipCode = "3000", City = "Bern" };

    private static SaldoBlock Saldo() => new(
        VormonatHourSaldo: 0, NeuerHourSaldo: 0, WorkedHours: 0, SollStunden: 0, Mehrstunden: 0, AbsenzGutschrift: 0,
        NightHours: 0, NightBonus: 0, NachtKompStunden: 0, VormonatNachtSaldo: 0, NeuerNachtSaldo: 0,
        VacationWeeks: 5, VormonatFerienTage: 0, FerienTageAccrual: 0, FerienTageGenommen: 0, FerienTageSaldoNeu: 0,
        VormonatFerienGeld: 0, FerienGeldSaldoNeu: 0, FerienGeldAuszahlung: 0,
        VormonatFeiertagTage: 0, FeiertagTageAccrual: 0, FeiertagTageGenommen: 0, FeiertagTageSaldoNeu: 0,
        ThirteenthPct: 0, PrevThirteenth: 0, Basis13ml: 0);

    private static DeductionRule Alv() => new()
    {
        Id = -1, CompanyProfileId = 1, CategoryCode = "ALV", CategoryName = "ALV", Name = "Arbeitslosenversicherung",
        Type = "percent", Rate = 1.1m, BasisType = "gross", MaxBaseMonthly = 12350m, IsActive = true,
    };

    private static DeductionRule Alvz() => new()
    {
        Id = -2, CompanyProfileId = 1, CategoryCode = "ALVZ", CategoryName = "ALVZ", Name = "ALV Solidaritätsprozent",
        Type = "percent", Rate = 0.5m, BasisType = "gross", BandVonMonthly = 12350m, MaxBaseMonthly = 30875m, IsActive = true,
    };

    [Fact]
    public void Burri_Feb_gibt_ALV_und_ALVZ_anteil_zurueck()
    {
        var anon = PayrollCalculations.BuildResult(
            Emp(), Contract(), Company(), 2025, 2, new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28),
            new List<object>(), new List<object>(),
            new List<DeductionRule> { Alv(), Alvz() },
            0m,
            new SvBases(-9500m, -9500m, 0m, 0m, 0m),
            new List<object>(), 0, new List<object>(), 0, new List<object>(), 0, Saldo(),
            new List<EmployeeLohnAssignment>(), new List<EmployeeBankAccount>(),
            ytdSvBasesDezember: new List<decimal> { 8000m, 8000m, 15000m },
            ausgleichMonate: 2m,
            ausgleichMonateBisher: 2m,
            ausgleichLabel: " (Nachzahlung, Austrittsjahr 2024)");

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in anon.GetType().GetProperties()) dict[p.Name] = p.GetValue(anon);
        var lines = new List<Dictionary<string, object?>>();
        foreach (var l in (System.Collections.IEnumerable)dict["abzugLines"]!)
        {
            var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in l.GetType().GetProperties()) d[p.Name] = p.GetValue(l);
            lines.Add(d);
        }

        var alv = lines.First(l => (string?)l["categoryCode"] == "ALV");
        var alvz = lines.First(l => (string?)l["categoryCode"] == "ALVZ");
        Assert.Equal(-3200m, (decimal)alv["basis"]!);
        Assert.Equal(35.20m, (decimal)alv["betrag"]!);
        Assert.Equal(-6300m, (decimal)alvz["basis"]!);
        Assert.Equal(31.50m, (decimal)alvz["betrag"]!);
    }
}

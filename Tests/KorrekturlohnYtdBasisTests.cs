using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Nachzahlung nach Austritt: die gedeckelten Abzuege rollen je Versicherungsart auf
/// IHRER eigenen Jahresbasis auf (Walter 27.09.2026). Muster AG TF01 Monica Herz,
/// Austritt 31.03.2025, Gratifikation 20'000 im Oktober.
/// Jahresbasen per September: AHV 35'359.30, UVG 32'859.30 (2'500 sind AHV-, aber
/// nicht UVG-pflichtig). Deckel = 12'350 x 3 Beschaeftigungsmonate = 37'050.
/// Mit der UVG-Basis: 37'050 − 32'859.30 = 4'190.70 → NBU 1.606 % = 67.30 (RefXML:
/// AHV+ALV+NBU zusammen 1'237.45). Mit der AHV-Basis als Ersatz waeren es 1'690.70
/// und 27.15 gewesen — so rechnete der Korrekturlohn vor diesem Fix.
/// </summary>
public class KorrekturlohnYtdBasisTests
{
    private static Employee Emp() => new() { Id = 1, FirstName = "Monica", LastName = "Herz", Street = "Test", ZipCode = "6003", City = "Luzern" };
    private static Employment Contract() => new() { Id = 1, EmployeeId = 1, EmploymentModel = "FLEX", HourlyRate = 35m, IsActive = false };
    private static CompanyProfile Company() => new() { Id = 1, CompanyName = "Muster AG", BranchName = "LU", Street = "Bahnhofstrasse", HouseNumber = "1", ZipCode = "6003", City = "Luzern" };

    private static SaldoBlock Saldo() => new(
        VormonatHourSaldo: 0, NeuerHourSaldo: 0, WorkedHours: 0, SollStunden: 0, Mehrstunden: 0, AbsenzGutschrift: 0,
        NightHours: 0, NightBonus: 0, NachtKompStunden: 0, VormonatNachtSaldo: 0, NeuerNachtSaldo: 0,
        VacationWeeks: 5, VormonatFerienTage: 0, FerienTageAccrual: 0, FerienTageGenommen: 0, FerienTageSaldoNeu: 0,
        VormonatFerienGeld: 0, FerienGeldSaldoNeu: 0, FerienGeldAuszahlung: 0,
        VormonatFeiertagTage: 0, FeiertagTageAccrual: 0, FeiertagTageGenommen: 0, FeiertagTageSaldoNeu: 0,
        ThirteenthPct: 0, PrevThirteenth: 0, Basis13ml: 0);

    /// <summary>UVG Betriebsteil A, NBU-Abzug AN 1.606 %, Hoechstlohn 12'350/Mt.</summary>
    private static DeductionRule Nbu(List<decimal>? ytdEigen) => new()
    {
        Id = -3, CompanyProfileId = 1, CategoryCode = "NBUV", CategoryName = "NBUV",
        Name = "UVG Betriebsteil A — BU+NBU, NBU-Abzug AN",
        Type = "percent", Rate = 1.606m, BasisType = "gross", MaxBaseMonthly = 12350m, IsActive = true,
        YtdBasenEigen = ytdEigen,
    };

    private static readonly List<decimal> YtdAhv = new() { 12350m, 12350m, 10659.30m };   // Summe 35'359.30
    private static readonly List<decimal> YtdUvg = new() { 12350m, 12350m,  8159.30m };   // Summe 32'859.30
    private const decimal Nachzahlung = 20000m;
    private const decimal Monate = 3m;   // Januar bis Austritt 31.03., die Nachzahlung bringt keinen Monat

    private static Dictionary<string, object?> NbuZeile(List<decimal>? ytdEigen)
    {
        var anon = PayrollCalculations.BuildResult(
            Emp(), Contract(), Company(), 2025, 10, new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31),
            new List<object>(), new List<object>(),
            new List<DeductionRule> { Nbu(ytdEigen) },
            0m,
            new SvBases(Nachzahlung, Nachzahlung, Nachzahlung, 0m, 0m),
            new List<object>(), 0, new List<object>(), 0, new List<object>(), 0, Saldo(),
            new List<EmployeeLohnAssignment>(), new List<EmployeeBankAccount>(),
            ytdSvBasesDezember: YtdAhv,
            ausgleichMonate: Monate,
            ausgleichMonateBisher: Monate);

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in anon.GetType().GetProperties()) dict[p.Name] = p.GetValue(anon);
        foreach (var l in (System.Collections.IEnumerable)dict["abzugLines"]!)
        {
            var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in l.GetType().GetProperties()) d[p.Name] = p.GetValue(l);
            if ((string?)d["categoryCode"] == "NBUV") return d;
        }
        throw new InvalidOperationException("NBUV-Zeile fehlt.");
    }

    [Fact]
    public void Herz_Oktober_MitUvgBasis()
    {
        var z = NbuZeile(YtdUvg);
        Assert.Equal(4190.70m, (decimal)z["basis"]!);
        Assert.Equal(-67.30m, (decimal)z["betrag"]!);
    }

    [Fact]
    public void Herz_Oktober_MitAhvProxy_WaereFalsch()
    {
        var z = NbuZeile(null);
        Assert.Equal(1690.70m, (decimal)z["basis"]!);
        Assert.Equal(-27.15m, (decimal)z["betrag"]!);
    }
}

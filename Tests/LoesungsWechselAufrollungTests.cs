using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Wechsel der Versicherungslösung mitten im Jahr — die Aufrollung des kumulierten
/// Höchstlohns darf nur die Monate zählen, in denen die Lösung galt
/// (Walter-Fall 23.09.2026, Muster AG TF03 Pia Lusser, Juli 2025).
///
/// Sie hat KTG 12 bis 31.05. und KTG 11 ab 01.06. Die YTD-AHV-Basis Januar–Juni
/// beträgt 69'400 (inkl. Dienstaltersgeschenk 34'000 im Februar). Ohne die
/// Einschränkung rollte KTG 11 über alle 7 Monate auf und landete bei einer Basis
/// von 10'000 (= 70'000 − 60'000) statt bei den 1'500 des Julilohns.
/// </summary>
public class LoesungsWechselAufrollungTests
{
    private static Employee Emp() => new() { Id = 3, FirstName = "Pia", LastName = "Lusser", Gender = "F", Street = "Buochserstrasse", ZipCode = "6370", City = "Stans" };
    private static Employment Contract() => new() { Id = 1, EmployeeId = 3, EmploymentModel = "FIX", MonthlySalary = 1500m, IsActive = true };
    private static CompanyProfile Company() => new() { Id = 1, CompanyName = "Muster AG", BranchName = "LU", Street = "Bahnhofstrasse", HouseNumber = "1", ZipCode = "6003", City = "Luzern" };

    private static SaldoBlock Saldo() => new(
        VormonatHourSaldo: 0, NeuerHourSaldo: 0, WorkedHours: 0, SollStunden: 0, Mehrstunden: 0, AbsenzGutschrift: 0,
        NightHours: 0, NightBonus: 0, NachtKompStunden: 0, VormonatNachtSaldo: 0, NeuerNachtSaldo: 0,
        VacationWeeks: 6, VormonatFerienTage: 0, FerienTageAccrual: 0, FerienTageGenommen: 0, FerienTageSaldoNeu: 0,
        VormonatFerienGeld: 0, FerienGeldSaldoNeu: 0, FerienGeldAuszahlung: 0,
        VormonatFeiertagTage: 0, FeiertagTageAccrual: 0, FeiertagTageGenommen: 0, FeiertagTageSaldoNeu: 0,
        ThirteenthPct: 0, PrevThirteenth: 0, Basis13ml: 0);

    /// <summary>KTG 11 «KTG-Lohn», Frauensatz 1.309 %, Höchstlohn 120'000/Jahr = 10'000/Mt.</summary>
    private static DeductionRule Ktg11() => new()
    {
        Id = -11, CompanyProfileId = 1, CategoryCode = "KTG", CategoryName = "KTG", Name = "KTG 11 — KTG-Lohn (F)",
        Type = "percent", Rate = 1.309m, BasisType = "gross", MaxBaseMonthly = 10000m,
        LoesungsCode = "11", IsActive = true,
    };

    /// <summary>YTD-AHV-Basen Januar–Juni (Februar mit Dienstaltersgeschenk 34'000).</summary>
    private static List<decimal> YtdJanBisJuni() => new() { 5500m, 42700m, 5500m, 5500m, 5500m, 4700m };

    private static decimal KtgBasis(DeductionRule regel)
    {
        var anon = PayrollCalculations.BuildResult(
            Emp(), Contract(), Company(), 2025, 7, new DateOnly(2025, 7, 1), new DateOnly(2025, 7, 31),
            new List<object>(), new List<object>(),
            new List<DeductionRule> { regel },
            0m,
            new SvBases(1500m, 1500m, 1500m, 0m, 0m),
            new List<object>(), 0, new List<object>(), 0, new List<object>(), 0, Saldo(),
            new List<EmployeeLohnAssignment>(), new List<EmployeeBankAccount>(),
            ytdSvBasesDezember: YtdJanBisJuni(),
            ausgleichMonate: 7m,
            ausgleichMonateBisher: 6m);

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in anon.GetType().GetProperties()) dict[p.Name] = p.GetValue(anon);
        foreach (var l in (System.Collections.IEnumerable)dict["abzugLines"]!)
        {
            var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in l.GetType().GetProperties()) d[p.Name] = p.GetValue(l);
            if ((d["categoryCode"] as string) == "KTG") return Convert.ToDecimal(d["basis"]);
        }
        throw new Xunit.Sdk.XunitException("Keine KTG-Zeile im Ergebnis.");
    }

    [Fact]
    public void OhneWechsel_RolltUeberDasGanzeJahr_auf()
    {
        // Gegenprobe: gilt die Lösung schon länger, bleibt die bisherige Rechnung.
        // 70'000 (7 Mt. × 10'000) − 60'000 (6 Mt.) = 10'000.
        Assert.Equal(10000m, KtgBasis(Ktg11()));
    }

    [Fact]
    public void MitWechselImJuni_ZaehltNurJuniUndJuli()
    {
        var regel = Ktg11();
        regel.LoesungAb                  = new DateOnly(2025, 6, 1);
        regel.YtdBasenEigen              = new List<decimal> { 4700m };   // nur Juni
        regel.AusgleichMonateBisherEigen = 1m;
        regel.AusgleichMonateEigen       = 2m;

        // min(4'700 + 1'500, 20'000) − min(4'700, 10'000) = 1'500 → 19.64 statt 130.90.
        Assert.Equal(1500m, KtgBasis(regel));
    }

    [Fact]
    public void MitWechsel_DeckeltImmerNochAmHoechstlohn()
    {
        // Sicherung: der Höchstlohn wirkt innerhalb der neuen Lösung weiter.
        // Juni-Basis 25'000 → kumuliert bisher 10'000, total 20'000 → diesen Monat 10'000.
        var regel = Ktg11();
        regel.LoesungAb                  = new DateOnly(2025, 6, 1);
        regel.YtdBasenEigen              = new List<decimal> { 25000m };
        regel.AusgleichMonateBisherEigen = 1m;
        regel.AusgleichMonateEigen       = 2m;

        Assert.Equal(10000m, KtgBasis(regel));
    }
}

using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Zieht eine Korrektur die KUMULIERTE Basis unter null, muss der gedeckelte Abzug
/// den vollen negativen Betrag zurückgeben — wie die ungedeckelte AHV im selben Beleg
/// (Walter 26.09.2026). Muster AG TF44 Hans Lusser, August 2025: Monatslohn 1'500,
/// Unfall-Taggeld 28'000 und Korrektur Taggeld −28'000 → Monatsbasis −26'500 bei
/// 7'500 YTD (KTG 11 seit März). RefXML meldet KTG −26'500; vorher kappte
/// «Math.Max(0, …)» auf −7'500 (Rückerstattung 72.45 statt 255.99).
/// </summary>
public class KorrekturNegativeJahresbasisTests
{
    private static Employee Emp() => new() { Id = 44, FirstName = "Hans", LastName = "Lusser", Street = "Test", ZipCode = "6000", City = "Luzern" };
    private static Employment Contract() => new() { Id = 1, EmployeeId = 44, EmploymentModel = "FIX", MonthlySalary = 1500m, IsActive = true };
    private static CompanyProfile Company() => new() { Id = 1, CompanyName = "Muster AG", BranchName = "LU", Street = "Bahnhofstrasse", HouseNumber = "1", ZipCode = "6003", City = "Luzern" };

    private static SaldoBlock Saldo() => new(
        VormonatHourSaldo: 0, NeuerHourSaldo: 0, WorkedHours: 0, SollStunden: 0, Mehrstunden: 0, AbsenzGutschrift: 0,
        NightHours: 0, NightBonus: 0, NachtKompStunden: 0, VormonatNachtSaldo: 0, NeuerNachtSaldo: 0,
        VacationWeeks: 6, VormonatFerienTage: 0, FerienTageAccrual: 0, FerienTageGenommen: 0, FerienTageSaldoNeu: 0,
        VormonatFerienGeld: 0, FerienGeldSaldoNeu: 0, FerienGeldAuszahlung: 0,
        VormonatFeiertagTage: 0, FeiertagTageAccrual: 0, FeiertagTageGenommen: 0, FeiertagTageSaldoNeu: 0,
        ThirteenthPct: 0, PrevThirteenth: 0, Basis13ml: 0);

    /// <summary>KTG 11 «KTG-Lohn», Lohnband 0–120'000/Jahr = 10'000/Mt., Satz Mann 0.966 %.</summary>
    private static DeductionRule Ktg() => new()
    {
        Id = -3, CompanyProfileId = 1, CategoryCode = "KTG", CategoryName = "KTG", Name = "KTG 11 — KTG-Lohn (M)",
        Type = "percent", Rate = 0.966m, BasisType = "gross", MaxBaseMonthly = 10000m, IsActive = true,
        // Lösung gilt seit März → eigene Aufroll-Basis: Mär–Jul je 1'500.
        YtdBasenEigen = new List<decimal> { 1500m, 1500m, 1500m, 1500m, 1500m },
        AusgleichMonateBisherEigen = 5m,
        AusgleichMonateEigen = 6m,
    };

    private static Dictionary<string, object?> AbzugZeile(object anon, string categoryCode)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in anon.GetType().GetProperties()) dict[p.Name] = p.GetValue(anon);
        foreach (var l in (System.Collections.IEnumerable)dict["abzugLines"]!)
        {
            var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in l.GetType().GetProperties()) d[p.Name] = p.GetValue(l);
            if ((string?)d["categoryCode"] == categoryCode) return d;
        }
        throw new InvalidOperationException($"Abzugszeile {categoryCode} fehlt.");
    }

    private static object Rechne(decimal monatsBasis, DeductionRule regel) =>
        PayrollCalculations.BuildResult(
            Emp(), Contract(), Company(), 2025, 8, new DateOnly(2025, 8, 1), new DateOnly(2025, 8, 31),
            new List<object>(), new List<object>(),
            new List<DeductionRule> { regel },
            0m,
            new SvBases(monatsBasis, monatsBasis, monatsBasis, 0m, 0m),
            new List<object>(), 0, new List<object>(), 0, new List<object>(), 0, Saldo(),
            new List<EmployeeLohnAssignment>(), new List<EmployeeBankAccount>());

    [Fact]
    public void Lusser_August_gibt_die_volle_negative_Basis_zurueck()
    {
        var ktg = AbzugZeile(Rechne(-26500m, Ktg()), "KTG");
        Assert.Equal(-26500m, (decimal)ktg["basis"]!);
        Assert.Equal(255.99m, (decimal)ktg["betrag"]!);   // positiv = Rückerstattung
    }

    [Fact]
    public void PositiveBasis_bleibt_gedeckelt()
    {
        // Gegenprobe: ohne Korrektur greift der Monats-Höchstlohn unverändert.
        // YTD 7'500 (5 Mt.), Monat 30'000 → kumuliert min(37'500, 10'000 × 6) = 60'000
        // → 37'500 − 7'500 = 30'000? Nein: Deckel 60'000 greift nicht, aber der
        // Monatslohn liegt über dem Monatsdeckel — die Aufrollung lässt ihn zu,
        // solange der Jahresdeckel nicht erreicht ist (Swissdec-Aufrollmethode).
        var ktg = AbzugZeile(Rechne(30000m, Ktg()), "KTG");
        Assert.Equal(30000m, (decimal)ktg["basis"]!);
    }

    [Fact]
    public void JahresdeckelBleibtWirksam()
    {
        // Monat 60'000 auf YTD 7'500: kumuliert min(67'500, 60'000) = 60'000
        // → Basis 60'000 − 7'500 = 52'500, nicht 60'000.
        var ktg = AbzugZeile(Rechne(60000m, Ktg()), "KTG");
        Assert.Equal(52500m, (decimal)ktg["basis"]!);
    }
}

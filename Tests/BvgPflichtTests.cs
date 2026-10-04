using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// BVG versichert ja/nein pro Person (Walter 04.10.2026). Anlass Blerta Sela,
/// Langenthal Drive: Mirus zieht jeden Monat 22.05 (7 % × 315), auch im Februar
/// mit nur 709.60 Lohn — massgebend ist der Jahreslohn, nicht der Monat.
/// </summary>
public class BvgPflichtTests
{
    private const decimal Schwelle = 22680m;

    private static Employee Emp() => new() { Id = 7, FirstName = "Blerta", LastName = "Sela", Street = "Weg 1", ZipCode = "4900", City = "Langenthal" };
    private static Employment Contract() => new() { Id = 1, EmployeeId = 7, EmploymentModel = "FLEX", IsActive = true };
    private static CompanyProfile Company() => new() { Id = 1, CompanyName = "Schaub", BranchName = "LD", Street = "X", HouseNumber = "1", ZipCode = "4900", City = "Langenthal" };

    private static SaldoBlock Saldo() => new(
        VormonatHourSaldo: 0, NeuerHourSaldo: 0, WorkedHours: 0, SollStunden: 0, Mehrstunden: 0, AbsenzGutschrift: 0,
        NightHours: 0, NightBonus: 0, NachtKompStunden: 0, VormonatNachtSaldo: 0, NeuerNachtSaldo: 0,
        VacationWeeks: 5, VormonatFerienTage: 0, FerienTageAccrual: 0, FerienTageGenommen: 0, FerienTageSaldoNeu: 0,
        VormonatFerienGeld: 0, FerienGeldSaldoNeu: 0, FerienGeldAuszahlung: 0,
        VormonatFeiertagTage: 0, FeiertagTageAccrual: 0, FeiertagTageGenommen: 0, FeiertagTageSaldoNeu: 0,
        ThirteenthPct: 0, PrevThirteenth: 0, Basis13ml: 0);

    private static DeductionRule Bvg(bool? versichert) => new()
    {
        Id = -5, CompanyProfileId = 1, CategoryCode = "BVG", CategoryName = "BVG", Name = "GastroSocial Uno Basis",
        Type = "percent", Rate = 7m, RateEmployer = 7.1m, BasisType = "bvg_basis",
        CoordinationDeduction = 2205m, MinBaseMonthly = 315m, MaxBaseFlatMonthly = 5355m,
        EntryThresholdYearly = Schwelle, IsActive = true, BvgVersichert = versichert,
    };

    private static decimal BvgAbzug(decimal bvgBasis, bool? versichert)
    {
        var anon = PayrollCalculations.BuildResult(
            Emp(), Contract(), Company(), 2026, 2, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28),
            new List<object>(), new List<object>(), new List<DeductionRule> { Bvg(versichert) }, 0m,
            new SvBases(bvgBasis, bvgBasis, bvgBasis, bvgBasis, bvgBasis),
            new List<object>(), 0, new List<object>(), 0, new List<object>(), 0, Saldo(),
            new List<EmployeeLohnAssignment>(), new List<EmployeeBankAccount>());
        foreach (var l in (System.Collections.IEnumerable)anon.GetType().GetProperty("abzugLines")!.GetValue(anon)!)
        {
            var cat = l.GetType().GetProperty("categoryCode")?.GetValue(l) as string;
            if (cat == "BVG") return (decimal)l.GetType().GetProperty("betrag")!.GetValue(l)!;
        }
        return 0m;
    }

    [Fact]
    public void Ohne_Eintrag_Monatsschwelle_wie_bisher()
    {
        // 709.60 × 12 = 8'515 < 22'680 → kein BVG (alte Monatsprüfung)
        Assert.Equal(0m, BvgAbzug(709.60m, null));
    }

    [Fact]
    public void Versichert_zahlt_Mindestbasis_auch_im_schwachen_Monat()
    {
        // Blerta Februar 2026: Mirus 22.05 = 7 % × 315
        Assert.Equal(-22.05m, BvgAbzug(709.60m, true));
    }

    [Fact]
    public void Nicht_versichert_zahlt_nichts_auch_ueber_der_Schwelle()
    {
        Assert.Equal(0m, BvgAbzug(3000m, false));
    }

    [Fact]
    public void Versichert_normaler_Monat_rechnet_koordiniert()
    {
        // 2'600 − 2'205 = 395 × 7 % = 27.65
        Assert.Equal(-27.65m, BvgAbzug(2600m, true));
    }

    // ── Vorschlag ────────────────────────────────────────────────────────

    private static readonly List<BvgPflichtVorschlag.LohnMonat> KeineMonate = new();

    [Fact]
    public void Fix_Monatslohn_mal_12_plus_13ml()
    {
        // 1'800 × 12 × 1.0833 = 23'399 ≥ 22'680
        var v = BvgPflichtVorschlag.Berechne("FIX", 1800m, null, null, true, 8.33m, Schwelle, KeineMonate);
        Assert.True(v.Versichert);
        Assert.Equal(23399m, v.Jahreslohn);
    }

    [Fact]
    public void Fix_ohne_13ml_nur_mal_12()
    {
        var v = BvgPflichtVorschlag.Berechne("FIX", 1800m, null, null, false, 8.33m, Schwelle, KeineMonate);
        Assert.False(v.Versichert);
        Assert.Equal(21600m, v.Jahreslohn);
    }

    [Fact]
    public void Mtp_garantierte_Stunden_mal_52_plus_13ml()
    {
        // 18 × 20.40 × 52 × 1.0833 = 20'685 → nicht versichert
        var unter = BvgPflichtVorschlag.Berechne("MTP", null, 18m, 20.40m, true, 8.33m, Schwelle, KeineMonate);
        Assert.False(unter.Versichert);
        Assert.Equal(20685m, unter.Jahreslohn);
        // 20 × 20.40 × 52 × 1.0833 = 22'984 → versichert
        var ueber = BvgPflichtVorschlag.Berechne("MTP", null, 20m, 20.40m, true, 8.33m, Schwelle, KeineMonate);
        Assert.True(ueber.Versichert);
    }

    [Fact]
    public void Flex_unter_drei_Monaten_kein_Vorschlag()
    {
        var monate = new List<BvgPflichtVorschlag.LohnMonat>
        {
            new(2026, 8, 2000m, false), new(2026, 9, 2100m, false),
        };
        var v = BvgPflichtVorschlag.Berechne("FLEX", null, null, 21.66m, true, 8.33m, Schwelle, monate);
        Assert.Null(v.Versichert);
        Assert.Null(v.Jahreslohn);
    }

    [Fact]
    public void Flex_Krankheitsmonat_zaehlt_nicht()
    {
        // Ø 2'010 aus drei gesunden Monaten × 12 × 1.0833 = 26'129; der Krankmonat 709.60 bleibt draussen
        var monate = new List<BvgPflichtVorschlag.LohnMonat>
        {
            new(2026, 1, 2010m, false), new(2026, 2, 709.60m, true),
            new(2026, 3, 1990m, false), new(2026, 4, 2030m, false),
        };
        var v = BvgPflichtVorschlag.Berechne("FLEX", null, null, 21.66m, true, 8.33m, Schwelle, monate);
        Assert.True(v.Versichert);
        Assert.Equal(26129m, v.Jahreslohn);
    }

    [Fact]
    public void Flex_nimmt_hoechstens_zwoelf_Monate()
    {
        var monate = Enumerable.Range(1, 12).Select(m => new BvgPflichtVorschlag.LohnMonat(2025, m, 1000m, false)).ToList();
        monate.AddRange(Enumerable.Range(1, 3).Select(m => new BvgPflichtVorschlag.LohnMonat(2026, m, 4000m, false)));
        var v = BvgPflichtVorschlag.Berechne("FLEX", null, null, null, false, 0m, Schwelle, monate);
        // jüngste 12: 2026-01..03 (3 × 4'000) + 2025-04..12 (9 × 1'000) → Ø 1'750 × 12 = 21'000
        Assert.Equal(21000m, v.Jahreslohn);
        Assert.False(v.Versichert);
    }

    [Fact]
    public void Ohne_Schwelle_kein_Vorschlag()
    {
        var v = BvgPflichtVorschlag.Berechne("FIX", 5000m, null, null, true, 8.33m, 0m, KeineMonate);
        Assert.Null(v.Versichert);
    }

    [Fact]
    public void Slip_13ml_und_Krankheit_erkennen()
    {
        const string slip = """{"lohnLines":[{"code":"10.1","betrag":1500.00},{"code":"180.1","betrag":124.95},{"code":"70.2","betrag":300.00}]}""";
        var (dreizehnter, krank) = BvgPflichtService.LeseSlip(slip);
        Assert.Equal(124.95m, dreizehnter);
        Assert.True(krank);
    }

    [Fact]
    public void Status_versichert_gewinnt_bei_Wechsel_im_Monat()
    {
        var eintraege = new[]
        {
            new EmployeeBvgPflicht { Versichert = false, GueltigAb = new DateOnly(2026, 1, 1), GueltigBis = new DateOnly(2026, 3, 14) },
            new EmployeeBvgPflicht { Versichert = true,  GueltigAb = new DateOnly(2026, 3, 15) },
        };
        Assert.True(BvgPflichtService.StatusInPeriode(eintraege, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)));
        Assert.False(BvgPflichtService.StatusInPeriode(eintraege, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));
        Assert.Null(BvgPflichtService.StatusInPeriode(eintraege, new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31)));
    }
}

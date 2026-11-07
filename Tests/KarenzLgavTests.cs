using System.Text.RegularExpressions;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Karenz und Taggeld nach L-GAV (Walter 07.11.2026, Empfehlung David Sägesser,
/// Rechtsdienst L-GAV). Ersetzt «Tagessatz ohne Feiertag» (04.08.2026) und die
/// 2-Monats-Regel der «Feiertagentschädigung auf Lohnersatz».
///
///   • Tagessatz 100 % = Bruttolohn pro Kalendertag inkl. Ferien, Feiertag, 13. ML:
///     (Grundlohn + Ferien + Feiertag) × (1 + 13. ML) × Std. × 52 ÷ 365.
///   • Taggeld 80 % = Versicherungsleistung, pro Tag auf 5 Rp. (wie SWICA), keine Zuschläge.
///   • Karenz 88 % = Grundlohn-Anteil × 88 %, Ferien/Feiertag/13. ML einzeln
///     (Rechenweg sichtbar, Total = 88 % des Tagessatzes).
///
/// Referenz: CHF 20.40, FLEX 17 h → 60.44 / 53.19 / 48.35;
/// SWICA-Abrechnung 24.10.2026: 12'899.05 × 80 % ÷ 365 = 28.25 × 15 Tage = 423.75.
/// </summary>
public class KarenzLgavTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
        => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("KarenzLgav_" + testName + "_" + Guid.NewGuid()).Options);

    private static async Task<(int empId, int cpId)> SeedAsync(
        AppDbContext db, string modell, decimal? hourlyRate, decimal? monthlySalary = null,
        decimal? guaranteedHours = null, DateTime? geburtstag = null, bool dreizehnter = true,
        bool ferienFeiertagImAnsatz = false, DateTime? vertragsBeginn = null)
    {
        var cp = new CompanyProfile
        {
            CompanyName = "Test-Filiale",
            DefaultVacationPercent5Weeks = 10.65m,
            DefaultVacationPercent6Weeks = 13.04m,
            VacationSixWeeksFromAge = 50,
            DefaultHolidayPercent = 2.27m,
            DefaultThirteenthSalaryPercent = 8.33m,
            MaxPartTimeHoursPerWeek = 17m,
        };
        db.CompanyProfiles.Add(cp);
        var emp = new Employee { EmployeeNumber = "750080", FirstName = "Test", LastName = "Muster", IsActive = true, DateOfBirth = geburtstag };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        db.Employments.Add(new Employment
        {
            EmployeeId = emp.Id,
            CompanyProfileId = cp.Id,
            IsActive = true,
            ContractStartDate = vertragsBeginn ?? DateTime.Today.AddMonths(-1),  // < 4 Perioden → Regel A
            EmploymentModel = modell,
            HourlyRate = hourlyRate,
            MonthlySalary = monthlySalary,
            GuaranteedHoursPerWeek = guaranteedHours,
            ThirteenthSalary = dreizehnter,
            FerienFeiertagImAnsatz = ferienFeiertagImAnsatz,
        });
        await db.SaveChangesAsync();
        return (emp.Id, cp.Id);
    }

    private static KtgTagessatzService Svc(AppDbContext db) => new(db, NullLogger<KtgTagessatzService>.Instance);

    // ── Tagessatz ───────────────────────────────────────────────────────

    [Fact]
    public async Task RegelA_FLEX_Lgav_Beispiel_20_40()
    {
        using var db = NewDb();
        var (empId, cpId) = await SeedAsync(db, "FLEX", 20.40m);
        var r = await Svc(db).CalculateAsync(empId, cpId);

        Assert.NotNull(r);
        Assert.Equal("A", r!.Regel);
        // 20.40 + 10.65 % + 2.27 % = 23.04 → × 1.0833 = 24.95 → × 17 × 52 / 365
        Assert.Equal(60.44m, r.Tagessatz100);
        Assert.Equal(53.19m, r.Tagessatz88);
        Assert.Equal(48.35m, r.Tagessatz80);      // 48.352 → 5 Rp.
        Assert.Equal(2.27m, r.Breakdown.FeiertagPct);
        Assert.True(r.EnthaeltFeiertag && r.EnthaeltFerien && r.Enthaelt13ml);
        // Grundlohn-Anteil exakt: 17 × 20.40 × 52 / 365
        Assert.Equal(17m * 20.40m * 52m / 365m, r.KarenzGrundTagessatz);
        // Rechenweg pro Tag fürs Fenster
        Assert.Equal(43.48m, r.Breakdown.KarenzGrundlohn);
        Assert.Equal(4.63m,  r.Breakdown.KarenzFerien);
        Assert.Equal(0.99m,  r.Breakdown.KarenzFeiertag);
        Assert.Equal(4.09m,  r.Breakdown.Karenz13ml);
        Assert.Equal(r.Tagessatz88, r.Breakdown.KarenzGrundlohn + r.Breakdown.KarenzFerien
                                  + r.Breakdown.KarenzFeiertag + r.Breakdown.Karenz13ml);
    }

    [Fact]
    public async Task RegelA_MTP_GarantierteStunden()
    {
        using var db = NewDb();
        var (empId, cpId) = await SeedAsync(db, "MTP", 21.66m, guaranteedHours: 34m);
        var r = await Svc(db).CalculateAsync(empId, cpId);

        // 34 × 21.66 × 1.1292 × 1.0833 × 52 / 365 (alt ohne Feiertag: 125.76)
        Assert.Equal(128.34m, r!.Tagessatz100);
        Assert.Equal(112.94m, r.Tagessatz88);
        Assert.Equal(102.65m, r.Tagessatz80);
        Assert.Equal(34m * 21.66m * 52m / 365m, r.KarenzGrundTagessatz);
    }

    [Fact]
    public async Task RegelA_FLEX_SechsWochenFerien()
    {
        using var db = NewDb();
        var (empId, cpId) = await SeedAsync(db, "FLEX", 20.40m, geburtstag: DateTime.Today.AddYears(-55));
        var r = await Svc(db).CalculateAsync(empId, cpId);

        Assert.Equal(13.04m, r!.Breakdown.FerienPct);
        Assert.Equal(61.72m, r.Tagessatz100);
        Assert.Equal(54.31m, r.Tagessatz88);
        Assert.Equal(5.67m, r.Breakdown.KarenzFerien);
        Assert.Equal(49.40m, r.Tagessatz80);
    }

    [Fact]
    public async Task RegelA_ohne_13ML_und_mit_Ferien_Feiertag_im_Ansatz()
    {
        using var db = NewDb();
        var (empId, cpId) = await SeedAsync(db, "FLEX", 20.40m, dreizehnter: false);
        var ohne13 = await Svc(db).CalculateAsync(empId, cpId);
        Assert.Equal(55.79m, ohne13!.Tagessatz100);   // wie die Engine: kein 13. ML
        Assert.Equal(0m, ohne13.Breakdown.Karenz13ml);

        using var db2 = NewDb();
        var (empId2, cpId2) = await SeedAsync(db2, "FLEX", 20.40m, ferienFeiertagImAnsatz: true);
        var imAnsatz = await Svc(db2).CalculateAsync(empId2, cpId2);
        Assert.Equal(53.52m, imAnsatz!.Tagessatz100);  // nur 13. ML auf den Ansatz
        Assert.Equal(0m, imAnsatz.Breakdown.KarenzFerien);
        Assert.Equal(0m, imAnsatz.Breakdown.KarenzFeiertag);
    }

    [Fact]
    public async Task RegelA_FIX_unveraendert()
    {
        using var db = NewDb();
        var (empId, cpId) = await SeedAsync(db, "FIX", null, monthlySalary: 4000m);
        var r = await Svc(db).CalculateAsync(empId, cpId);

        Assert.Equal(131.51m, r!.Tagessatz100);        // 4000 × 12 / 365
        Assert.Equal(r.Tagessatz100, Math.Round(r.KarenzGrundTagessatz, 2));
        Assert.Null(r.Breakdown.FeiertagPct);
        Assert.Null(r.Breakdown.KarenzGrundlohn);
        Assert.False(r.EnthaeltFeiertag);
    }

    [Fact]
    public async Task RegelB_FLEX_Grundlohn_aus_AHV_Durchschnitt_herausgerechnet()
    {
        using var db = NewDb();
        var start = new DateTime(DateTime.Today.Year - 1, 1, 1);
        var (empId, cpId) = await SeedAsync(db, "FLEX", 20.40m, vertragsBeginn: start);
        for (int m = 1; m <= 4; m++)
        {
            var periode = new PayrollPeriode
            {
                CompanyProfileId = cpId, Year = start.Year, Month = m,
                PeriodFrom = new DateOnly(start.Year, m, 1),
                PeriodTo = new DateOnly(start.Year, m, DateTime.DaysInMonth(start.Year, m)),
            };
            db.Add(periode);
            await db.SaveChangesAsync();
            db.Add(new PayrollSnapshot { PayrollPeriodeId = periode.Id, EmployeeId = empId, CompanyProfileId = cpId, SvBasisAhv = 1500m });
        }
        await db.SaveChangesAsync();

        var r = await Svc(db).CalculateAsync(empId, cpId);

        Assert.Equal("B", r!.Regel);
        Assert.Equal(49.32m, r.Tagessatz100);          // 1500 × 12 / 365
        // AHV-Ø enthält Ferien/Feiertag/13. ML schon → Grundlohn = Satz ÷ Aufschlag
        Assert.Equal(40.31m, Math.Round(r.KarenzGrundTagessatz, 2));
        var k = r.Breakdown;
        Assert.Equal(r.Tagessatz88, k.KarenzGrundlohn + k.KarenzFerien + k.KarenzFeiertag + k.Karenz13ml);
    }

    [Fact]
    public async Task Manueller_Satz_ist_Bruttolohn()
    {
        using var db = NewDb();
        var (empId, cpId) = await SeedAsync(db, "FLEX", 20.40m);
        (await db.Employees.FindAsync(empId))!.KtgTagessatzManuell = 60.44m;
        await db.SaveChangesAsync();

        var r = await Svc(db).CalculateAsync(empId, cpId);

        Assert.Equal("MANUELL", r!.Regel);
        Assert.Equal(48.35m, r.Tagessatz80);
        Assert.Equal(49.41m, Math.Round(r.KarenzGrundTagessatz, 2));  // 60.44 ÷ 1.1292 ÷ 1.0833
        Assert.Equal(53.19m, r.Tagessatz88);
    }

    // ── Rechenhilfen (PayrollCalculations) ──────────────────────────────

    private static KarenzService.MonatsKrankTag Tag(int i, decimal prozent, bool karenz, bool bvg = false)
        => new(new DateOnly(2026, 9, 1).AddDays(i), prozent, karenz, bvg, 1);

    [Fact]
    public void Taggeld_wie_SWICA_Abrechnung()
    {
        // Leistungsabrechnung SWICA 24.10.2026: versicherter Verdienst 12'899.05,
        // 80 %, 15 Tage à 28.25 = 423.75
        decimal satz100 = 12899.05m / 365m;
        Assert.Equal(28.25m, PayrollCalculations.TaggeldProTag(satz100));
        var tage = Enumerable.Range(0, 15).Select(i => Tag(i, 100m, karenz: false)).ToList();
        var le = PayrollCalculations.LohnersatzRechnen(tage, satz100, satz100 / PayrollCalculations.BruttoAufschlag(10.65m, 2.27m, 8.33m));
        Assert.Equal(423.75m, le.Taggeld);
        Assert.Equal(0m, le.Karenz);
        Assert.Equal(15m, le.TageTaggeld);
    }

    [Fact]
    public void Mischmonat_Karenz_und_Taggeld_mit_BVG_Wartefrist()
    {
        decimal grund = 17m * 20.40m * 52m / 365m;
        var tage = Enumerable.Range(0, 14).Select(i => Tag(i, 100m, karenz: true, bvg: true))
            .Concat(Enumerable.Range(14, 10).Select(i => Tag(i, 100m, karenz: false, bvg: true)))
            .ToList();
        var le = PayrollCalculations.LohnersatzRechnen(tage, 60.44m, grund);

        Assert.Equal(608.70m, le.Karenz);              // 14 × Grundlohn × 88 %
        Assert.Equal(483.50m, le.Taggeld);             // 10 × 48.35
        Assert.Equal(14m, le.TageKarenz);
        Assert.Equal(10m, le.TageTaggeld);
        // BVG: fehlende 12 % (Karenz) bzw. Differenz zu 100 % (Taggeld) des Tagessatzes
        Assert.Equal(222.44m, le.BvgKorrektur);
    }

    [Fact]
    public void Teilarbeitsunfaehigkeit_halbe_Tage()
    {
        var tage = Enumerable.Range(0, 10).Select(i => Tag(i, 50m, karenz: false)).ToList();
        var le = PayrollCalculations.LohnersatzRechnen(tage, 60.44m, 0m);
        Assert.Equal(241.75m, le.Taggeld);             // 10 × 48.35 × 50 %
        Assert.Equal(5m, le.TageTaggeld);
    }

    [Fact]
    public void Karenz_Rechenweg_14_Tage_wie_Lohnabrechnung()
    {
        // Zeilen der Lohnabrechnung (FLEX 17 h, 20.40, Regel A): Karenz rappengenau,
        // Prozentzeilen auf 5 Rp. — Total 744.60 = 14 × 53.19 ± Rundung
        decimal grund = 17m * 20.40m * 52m / 365m;
        decimal karenz   = PayrollCalculations.Rappen(14m * grund * PayrollCalculations.KarenzAnteil);
        decimal ferien   = PayrollCalculations.Round05(karenz * 10.65m / 100m);
        decimal feiertag = PayrollCalculations.Round05(karenz * 2.27m / 100m);
        decimal ml13     = PayrollCalculations.Round05((karenz + ferien + feiertag) * 8.33m / 100m);

        Assert.Equal(608.70m, karenz);
        Assert.Equal(64.85m, ferien);
        Assert.Equal(13.80m, feiertag);
        Assert.Equal(57.25m, ml13);
        Assert.Equal(744.60m, karenz + ferien + feiertag + ml13);
        Assert.True(Math.Abs(karenz + ferien + feiertag + ml13 - 14m * 53.19m) <= 0.10m);
    }

    [Fact]
    public void KarenzAufteilen_ergibt_88_Prozent_des_Tagessatzes()
    {
        foreach (var (fer, fei, m13) in new[] { (10.65m, 2.27m, 8.33m), (13.04m, 2.27m, 8.33m), (10.65m, 4.00m, 8.33m), (10.65m, 2.27m, 0m) })
        {
            decimal grund = 17m * 20.40m * 52m / 365m;
            decimal satz100 = grund * PayrollCalculations.BruttoAufschlag(fer, fei, m13);
            var k = PayrollCalculations.KarenzAufteilen(grund, fer, fei, m13);
            Assert.Equal(Math.Round(satz100 * 0.88m, 8), Math.Round(k.Total, 8));
        }
    }

    // ── Quellcode-Audit ─────────────────────────────────────────────────

    private static string ReadAllText(string relPath)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "hr-system.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        return File.ReadAllText(Path.Combine(dir ?? throw new InvalidOperationException("hr-system.csproj fehlt"), relPath));
    }

    [Fact]
    public void Engine_Feiertag_nur_auf_Karenz_nie_auf_Taggeld()
    {
        var src = ReadAllText("Services/PayrollCalculationEngine.cs");
        Assert.DoesNotContain("Feiertagentschädigung auf Lohnersatz", src);
        Assert.Equal(2, Regex.Matches(src, @"bezeichnung = ""Feiertagentschädigung auf Karenz""").Count);
        // Basis = nur die Karenz (krank88 + unfall88), SV nach Häkchen (Grundzeile)
        Assert.Contains("decimal karenzSummeMtp = krank88Mtp + unfall88Mtp;", src);
        Assert.Contains("decimal karenzSummeUtp = krank88Utp + unfall88Utp;", src);
        Assert.Contains("Grundzeile(LohnCodes.FeiertagEntschaedigung, feiertagKarenzMtp)", src);
        Assert.Contains("Grundzeile(LohnCodes.FeiertagEntschaedigung, feiertagKarenzUtp)", src);
        // Ferien und 13. ML der Karenz über die normalen Basen
        Assert.Contains("SumByFlag(lp => lp.ZaehltAlsBasisFerien) + karenzSummeMtp", src);
        Assert.Contains("SumByFlag(lp => lp.ZaehltAlsBasisFerien) + karenzSummeUtp", src);
        Assert.Contains("decimal lohnersatz13Mtp = karenzSummeMtp + feiertagKarenzMtp;", src);
        Assert.Contains("decimal lohnersatz13Utp = karenzSummeUtp + feiertagKarenzUtp;", src);
    }

    [Fact]
    public void Engine_88_und_80_Prozent_nur_zentral()
    {
        var src = ReadAllText("Services/PayrollCalculationEngine.cs");
        // Karenz/Taggeld laufen über PayrollCalculations.LohnersatzRechnen (MTP, FLEX, FIX)
        Assert.Equal(6, Regex.Matches(src, @"LohnersatzRechnen\(").Count);
        Assert.DoesNotContain("tagWert * 0.88m", src);
        Assert.DoesNotContain("tagWert * 0.80m", src);
    }

    [Fact]
    public void Engine_UnfallTaggeld_NutztEigeneHaekchen()
    {
        var src = ReadAllText("Services/PayrollCalculationEngine.cs");
        // 60.3 (UVG Taggeld 80 %) hatte die SV-Häkchen von 60.2 (Karenz) → war AHV-pflichtig.
        Assert.DoesNotContain("LpFlagsOr(\"60.2\"", src);
        Assert.Equal(3, Regex.Matches(src, @"LpFlagsOr\(LohnCodes\.UnfallTaggeld").Count);
    }
}

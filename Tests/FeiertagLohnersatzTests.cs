using System.Text.RegularExpressions;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Walter-Vorgabe 04.08.2026 (SV-Pflicht-Korrektur):
///
/// Die Feiertagsentschädigung ist AHV-pflichtiger LOHN und darf nicht im
/// (SV-freien) KTG-/UVG-Versicherungs-Taggeld stecken. Daher:
///
///   1. KtgTagessatzService: Brutto-Stundenlohn bei FLEX/MTP = Stundenlohn
///      × (1 + Ferien%) × (1 + 13.ML%) — OHNE Feiertag% (vorher ×1.0227).
///      FIX/FIX-M unverändert (Monatslohn × 12/365 bzw. AHV-Durchschnitt).
///   2. PayrollCalculationEngine (FLEX + MTP): während Krankheit/Unfall wird
///      die Feiertagsentschädigung als SEPARATE, voll SV-pflichtige Lohnzeile
///      «Feiertagentschädigung auf Lohnersatz» (2.27% auf Karenz-/Taggeld-
///      Summe, Codes 70/70.2/60/60.2) gebucht.
///   3. Zeitliche Grenze: nur solange die zusammenhängende Krank-/Unfall-
///      Absenz &lt; 2 volle Monate dauert (Ketten-Beginn + 2 Monate ≤
///      Periodenende → keine Zahlung mehr).
///
/// Referenzfall Zeljka Pajic (MTP, 21.66/h): alter Tagessatz 128.60
/// (mit Feiertag) → neu 125.76 (ohne Feiertag).
/// </summary>
public class FeiertagLohnersatzTests
{
    // ────────────────────────────────────────────────────────────────────
    // Teil 1: KtgTagessatzService — Tagessatz OHNE Feiertag-Komponente
    // ────────────────────────────────────────────────────────────────────

    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
        => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("FeiertagLohnersatz_" + testName + "_" + Guid.NewGuid()).Options);

    private static async Task<(AppDbContext db, int empId, int cpId)> SeedAsync(
        AppDbContext db, string modell, decimal? hourlyRate, decimal? monthlySalary,
        decimal? guaranteedHours)
    {
        var cp = new CompanyProfile
        {
            CompanyName = "Test-Filiale",
            DefaultVacationPercent5Weeks = 10.65m,
            DefaultHolidayPercent = 2.27m,
            MaxPartTimeHoursPerWeek = 17m,
        };
        db.CompanyProfiles.Add(cp);
        var emp = new Employee { EmployeeNumber = "750001", FirstName = "Zeljka", LastName = "Muster", IsActive = true };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        db.Employments.Add(new Employment
        {
            EmployeeId = emp.Id,
            CompanyProfileId = cp.Id,
            IsActive = true,
            ContractStartDate = DateTime.Today.AddMonths(-1),  // < 4 Perioden → Regel A
            EmploymentModel = modell,
            HourlyRate = hourlyRate,
            MonthlySalary = monthlySalary,
            GuaranteedHoursPerWeek = guaranteedHours,
        });
        await db.SaveChangesAsync();
        return (db, emp.Id, cp.Id);
    }

    [Fact]
    public async Task RegelA_MTP_TagessatzOhneFeiertagKomponente()
    {
        using var db = NewDb();
        var (_, empId, cpId) = await SeedAsync(db, "MTP", hourlyRate: 21.66m, monthlySalary: null, guaranteedHours: 34m);
        var svc = new KtgTagessatzService(db, NullLogger<KtgTagessatzService>.Instance);

        var r = await svc.CalculateAsync(empId, cpId);

        Assert.NotNull(r);
        Assert.Equal("A", r!.Regel);
        // 34 × (21.66 × 1.1065 × 1.0833) × 52 / 365 = 125.76
        // (ALT mit Feiertag ×1.0227 wäre 128.60 — Referenz Zeljka Pajic)
        Assert.Equal(125.76m, r.Tagessatz100);
        Assert.NotEqual(128.60m, r.Tagessatz100);
        // Breakdown liefert keine Feiertag-Komponente mehr (Anzeige-Karte)
        Assert.Null(r.Breakdown.FeiertagPct);
        Assert.Equal(10.65m, r.Breakdown.FerienPct);
        Assert.Equal(8.33m, r.Breakdown.ZehnterMLPct);
    }

    [Fact]
    public async Task RegelA_FLEX_TagessatzOhneFeiertagKomponente()
    {
        using var db = NewDb();
        var (_, empId, cpId) = await SeedAsync(db, "FLEX", hourlyRate: 20m, monthlySalary: null, guaranteedHours: null);
        var svc = new KtgTagessatzService(db, NullLogger<KtgTagessatzService>.Instance);

        var r = await svc.CalculateAsync(empId, cpId);

        Assert.NotNull(r);
        // 17 × (20 × 1.1064 × 1.0833) × 52 / 365 = 58.06
        Assert.Equal(58.06m, r!.Tagessatz100);
        Assert.Null(r.Breakdown.FeiertagPct);
    }

    [Fact]
    public async Task RegelA_FIX_UnveraendertMonatslohnFormel()
    {
        using var db = NewDb();
        var (_, empId, cpId) = await SeedAsync(db, "FIX", hourlyRate: null, monthlySalary: 4000m, guaranteedHours: null);
        var svc = new KtgTagessatzService(db, NullLogger<KtgTagessatzService>.Instance);

        var r = await svc.CalculateAsync(empId, cpId);

        Assert.NotNull(r);
        // FIX bleibt UNBERÜHRT von der Feiertag-Korrektur: 4000 × 12 / 365 = 131.51
        Assert.Equal(131.51m, r!.Tagessatz100);
    }

    // ────────────────────────────────────────────────────────────────────
    // Teil 2: Ketten-Beginn der zusammenhängenden Krank-/Unfall-Absenz
    // ────────────────────────────────────────────────────────────────────

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void KettenBeginn_EinzelRange_LiefertVon()
    {
        var beginn = PayrollCalculationEngine.FindLohnersatzKettenBeginn(
            new[] { (D(2026, 7, 5), D(2026, 7, 20)) }, anchorTag: D(2026, 7, 10));
        Assert.Equal(D(2026, 7, 5), beginn);
    }

    [Fact]
    public void KettenBeginn_GestueckelteAbsenz_UeberMonatsgrenze_EineKette()
    {
        // Monatlich neu erfasste Krankheit (wie in der Praxis üblich):
        // 15.05.–31.05. + 01.06.–30.06. + 01.07.–31.07. = EINE Kette ab 15.05.
        var beginn = PayrollCalculationEngine.FindLohnersatzKettenBeginn(
            new[]
            {
                (D(2026, 5, 15), D(2026, 5, 31)),
                (D(2026, 6, 1),  D(2026, 6, 30)),
                (D(2026, 7, 1),  D(2026, 7, 31)),
            },
            anchorTag: D(2026, 7, 1));
        Assert.Equal(D(2026, 5, 15), beginn);
    }

    [Fact]
    public void KettenBeginn_LueckeUnterbrichtKette()
    {
        // Lücke > 1 Tag (11.–19.05. gesund) → neue Kette ab 20.05.
        var beginn = PayrollCalculationEngine.FindLohnersatzKettenBeginn(
            new[]
            {
                (D(2026, 5, 1),  D(2026, 5, 10)),
                (D(2026, 5, 20), D(2026, 5, 31)),
            },
            anchorTag: D(2026, 5, 25));
        Assert.Equal(D(2026, 5, 20), beginn);
    }

    [Fact]
    public void KettenBeginn_AnchorNichtAbgedeckt_LiefertNull()
    {
        var beginn = PayrollCalculationEngine.FindLohnersatzKettenBeginn(
            new[] { (D(2026, 5, 1), D(2026, 5, 10)) }, anchorTag: D(2026, 6, 15));
        Assert.Null(beginn);
    }

    // ────────────────────────────────────────────────────────────────────
    // Teil 3: 2-Monats-Grenze (Beginn + 2 Monate ≤ Periodenende → stopp)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ZweiMonatsGrenze_KurzeAbsenz_Erlaubt()
    {
        // Krank ab 10.06., Juli-Lohn (periodTo 31.07.): 10.08. > 31.07. → zahlen
        Assert.True(PayrollCalculationEngine.IsFeiertagAufLohnersatzErlaubt(
            D(2026, 6, 10), periodTo: D(2026, 7, 31)));
    }

    [Fact]
    public void ZweiMonatsGrenze_LangeAbsenz_NichtMehrErlaubt()
    {
        // Krank ab 15.05., Juli-Lohn: 15.07. ≤ 31.07. → 2 volle Monate um → stopp
        Assert.False(PayrollCalculationEngine.IsFeiertagAufLohnersatzErlaubt(
            D(2026, 5, 15), periodTo: D(2026, 7, 31)));
    }

    [Fact]
    public void ZweiMonatsGrenze_Grenzfall_BeginnAmMonatsersten()
    {
        // Krank ab 01.06., Juli-Lohn: 01.08. > 31.07. → am Periodenende sind
        // noch keine 2 vollen Monate um → im Juli noch zahlen.
        Assert.True(PayrollCalculationEngine.IsFeiertagAufLohnersatzErlaubt(
            D(2026, 6, 1), periodTo: D(2026, 7, 31)));
        // Im August-Lohn (periodTo 31.08.): 01.08. ≤ 31.08. → stopp.
        Assert.False(PayrollCalculationEngine.IsFeiertagAufLohnersatzErlaubt(
            D(2026, 6, 1), periodTo: D(2026, 8, 31)));
    }

    // ────────────────────────────────────────────────────────────────────
    // Teil 4: Spec-Audit (Quellcode-Scan, Muster WorkflowSpecAuditTests)
    // ────────────────────────────────────────────────────────────────────

    private static string RepoRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "hr-system.csproj")))
                dir = Directory.GetParent(dir)?.FullName;
            if (dir is null) throw new InvalidOperationException("hr-system.csproj nicht gefunden — Test muss im Tests-Subdir laufen.");
            return dir;
        }
    }

    private static string ReadAllText(string relPath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relPath));

    [Fact]
    public void KtgTagessatzService_StdLohnBrutto_OhneFeiertagFaktor()
    {
        var src = ReadAllText("Services/KtgTagessatzService.cs");
        // Der Brutto-Stundenlohn darf keinen Feiertag-Faktor mehr enthalten.
        Assert.DoesNotContain("feiertagPct  / 100m", src);
        Assert.DoesNotContain("(1 + feiertagPct", src);
        // Ferien + 13. ML bleiben drin.
        Assert.Contains("* (1 + ferienPct    / 100m)", src);
        Assert.Contains("* (1 + ZehnterMonatslohnPct / 100m)", src);
        // Begründung dokumentiert.
        Assert.Contains("04.08.2026", src);
    }

    [Fact]
    public void Engine_FeiertagAufLohnersatz_InBeidenStundenlohnZweigen()
    {
        var src = ReadAllText("Services/PayrollCalculationEngine.cs");
        // Genau 2 Buchungs-Stellen (MTP + FLEX/UTP) — FIX/FIX-M bewusst NICHT.
        var count = Regex.Matches(src, @"bezeichnung = ""Feiertagentschädigung auf Lohnersatz""").Count;
        Assert.Equal(2, count);
        // Beide Stellen respektieren die 2-Monats-Grenze.
        Assert.Equal(2, Regex.Matches(src, @"if \(feiertagAufLohnersatzErlaubt && holidayPct > 0").Count);
        // Der Guard nutzt die zentrale statische Regel.
        Assert.Contains("IsFeiertagAufLohnersatzErlaubt(kettenBeginn, periodTo)", src);
    }

    [Fact]
    public void Engine_FeiertagAufLohnersatz_Utp_VollSvPflichtig()
    {
        var src = ReadAllText("Services/PayrollCalculationEngine.cs");
        // Im UTP-Zweig liegt die Zeile NACH dem mainLohn-Snapshot → sie muss
        // explizit in ALLE fünf delta*-SV-Basen fliessen.
        var idx = src.IndexOf("lohnersatzSummeUtp", StringComparison.Ordinal);
        Assert.True(idx > 0, "UTP-Block «Feiertagentschädigung auf Lohnersatz» nicht gefunden.");
        var block = src.Substring(idx, Math.Min(2200, src.Length - idx));
        Assert.Contains("deltaAhv  += feiertagLohnersatzUtp", block);
        Assert.Contains("deltaNbuv += feiertagLohnersatzUtp", block);
        Assert.Contains("deltaKtg  += feiertagLohnersatzUtp", block);
        Assert.Contains("deltaBvg  += feiertagLohnersatzUtp", block);
        Assert.Contains("deltaQst  += feiertagLohnersatzUtp", block);
    }
}

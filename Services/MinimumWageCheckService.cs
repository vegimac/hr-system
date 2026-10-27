using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Ergebnis einer L-GAV-Mindestlohn-Prüfung.
///   Status: "OK" | "UNDERPAID" | "NO_RULE" | "NOT_CHECKED"
///   Minimum/Actual in CHF; Unit "/h" oder "/Mt."; Difference = Actual − Minimum.
/// </summary>
public record MinWageCheckResult(
    string Status,
    decimal? Minimum,
    decimal? Actual,
    string? Unit,
    decimal? Difference,
    string? Message);

/// <summary>
/// Zentrale L-GAV-Mindestlohn-Prüfung (Walter-Vorgabe 20.05.2026).
/// Eine Quelle für die Regel-Auswahl (Funktion × Modell × Ausbildung × Alter ×
/// Stichtag) und den Vergleich gegen den vertraglichen Lohn. Wird verwendet vom
/// Lohnlauf (harter Block in PayrollController.ConfirmPayroll) und vom Listen-
/// Check (MinimumWageRulesController.CheckPeriod). Die Logik spiegelt
/// ComplianceController/DashboardService — diese könnten später hierauf
/// migriert werden, um die Dreifach-Pflege zu beenden.
/// </summary>
public class MinimumWageCheckService
{
    private readonly AppDbContext _db;
    public MinimumWageCheckService(AppDbContext db) => _db = db;

    /// <summary>
    /// Prüft ob ein aktiver Vertrag KEINE verwertbare Lohnsumme hat
    /// (Walter-Vorgabe 21.05.2026). Rule-UNABHÄNGIG — greift auch wenn gar
    /// keine Mindestlohnregel existiert. Spiegelt exakt die Logik der
    /// Lohn-Engine (PayrollCalculationEngine):
    ///   • FIX / FIX-M: Monatslohn nötig. Die Engine nimmt MonthlySalary, fällt
    ///     auf MonthlySalaryFte × Pensum zurück → fehlt BEIDES, ist der Lohn 0.
    ///   • UTP / MTP (und alles andere): Stundenlohn (HourlyRate) nötig.
    /// true = Lohnsumme fehlt → Lohnlauf muss gesperrt werden.
    /// </summary>
    public static bool IsLohnsummeMissing(
        string? employmentModel, decimal? monthlySalary, decimal? monthlySalaryFte, decimal? hourlyRate)
    {
        var model = (employmentModel ?? "").ToUpperInvariant();
        bool isFix = model == "FIX" || model == "FIX-M";
        if (isFix)
            return !((monthlySalary ?? 0m) > 0m || (monthlySalaryFte ?? 0m) > 0m);
        // UTP / MTP → Stundenlohn erforderlich
        return !((hourlyRate ?? 0m) > 0m);
    }

    /// <summary>
    /// Hat der MA in der Periode AHV-pflichtige Lohnzeilen (Zulagen/Honorare > 0)?
    /// Dann ist «Vertrag ohne Lohnsumme» KEINE Sperre: der Lohn kommt aus den
    /// Zulagen (Honorar-Journalistin, Muster AG TF16; Verwaltungsrat mit Sitzungs-
    /// geld; GF mit Lohn 0 und Bonus). Die Sperre soll nur den 0-Lohn mit lauter
    /// Abzügen verhindern. (Walter 09.09.2026)
    /// </summary>
    public async Task<bool> HatLohnzeilenAsync(int employeeId, int year, int month)
    {
        var periode = $"{year}-{month:D2}";
        return await _db.LohnZulagen
            .Where(z => z.EmployeeId == employeeId && z.Periode == periode && z.Betrag > 0m
                     && z.Lohnposition!.AhvAlvPflichtig)
            .AnyAsync();
    }

    /// <summary>
    /// Echter 0-Beleg ohne Vertragslohn: erlaubt, wenn der MA nicht L-GAV-
    /// pflichtig ist (Honorar / Verwaltungsrat / Swissdec NoTimeConstraint).
    /// Muster AG TF39 Hasler Jan 2025: Eintritt, Honorar erst im April —
    /// Swissdec erwartet die Person mit 0 in der Monatsmeldung, nicht «raus».
    /// Bei L-GAV (Schaub-Crew) bleibt die Sperre: leerer Vertrag = Lohn fehlt.
    /// (Walter 11.09.2026)
    /// </summary>
    public static bool IstNullBelegErlaubt(bool lgavPflichtig) => !lgavPflichtig;

    /// <summary>
    /// Die L-GAV-Mindestlohn-Tabelle gilt nur unter dem L-GAV und nur für die
    /// vier Modelle (keine Swissdec-Art). Die Tabelle bleibt liegen. Ein späterer
    /// Gesamtarbeitsvertrag wäre dieselbe Prüfung mit einer eigenen Tabelle,
    /// eingeschaltet an der Filiale. Der örtliche Mindestlohn der Filiale gilt
    /// unabhängig davon.
    /// </summary>
    public static bool LgavTabelleGilt(bool lgavAktiv, string? swissdecVertragsart)
        => lgavAktiv && string.IsNullOrWhiteSpace(swissdecVertragsart);

    public async Task<MinWageCheckResult> CheckAsync(
        string? jobGroupCode,
        string? educationLevelCode,
        string? employmentModel,
        decimal? employmentPercentage,
        decimal? hourlyRate,
        decimal? monthlySalary,
        DateTime? dateOfBirth,
        DateOnly effectiveDate,
        int? companyProfileId = null,
        string? swissdecVertragsart = null)
    {
        if (string.IsNullOrWhiteSpace(jobGroupCode) || string.IsNullOrWhiteSpace(employmentModel))
            return new MinWageCheckResult("NOT_CHECKED", null, null, null, null, null);

        var modelCode = employmentModel.ToUpperInvariant() switch
        {
            "FIX-M" => "FIX-M",
            "FIX"   => "FIX",
            "MTP"   => "MTP",
            _       => "FLEX"
        };
        var salaryType = (modelCode == "FIX" || modelCode == "FIX-M") ? "monthly" : "hourly";

        var istLgav = true;
        if (companyProfileId is int filialeId)
        {
            var gav = await _db.CompanyProfiles.AsNoTracking()
                .Where(c => c.Id == filialeId)
                .Select(c => new { c.IstGav, c.GavName })
                .FirstOrDefaultAsync();
            istLgav = gav != null && HrSystem.Models.CompanyProfile.IstLgavBezeichnung(gav.IstGav, gav.GavName);
        }
        var tabelle = LgavTabelleGilt(istLgav, swissdecVertragsart);

        // Alter für altersabhängige Jugend-Regeln — am Ende des Stichtag-Monats.
        var effDt = effectiveDate.ToDateTime(TimeOnly.MinValue);
        int? age = MindestlohnAlter.AlterFuerRegel(dateOfBirth, effDt);

        HrSystem.Models.MinimumWageRuleNew? rule = null;
        if (tabelle)
        {
            // Importer-Konvention: leere Ausbildung → «Ia» (5 Sans qualification).
            var eduCode = string.IsNullOrWhiteSpace(educationLevelCode) ? "Ia" : educationLevelCode!;
            var eduLevelId = await _db.EducationLevels
                .Where(e => e.Code == eduCode && e.IsActive)
                .Select(e => (int?)e.Id)
                .FirstOrDefaultAsync();
            if (eduLevelId == null)
                return new MinWageCheckResult("NO_RULE", null, null, null, null, "Bildungsstufe nicht gefunden.");

            // Spezifischste Regel zuerst: niedrigster age_max (Jugendliche), NULLS LAST;
            // dann jüngstes Gültig-ab.
            rule = await _db.MinimumWageRulesNew
                .Where(r => r.IsActive
                         && r.JobGroupCode == jobGroupCode
                         && r.EmploymentModelCode == modelCode
                         && r.EducationLevelId == eduLevelId.Value
                         && r.SalaryType == salaryType
                         && r.ValidFrom <= effDt
                         && (r.ValidTo == null || r.ValidTo >= effDt)
                         && (r.AgeMax == null || (age != null && age <= r.AgeMax)))
                .OrderBy(r => r.AgeMax == null ? int.MaxValue : r.AgeMax)
                .ThenByDescending(r => r.ValidFrom)
                .FirstOrDefaultAsync();

            if (rule == null)
                return new MinWageCheckResult("NO_RULE", null, null, null, null, "Keine Mindestlohnregel gefunden.");
        }

        decimal? actual = salaryType == "monthly" ? monthlySalary : hourlyRate;
        decimal minimum = 0m;
        string unit = salaryType == "monthly" ? "/Mt." : "/h";
        var hatMindestlohn = false;
        if (rule != null)
        {
            if (salaryType == "monthly")
            {
                var pct = (employmentPercentage ?? 100m) / 100m;
                minimum = Math.Round(rule.Amount * pct, 2);
            }
            else
            {
                minimum = rule.Amount;
            }
            hatMindestlohn = true;
        }

        // ── Kommunaler Filial-Mindestlohn (übersteuert NACH OBEN) ────────────
        // Walter-Vorgabe 23.05.2026: in manchen Städten gilt ein eigener
        // Mindestlohn. Erfasst als JAHRESLOHN pro Filiale (branch_min_wage,
        // versioniert). Monatslohn = Jahr/13, Stundenlohn = Jahr/52/Wochenstunden.
        // Effektives Minimum = max(L-GAV, Filial-Floor) — senkt nie einen schon
        // höheren L-GAV-Satz (z.B. Manager). Ohne L-GAV ist er der einzige
        // Mindestlohn. Bei Jugendlichen nur, wenn der Filial-Eintrag das
        // ausdrücklich vorsieht (applies_to_youth).
        string minSource = "L-GAV-Mindestlohn";
        DateTime minValidFrom = rule?.ValidFrom ?? effDt;
        if (companyProfileId is int filialId && await FilialMindestlohnAsync(filialId, effDt, age) is { } filial)
        {
            decimal cityFloor = salaryType == "monthly"
                ? Math.Round(filial.MonthlyFte * ((employmentPercentage ?? 100m) / 100m), 2)
                : filial.Hourly;
            if (cityFloor > minimum)
            {
                minimum        = cityFloor;
                hatMindestlohn = true;
                minSource      = "kommunalen Mindestlohn dieser Filiale";
                minValidFrom   = filial.ValidFrom;
            }
        }

        if (!hatMindestlohn)
            return new MinWageCheckResult("NOT_CHECKED", null, null, null, null, null);

        if (actual == null)
            return new MinWageCheckResult("NOT_CHECKED", minimum, null, unit, null, "Lohn fehlt.");

        var diff = actual.Value - minimum;
        if (diff < 0)
        {
            var msg = $"Lohn CHF {actual.Value:0.00}{unit} liegt CHF {Math.Abs(diff):0.00} unter dem {minSource} von CHF {minimum:0.00}{unit} (gültig ab {minValidFrom:dd.MM.yyyy}).";
            return new MinWageCheckResult("UNDERPAID", minimum, actual, unit, diff, msg);
        }

        return new MinWageCheckResult("OK", minimum, actual, unit, diff, null);
    }

    /// <summary>
    /// Kantonaler/örtlicher Mindestlohn der Filiale, falls einer am Stichtag gilt.
    /// Stundenlohn = Jahreslohn / 52 / Wochenstunden, Monatslohn 100 % = Jahreslohn / 13.
    /// Jugendliche nur, wenn der Eintrag das vorsieht.
    /// </summary>
    public async Task<FilialMindestlohn?> FilialMindestlohnAsync(int companyProfileId, DateTime effDt, int? age)
    {
        bool isYouth = age != null && age < 18;
        var bmw = await _db.BranchMinWages
            .Where(b => b.IsActive
                     && b.CompanyProfileId == companyProfileId
                     && b.ValidFrom <= effDt
                     && (b.ValidTo == null || b.ValidTo >= effDt))
            .OrderByDescending(b => b.ValidFrom)
            .FirstOrDefaultAsync();
        if (bmw == null || (isYouth && !bmw.AppliesToYouth)) return null;

        var weekly = (await _db.CompanyProfiles
            .Where(c => c.Id == companyProfileId)
            .Select(c => c.NormalWeeklyHours)
            .FirstOrDefaultAsync()) ?? 42m;
        if (weekly <= 0) weekly = 42m;

        return new FilialMindestlohn(
            Math.Round(bmw.AnnualSalary / 52m / weekly, 2),
            Math.Round(bmw.AnnualSalary / 13m, 2),
            bmw.ValidFrom);
    }
}

public readonly record struct FilialMindestlohn(decimal Hourly, decimal MonthlyFte, DateTime ValidFrom);

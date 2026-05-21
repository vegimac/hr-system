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

    public async Task<MinWageCheckResult> CheckAsync(
        string? jobGroupCode,
        string? educationLevelCode,
        string? employmentModel,
        decimal? employmentPercentage,
        decimal? hourlyRate,
        decimal? monthlySalary,
        DateTime? dateOfBirth,
        DateOnly effectiveDate)
    {
        if (string.IsNullOrWhiteSpace(jobGroupCode) || string.IsNullOrWhiteSpace(employmentModel))
            return new MinWageCheckResult("NOT_CHECKED", null, null, null, null, null);

        var modelCode = employmentModel.ToUpperInvariant() switch
        {
            "FIX-M" => "FIX-M",
            "FIX"   => "FIX",
            "MTP"   => "MTP",
            _       => "UTP"
        };
        var salaryType = (modelCode == "FIX" || modelCode == "FIX-M") ? "monthly" : "hourly";

        // Importer-Konvention: leere Ausbildung → „Ia" (5 Sans qualification).
        var eduCode = string.IsNullOrWhiteSpace(educationLevelCode) ? "Ia" : educationLevelCode!;
        var eduLevelId = await _db.EducationLevels
            .Where(e => e.Code == eduCode && e.IsActive)
            .Select(e => (int?)e.Id)
            .FirstOrDefaultAsync();
        if (eduLevelId == null)
            return new MinWageCheckResult("NO_RULE", null, null, null, null, "Bildungsstufe nicht gefunden.");

        // Alter am Stichtag (für altersabhängige Jugend-Regeln).
        int? age = null;
        var effDt = effectiveDate.ToDateTime(TimeOnly.MinValue);
        if (dateOfBirth.HasValue)
        {
            var bd = dateOfBirth.Value;
            int a = effDt.Year - bd.Year;
            if (effDt < new DateTime(effDt.Year, bd.Month, bd.Day)) a--;
            age = a;
        }

        // Spezifischste Regel zuerst: niedrigster age_max (Jugendliche), NULLS LAST;
        // dann jüngstes Gültig-ab.
        var rule = await _db.MinimumWageRulesNew
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

        decimal? actual;
        decimal minimum;
        string unit;
        if (salaryType == "monthly")
        {
            actual  = monthlySalary;
            var pct = (employmentPercentage ?? 100m) / 100m;
            minimum = Math.Round(rule.Amount * pct, 2);
            unit    = "/Mt.";
        }
        else
        {
            actual  = hourlyRate;
            minimum = rule.Amount;
            unit    = "/h";
        }

        if (actual == null)
            return new MinWageCheckResult("NOT_CHECKED", minimum, null, unit, null, "Lohn fehlt.");

        var diff = actual.Value - minimum;
        if (diff < 0)
        {
            var msg = $"Lohn CHF {actual.Value:0.00}{unit} liegt CHF {Math.Abs(diff):0.00} unter dem L-GAV-Mindestlohn von CHF {minimum:0.00}{unit} (gültig ab {rule.ValidFrom:dd.MM.yyyy}).";
            return new MinWageCheckResult("UNDERPAID", minimum, actual, unit, diff, msg);
        }

        return new MinWageCheckResult("OK", minimum, actual, unit, diff, null);
    }
}

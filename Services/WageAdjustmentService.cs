using System.Globalization;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

// ============================================================================
// Mindestlohn-Vertragsanpassung (Walter-Vorgabe 23.05.2026).
//
// Wenn die Mindestlöhne per einem Stichtag steigen (= eine Folge-Version in
// minimum_wage_rule_new mit valid_from in der Zukunft), liegen evtl. bestehende
// Verträge ab diesem Datum unter dem neuen Mindestlohn. Dieser Service findet
// diese Verträge (pro Filiale) und erzeugt — auf Wunsch einzeln oder alle —
// pro betroffenem MA einen NEUEN Vertrag: identisch zum bestehenden, nur mit
// angepasstem Lohn und ContractStartDate = Mindestlohn-Datum. Der bestehende
// Vertrag bleibt unverändert und wird automatisch auf den Vortag beendet
// (gleiches Versionierungs-Muster wie EmploymentsController.Create).
//
// Optional wird dem MA eine kurze Text-Mitteilung ins Postfach gelegt
// (MailboxDocument TargetType=EMPLOYEE, MessageBody, ohne Datei).
//
// Der Mindestlohn-Vergleich läuft ausschliesslich über die zentrale
// MinimumWageCheckService.CheckAsync — eine Quelle für die Regel-Auswahl.
// ============================================================================

public record WageAdjustmentItem(
    int EmploymentId,
    int EmployeeId,
    string EmployeeName,
    string? EmployeeNumber,
    string? JobGroupCode,
    string? EmploymentModel,
    string? EducationLevelCode,
    string Unit,                    // "/h" | "/Mt."
    decimal? CurrentWage,           // aktueller Lohn (Stundenlohn bzw. Monatslohn actual)
    decimal NewMinimum,             // Mindestlohn am Stichtag (actual; Monat = Satz × Pensum)
    decimal SuggestedWage,          // Vorschlag zum Eintragen: Std = Satz; Monat = Satz (100 %/FTE)
    decimal? EmploymentPercentage,
    bool Monthly,
    decimal Difference);            // aktuell − Minimum (negativ)

public record WageAdjustmentPending(
    bool HasGeneration,
    DateOnly? EffectiveDate,
    int Count,
    List<WageAdjustmentItem> Items);

public record WageAdjustmentApplyItem(int EmploymentId, decimal NewWage);

public record WageAdjustmentApplyResult(int Created, int Messages, List<string> Skipped);

public class WageAdjustmentService
{
    private readonly AppDbContext _db;
    private readonly MinimumWageCheckService _minWage;

    public WageAdjustmentService(AppDbContext db, MinimumWageCheckService minWage)
    {
        _db = db;
        _minWage = minWage;
    }

    private static readonly CultureInfo Ch = CultureInfo.GetCultureInfo("de-CH");
    private static string Fmt(decimal v) => v.ToString("#,##0.00", Ch);

    /// <summary>
    /// Nächstes Mindestlohn-Stichtagsdatum = frühestes valid_from in der Zukunft
    /// (über alle Filialen, die Tabelle ist global). NULL = keine geplante Änderung.
    /// </summary>
    public async Task<DateOnly?> GetNextEffectiveDateAsync()
    {
        var today = DateTime.Today;
        var next = await _db.MinimumWageRulesNew
            .Where(r => r.IsActive && r.ValidFrom > today)
            .OrderBy(r => r.ValidFrom)
            .Select(r => (DateTime?)r.ValidFrom)
            .FirstOrDefaultAsync();
        return next.HasValue ? DateOnly.FromDateTime(next.Value) : (DateOnly?)null;
    }

    /// <summary>
    /// Betroffene Verträge der Filiale: aktive Verträge, deren Lohn am nächsten
    /// Mindestlohn-Stichtag UNTER dem dann gültigen Mindestlohn liegt. Schon
    /// angepasste MA (Folge-Vertrag ab Stichtag existiert) fallen weg.
    /// </summary>
    public async Task<WageAdjustmentPending> GetPendingAsync(int companyProfileId)
    {
        var next = await GetNextEffectiveDateAsync();
        if (next == null)
            return new WageAdjustmentPending(false, null, 0, new List<WageAdjustmentItem>());

        var effDate = next.Value;
        var effDt   = effDate.ToDateTime(TimeOnly.MinValue);

        // Aktive Verträge der Filiale, die am Stichtag gelten. DateTime-Vergleich
        // (nicht DateOnly.FromDateTime — in EF/Npgsql nicht übersetzbar).
        var ems = await _db.Employments
            .Include(e => e.Employee)
            .Include(e => e.JobGroup)   // FK-Code statt JobTitle (Walter 26.05.2026)
            .Where(e => e.IsActive
                     && e.CompanyProfileId == companyProfileId
                     && e.Employee != null
                     && e.Employee.IsActive
                     && !e.Employee.IsPayrollExcluded
                     && e.JobGroupId != null
                     && e.ContractStartDate <= effDt
                     && (e.ContractEndDate == null || e.ContractEndDate >= effDt))
            .ToListAsync();

        // Pro MA der am Stichtag jüngste Vertrag.
        var byEmp = ems
            .GroupBy(e => e.EmployeeId)
            .Select(g => g.OrderByDescending(e => e.ContractStartDate).First())
            .ToList();

        // MA mit bereits existierendem Folge-Vertrag GENAU ab dem Stichtag → fertig.
        var empIds = byEmp.Select(e => e.EmployeeId).ToList();
        var adjusted = (await _db.Employments
            .Where(e => empIds.Contains(e.EmployeeId) && e.ContractStartDate == effDt)
            .Select(e => e.EmployeeId)
            .ToListAsync()).ToHashSet();

        var items = new List<WageAdjustmentItem>();
        foreach (var em in byEmp)
        {
            if (adjusted.Contains(em.EmployeeId)) continue;

            var chk = await _minWage.CheckAsync(
                em.JobGroup?.Code, em.EducationLevelCode, em.EmploymentModel,
                em.EmploymentPercentage, em.HourlyRate, em.MonthlySalary,
                em.Employee!.DateOfBirth, effDate, companyProfileId);
            if (chk.Status != "UNDERPAID" || chk.Minimum == null) continue;

            bool monthly = chk.Unit == "/Mt.";
            var pct = em.EmploymentPercentage ?? 100m;
            // Alles in EINER Basis darstellen, die zum Vertrags-Hauptfeld + zur
            // Mindestlohn-Matrix passt: Stundenlohn /h, Monatslohn 100 %/FTE.
            decimal ruleAmount = monthly
                ? (pct > 0 ? Math.Round(chk.Minimum!.Value * 100m / pct, 2) : chk.Minimum!.Value)
                : chk.Minimum!.Value;
            decimal currentBasis = monthly
                ? (em.MonthlySalaryFte
                    ?? (pct > 0 ? Math.Round((em.MonthlySalary ?? 0m) * 100m / pct, 2) : (em.MonthlySalary ?? 0m)))
                : (em.HourlyRate ?? 0m);

            items.Add(new WageAdjustmentItem(
                EmploymentId:        em.Id,
                EmployeeId:          em.EmployeeId,
                EmployeeName:        $"{em.Employee!.FirstName} {em.Employee!.LastName}".Trim(),
                EmployeeNumber:      em.Employee!.EmployeeNumber,
                JobGroupCode:        em.JobGroup?.Code ?? "",
                EmploymentModel:     em.EmploymentModel,
                EducationLevelCode:  em.EducationLevelCode,
                Unit:                chk.Unit ?? "",
                CurrentWage:         currentBasis,
                NewMinimum:          ruleAmount,
                SuggestedWage:       ruleAmount,
                EmploymentPercentage: em.EmploymentPercentage,
                Monthly:             monthly,
                Difference:          Math.Round(currentBasis - ruleAmount, 2)));
        }

        items = items.OrderBy(i => i.EmployeeName, StringComparer.OrdinalIgnoreCase).ToList();
        return new WageAdjustmentPending(true, effDate, items.Count, items);
    }

    /// <summary>
    /// Legt für die übergebenen Verträge je einen neuen Vertrag ab dem Stichtag an
    /// (Lohn = NewWage; bei Monatslohn ist NewWage der 100 %/FTE-Wert), beendet den
    /// offenen Vertrag auf den Vortag und legt optional eine Postfach-Mitteilung ab.
    /// Server-autoritativ: jeder neue Lohn wird gegengeprüft — liegt er trotzdem
    /// unter dem Mindestlohn, wird der Eintrag übersprungen (kein Vertrag erzeugt).
    /// Läuft als ein SaveChangesAsync (atomar).
    /// </summary>
    public async Task<WageAdjustmentApplyResult> ApplyAsync(
        int companyProfileId, DateOnly effectiveDate,
        List<WageAdjustmentApplyItem> items, bool sendMessage, int? actorUserId)
    {
        var effDt = effectiveDate.ToDateTime(TimeOnly.MinValue);
        int created = 0, messages = 0;
        var skipped = new List<string>();

        foreach (var it in items)
        {
            var src = await _db.Employments
                .Include(e => e.Employee)
                .Include(e => e.JobGroup)   // FK-Code für Recheck (Walter 26.05.2026)
                .FirstOrDefaultAsync(e => e.Id == it.EmploymentId);
            if (src == null) { skipped.Add($"Vertrag {it.EmploymentId} nicht gefunden."); continue; }
            if (src.CompanyProfileId != companyProfileId)
            { skipped.Add($"Vertrag {it.EmploymentId} gehört nicht zur gewählten Filiale."); continue; }

            // Idempotent: existiert schon ein Vertrag ab dem Stichtag → überspringen.
            bool exists = await _db.Employments.AnyAsync(e =>
                e.EmployeeId == src.EmployeeId && e.ContractStartDate == effDt);
            if (exists) { skipped.Add($"{src.Employee?.FirstName} {src.Employee?.LastName}: Folge-Vertrag ab {effectiveDate:dd.MM.yyyy} existiert bereits."); continue; }

            bool monthly = (src.EmploymentModel ?? "").ToUpperInvariant() is "FIX" or "FIX-M";
            var pct = src.EmploymentPercentage ?? 100m;
            var newActual = monthly ? Math.Round(it.NewWage * pct / 100m, 2) : it.NewWage;

            // Server-autoritative Gegenprüfung: neuer Lohn muss den Mindestlohn erreichen.
            var recheck = await _minWage.CheckAsync(
                src.JobGroup?.Code, src.EducationLevelCode, src.EmploymentModel,
                src.EmploymentPercentage,
                monthly ? (decimal?)null : newActual,
                monthly ? newActual : (decimal?)null,
                src.Employee?.DateOfBirth, effectiveDate, companyProfileId);
            if (recheck.Status == "UNDERPAID")
            {
                skipped.Add($"{src.Employee?.FirstName} {src.Employee?.LastName}: Neuer Lohn liegt weiterhin unter dem Mindestlohn.");
                continue;
            }

            // Den am Stichtag laufenden Vertrag (= src) auf den Vortag beenden.
            // Deckt offene UND befristete Verträge ab, die über den Stichtag ragen.
            src.ContractEndDate = effDt.AddDays(-1);

            // Neuer Vertrag = Klon, nur Lohn + Datum neu.
            var neu = new Employment
            {
                EmployeeId             = src.EmployeeId,
                CompanyProfileId       = src.CompanyProfileId,
                EmploymentModel        = src.EmploymentModel ?? "",
                SalaryType             = src.SalaryType ?? "",
                ContractStartDate      = effDt,
                ContractEndDate        = null,
                JobTitle               = src.JobTitle,       // Stellenbezeichnung free-text
                JobGroupId             = src.JobGroupId,     // FK (Walter 26.05.2026)
                ContractType           = src.ContractType,
                EducationLevelCode     = src.EducationLevelCode,
                EmploymentPercentage   = src.EmploymentPercentage,
                WeeklyHours            = src.WeeklyHours,
                GuaranteedHoursPerWeek = src.GuaranteedHoursPerWeek,
                MonthlySalaryFte       = monthly ? it.NewWage : src.MonthlySalaryFte,
                MonthlySalary          = monthly ? newActual   : src.MonthlySalary,
                HourlyRate             = monthly ? src.HourlyRate : it.NewWage,
                VacationPaymentMode    = src.VacationPaymentMode,
                ProbationPeriodMonths  = src.ProbationPeriodMonths,
                ProbationEndDate       = src.ProbationEndDate,
                IsActive               = true,
            };
            _db.Employments.Add(neu);
            created++;

            if (sendMessage)
            {
                var lohnLabel = monthly ? "Monatslohn" : "Stundenlohn";
                var betragTxt = monthly ? $"CHF {Fmt(newActual)}" : $"CHF {Fmt(it.NewWage)}/Std.";
                var body = $"Dein {lohnLabel} wird per {effectiveDate:dd.MM.yyyy} auf {betragTxt} angepasst "
                         + "(Anpassung an den neuen L-GAV-Mindestlohn). Der angepasste Lohn erscheint "
                         + "auf deiner nächsten Lohnabrechnung.";
                _db.MailboxDocuments.Add(new MailboxDocument
                {
                    CompanyProfileId = companyProfileId,
                    UploadedBy       = actorUserId,
                    UploadedAt       = DateTime.Now,
                    OriginalFilename = $"Lohnanpassung per {effectiveDate:dd.MM.yyyy}",
                    // Reine Text-Mitteilung (keine Datei). storage_filename ist UNIQUE →
                    // leerer String kollidiert ab der 2. Notiz. Eindeutiger Platzhalter.
                    StorageFilename  = $"msg-{Guid.NewGuid():N}",
                    MimeType         = null,
                    FileSizeBytes    = null,
                    Bemerkung        = "Lohnanpassung",
                    MessageBody      = body,
                    EmployeeId       = src.EmployeeId,
                    NotifyUserId     = null,
                    TargetType       = "EMPLOYEE",
                });
                messages++;
            }
        }

        await _db.SaveChangesAsync();
        return new WageAdjustmentApplyResult(created, messages, skipped);
    }
}

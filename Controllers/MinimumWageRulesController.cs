using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

// ============================================================================
// L-GAV Mindestlohn-Verwaltung (Walter-Vorgabe 20.05.2026).
// Pflegt die Tabelle minimum_wage_rule_new (Funktion × Modell × Ausbildung,
// Stunden-/Monatslohn, Jugend-Sondersätze via age_max). Die Sätze sind
// versioniert über valid_from/valid_to — eine Lohnänderung kann an JEDEM Datum
// greifen (1.1., 1.7., …), nicht nur zum Jahreswechsel.
//
// Lohn-Edit-Lock (der datum-basierte LohnEditLockService): NICHT relevant — das
// sind Katalog-/Stammdaten, kein MA-Lohn. (Im Audit-Test EditLockEndpointAuditTests
// entsprechend whitelisted.)
//
// ABER: ein wage-spezifischer „in Lohn verwendet"-Lock GILT (Walter-Vorgabe
// 23.05.2026, analog SV-Sätze): ein Mindestlohn, dessen Gültigkeit eine
// eingefrorene Lohnperiode überlappt, wurde in einem Lohnlauf verwendet und darf
// NICHT mehr direkt geändert werden (PUT /{id} → 409 MINWAGE_LOCKED). Eine
// notwendige Änderung läuft ausschliesslich über „Neue Sätze ab" (POST /copy =
// neue Version ab Stichtag). So bleibt der Compliance-Check, der den am
// Vertrags-/Stichtag gültigen Satz liest, für abgeschlossene Perioden stabil.
// ============================================================================
[ApiController]
[Route("api/minimum-wage-rules")]
[Authorize(Roles = "admin,superuser")]
public class MinimumWageRulesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly MinimumWageCheckService _minWage;
    private readonly LohnEditLockService _editLock;
    private readonly QstPflichtCheckService _qstCheck;
    public MinimumWageRulesController(AppDbContext db, MinimumWageCheckService minWage,
                                      LohnEditLockService editLock, QstPflichtCheckService qstCheck)
    {
        _db = db;
        _minWage = minWage;
        _editLock = editLock;
        _qstCheck = qstCheck;
    }

    // GET /api/minimum-wage-rules/first-allowed-date → frühestes Gültig-ab-Datum
    // für eine neue Folge-Version (global über alle Filialen): 1. Tag des Monats
    // nach der spätesten abgeschlossenen/in-Verarbeitung-Periode. NULL = frei.
    [HttpGet("first-allowed-date")]
    public async Task<IActionResult> FirstAllowedDate()
    {
        var d = await _editLock.GetGlobalFirstAllowedDateAsync();
        return Ok(new { firstAllowedDate = d?.ToString("yyyy-MM-dd") });
    }

    // GET /api/minimum-wage-rules?date=2026-05-20  → am Stichtag gültige Sätze
    // GET /api/minimum-wage-rules?all=true         → komplette Historie
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly? date, [FromQuery] bool all = false)
    {
        var q = _db.MinimumWageRulesNew.AsQueryable();

        if (!all)
        {
            var d  = date ?? DateOnly.FromDateTime(DateTime.Today);
            var dt = d.ToDateTime(TimeOnly.MinValue);
            q = q.Where(r => r.IsActive
                          && r.ValidFrom <= dt
                          && (r.ValidTo == null || r.ValidTo >= dt));
        }

        var rules = await q
            .OrderBy(r => r.SalaryType)
            .ThenBy(r => r.JobGroupCode)
            .ThenBy(r => r.EmploymentModelCode)
            .ThenBy(r => r.EducationLevelId)
            .ThenBy(r => r.ValidFrom)
            .ToListAsync();

        // „In Lohn verwendet" (Walter-Vorgabe 22.05.2026): ein Mindestlohn, dessen
        // Gültigkeit eine eingefrorene Lohnperiode überlappt, gilt als verwendet →
        // darf nicht mehr direkt geändert werden, nur über „Neu ab" (neue Version).
        // Gleiche Logik wie SV-Sätze.
        var frozen = await _db.PayrollPerioden
            .Where(p => p.Status != "offen"
                     || (p.AkontoStatus != "OFFEN" && p.AkontoStatus != "IN_BEARBEITUNG_GF"))
            .Select(p => new { p.PeriodFrom, p.PeriodTo })
            .ToListAsync();

        var result = rules.Select(r => new
        {
            r.Id,
            r.JobGroupCode,
            r.EmploymentModelCode,
            r.EducationLevelId,
            r.SalaryType,
            r.Amount,
            r.ValidFrom,
            r.ValidTo,
            r.IsActive,
            r.AgeMax,
            r.Confirmed,
            inLohnVerwendet = frozen.Any(p =>
                DateOnly.FromDateTime(r.ValidFrom) <= p.PeriodTo
             && (r.ValidTo == null || DateOnly.FromDateTime(r.ValidTo.Value) >= p.PeriodFrom))
        });

        return Ok(result);
    }

    // Prüft, ob eine Regel zeitlich eine eingefrorene Periode überlappt (= in einem
    // Lohnlauf verwendet) → dann ist Direkt-Bearbeiten gesperrt (nur „Neu ab").
    private async Task<bool> IsRuleInLohnVerwendetAsync(MinimumWageRuleNew rule)
    {
        var vf = DateOnly.FromDateTime(rule.ValidFrom);
        DateOnly? vt = rule.ValidTo.HasValue ? DateOnly.FromDateTime(rule.ValidTo.Value) : (DateOnly?)null;
        return await _db.PayrollPerioden.AnyAsync(p =>
            (p.Status != "offen" || (p.AkontoStatus != "OFFEN" && p.AkontoStatus != "IN_BEARBEITUNG_GF"))
            && vf <= p.PeriodTo
            && (vt == null || vt >= p.PeriodFrom));
    }

    // PUT /api/minimum-wage-rules/{id}  — nur den Betrag ändern.
    // Bewusst NUR amount: Schlüsselfelder (Funktion/Modell/Ausbildung/Datum)
    // dürfen nicht nachträglich umgehängt werden — dafür gibt es /copy.
    [Authorize(Roles = "admin")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAmount(int id, [FromBody] MinWageAmountDto dto)
    {
        var rule = await _db.MinimumWageRulesNew.FindAsync(id);
        if (rule is null) return NotFound();

        // In-Lohn-verwendet-Sperre (Walter-Vorgabe 23.05.2026): überlappt der Satz
        // eine eingefrorene Lohnperiode, wurde er bereits abgerechnet → Direkt-Edit
        // gesperrt, nur „Neue Sätze ab" (POST /copy). Gleiche Logik wie SV-Sätze.
        if (await IsRuleInLohnVerwendetAsync(rule))
            return Conflict(new
            {
                error   = "MINWAGE_LOCKED",
                message = "Dieser Mindestlohn wurde bereits in einer Lohnabrechnung verwendet — Direkt-Bearbeiten ist gesperrt. Bitte 'Neue Sätze ab' verwenden, um eine Folge-Version mit neuem Gültig-ab-Datum anzulegen."
            });

        if (dto.Amount < 0)
            return BadRequest(new { error = "Betrag darf nicht negativ sein." });

        rule.Amount = dto.Amount;
        // Speichern = „bestätigt" (Walter-Vorgabe 23.05.2026). Ein geplanter Satz,
        // der angeschaut/gespeichert wurde, gilt als geprüft → Frontend zeigt ihn
        // grün (Betrag geändert) bzw. orange (unverändert) statt rot (unbestätigt).
        rule.Confirmed = true;
        await _db.SaveChangesAsync();
        return Ok(new { rule.Id, rule.Amount, rule.Confirmed });
    }

    // POST /api/minimum-wage-rules/copy  — Body { effectiveDate: "2026-07-01" }
    // Versionierung an beliebigem Stichtag: alle aktuell offenen Sätze
    // (valid_to == NULL, aktiv, valid_from < Stichtag) werden auf Stichtag − 1
    // begrenzt; je eine Kopie mit valid_from = Stichtag, valid_to = NULL wird
    // angelegt. Danach kann der User die neuen Beträge anpassen.
    // Läuft als ein SaveChangesAsync = atomar (implizite Transaktion).
    [Authorize(Roles = "admin")]
    [HttpPost("copy")]
    public async Task<IActionResult> Copy([FromBody] MinWageCopyDto dto)
    {
        var eff    = dto.EffectiveDate;
        var effDt  = eff.ToDateTime(TimeOnly.MinValue);
        var prevDt = eff.AddDays(-1).ToDateTime(TimeOnly.MinValue);

        // Stichtag darf nicht in einer bereits abgeschlossenen/in Verarbeitung
        // befindlichen Lohnperiode liegen (Walter-Vorgabe 23.05.2026, GLOBAL über
        // alle Filialen — die Mindestlohn-Tabelle gilt für alle). Frühestes Datum
        // = 1. Tag des Monats nach der spätesten gesperrten Periode irgendeiner Filiale.
        var firstAllowed = await _editLock.GetGlobalFirstAllowedDateAsync();
        if (firstAllowed.HasValue && eff < firstAllowed.Value)
            return Conflict(new
            {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Das Gültig-ab-Datum {eff:dd.MM.yyyy} liegt in einer bereits abgeschlossenen/verarbeiteten Lohnperiode. Frühestes erlaubtes Datum: {firstAllowed.Value:dd.MM.yyyy}.",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });

        // Duplikat-Schutz: noch keine Version mit genau diesem Gültig-ab.
        var alreadyExists = await _db.MinimumWageRulesNew.AnyAsync(r => r.ValidFrom == effDt);
        if (alreadyExists)
            return Conflict(new { error = $"Es existieren bereits Sätze mit Gültig-ab {eff:dd.MM.yyyy}." });

        // Aktuell offene Sätze, die vor dem Stichtag gelten.
        var current = await _db.MinimumWageRulesNew
            .Where(r => r.IsActive && r.ValidTo == null && r.ValidFrom < effDt)
            .ToListAsync();

        if (current.Count == 0)
            return BadRequest(new { error = "Keine offenen Sätze gefunden, die vor dem Stichtag gültig sind." });

        foreach (var old in current)
        {
            old.ValidTo = prevDt;                       // Vorgänger begrenzen
            _db.MinimumWageRulesNew.Add(new MinimumWageRuleNew
            {
                JobGroupCode        = old.JobGroupCode,     // Legacy-Cache (sync zu JobGroupId)
                JobGroupId          = old.JobGroupId,       // FK — Walter-Vorgabe 26.05.2026
                EmploymentModelCode = old.EmploymentModelCode,
                EducationLevelId    = old.EducationLevelId,
                SalaryType          = old.SalaryType,
                Amount              = old.Amount,
                ValidFrom           = effDt,
                ValidTo             = null,
                IsActive            = true,
                AgeMax              = old.AgeMax,
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { copied = current.Count, effectiveDate = eff.ToString("yyyy-MM-dd") });
    }

    // GET /api/minimum-wage-rules/check-period?companyProfileId=&year=&month=
    // Liefert die aktiven MA der Filiale, deren vertraglicher Lohn am
    // Periodenende UNTER dem L-GAV-Mindestlohn liegt. Speist im Lohnlauf den
    // Listen-Indikator (⚠ + Zähler) und den Lohnzettel-Banner. Read-only.
    [HttpGet("check-period")]
    public async Task<IActionResult> CheckPeriod(
        [FromQuery] int companyProfileId, [FromQuery] int year, [FromQuery] int month)
    {
        if (year < 2000 || month < 1 || month > 12)
            return BadRequest(new { error = "Ungültige Periode." });

        var periodTo   = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var periodFromDt = new DateTime(year, month, 1);
        var periodToDt   = periodTo.ToDateTime(TimeOnly.MinValue);

        // DateTime-Vergleich (NICHT DateOnly.FromDateTime — in EF/Npgsql nicht
        // SQL-übersetzbar → 500). ContractStartDate ist DateTime (date-mid).
        var ems = await _db.Employments
            .Include(e => e.Employee)
            .Include(e => e.JobGroup)   // FK-Code statt JobTitle (Walter 26.05.2026)
            .Where(e => e.IsActive
                     && e.CompanyProfileId == companyProfileId
                     && e.Employee != null
                     && e.Employee.IsActive
                     && !e.Employee.IsPayrollExcluded
                     && e.JobGroupId != null
                     && e.ContractStartDate <= periodToDt
                     && (e.ContractEndDate == null || e.ContractEndDate >= periodFromDt))
            .ToListAsync();

        // Pro MA nur den in der Periode jüngsten Vertrag prüfen (analog Engine-Auswahl).
        var byEmp = ems
            .GroupBy(e => e.EmployeeId)
            .Select(g => g.OrderByDescending(e => e.ContractStartDate).First())
            .ToList();

        // QST-Kanton-Mismatch (Walter-Vorgabe 04.08.2026): den Steuerkanton der
        // am Periodenende aktiven QST-Erfassung pro MA vorladen (eine Query statt
        // N+1) — Vergleich gegen employee.canton_code unten im Loop.
        var qstMmEmpIds = byEmp.Select(e => e.EmployeeId).ToList();
        var qstMmRows = await _db.EmployeeQuellensteuer
            .AsNoTracking()
            .Where(q => qstMmEmpIds.Contains(q.EmployeeId)
                     && q.ValidFrom <= periodTo
                     && (q.ValidTo == null || q.ValidTo >= periodTo))
            .Select(q => new { q.EmployeeId, q.ValidFrom, q.Id, q.Steuerkanton })
            .ToListAsync();
        var qstKantonByEmp = qstMmRows
            .GroupBy(q => q.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.ValidFrom).ThenByDescending(x => x.Id).First().Steuerkanton);

        // Wohnkanton AM PERIODENENDE aus der Wohnort-Historie (Walter-Vorgabe
        // 12.08.2026): der Vergleich darf NICHT gegen den heutigen
        // employee.canton_code laufen — nach einem Umzug per 1.8. wohnte die MA
        // in der Juli-Periode noch im alten Kanton, der Juli-QST-Eintrag (alter
        // Kanton) ist also KORREKT und darf keine Mismatch-Warnung auslösen.
        // Auflösung: jüngster Historie-Eintrag mit GueltigAb <= periodTo
        // (GueltigAb NULL = «seit jeher» = ältester). Ohne Historie-Eintrag
        // Fallback auf employee.canton_code (Bestand ohne Umzug).
        var wohnHistRows = await _db.EmployeeWohnortHistories
            .AsNoTracking()
            .Where(w => qstMmEmpIds.Contains(w.EmployeeId))
            .Select(w => new { w.EmployeeId, w.GueltigAb, w.KantonCode, w.Id })
            .ToListAsync();
        var wohnKantonByEmp = wohnHistRows
            .GroupBy(w => w.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                Row = g.Where(w => w.GueltigAb == null || w.GueltigAb <= periodTo)
                       .OrderByDescending(w => w.GueltigAb.HasValue)  // datiert vor «seit jeher»
                       .ThenByDescending(w => w.GueltigAb)
                       .ThenByDescending(w => w.Id)
                       .FirstOrDefault()
            })
            .Where(x => x.Row != null && !string.IsNullOrWhiteSpace(x.Row!.KantonCode))
            .ToDictionary(x => x.EmployeeId, x => x.Row!.KantonCode!);

        var underpaid = new List<object>();
        foreach (var em in byEmp)
        {
            // Lohnsumme-fehlt zuerst (rule-unabhängig, Walter 21.05.2026): gültiger
            // Vertrag ohne Monats-/Stundenlohn → der MA bekäme 0 Lohn. Wird mit
            // problem="NO_SALARY" geliefert; das Frontend zeigt dasselbe ⚠/Banner
            // wie beim Mindestlohn und sperrt das Bestätigen.
            if (MinimumWageCheckService.IsLohnsummeMissing(
                    em.EmploymentModel, em.MonthlySalary, em.MonthlySalaryFte, em.HourlyRate))
            {
                underpaid.Add(new
                {
                    employeeId = em.EmployeeId,
                    firstName  = em.Employee!.FirstName,
                    lastName   = em.Employee!.LastName,
                    problem    = "NO_SALARY",
                    Minimum    = (decimal?)null,
                    Actual     = (decimal?)null,
                    Unit       = (string?)null,
                    Difference = (decimal?)null,
                    Message    = "Vertrag ohne Lohnsumme — bitte zuerst einen Lohn erfassen."
                });
                continue;
            }

            var chk = await _minWage.CheckAsync(
                em.JobGroup?.Code, em.EducationLevelCode, em.EmploymentModel,
                em.EmploymentPercentage, em.HourlyRate, em.MonthlySalary,
                em.Employee!.DateOfBirth, periodTo, companyProfileId);
            if (chk.Status == "UNDERPAID")
            {
                underpaid.Add(new
                {
                    employeeId = em.EmployeeId,
                    firstName  = em.Employee!.FirstName,
                    lastName   = em.Employee!.LastName,
                    problem    = "UNDERPAID",
                    chk.Minimum,
                    chk.Actual,
                    chk.Unit,
                    chk.Difference,
                    chk.Message
                });
            }

            // QST-Pflicht-Lücke (Walter-Vorgabe 26.05.2026) — wird neben
            // UNDERPAID/NO_SALARY im selben „mit Lohnproblem"-Aggregat
            // im Frontend angezeigt. problem="QST_OFFEN".
            var qstChk = await _qstCheck.CheckAsync(em.EmployeeId, periodTo);
            if (qstChk.IsPflichtOffen)
            {
                underpaid.Add(new
                {
                    employeeId = em.EmployeeId,
                    firstName  = em.Employee!.FirstName,
                    lastName   = em.Employee!.LastName,
                    problem    = "QST_OFFEN",
                    Minimum    = (decimal?)null,
                    Actual     = (decimal?)null,
                    Unit       = (string?)null,
                    Difference = (decimal?)null,
                    Message    = qstChk.Message
                });
            }

            // Ehepartner-Angaben unvollständig (Walter-Vorgabe 20.08.2026) —
            // blockt ConfirmPayroll/Freigeben mit QST_PARTNER_DATEN_FEHLEN,
            // erscheint hier im selben «mit Lohnproblem»-Aggregat.
            if (qstChk.PartnerDatenFehlen)
            {
                underpaid.Add(new
                {
                    employeeId = em.EmployeeId,
                    firstName  = em.Employee!.FirstName,
                    lastName   = em.Employee!.LastName,
                    problem    = "QST_PARTNER",
                    Minimum    = (decimal?)null,
                    Actual     = (decimal?)null,
                    Unit       = (string?)null,
                    Difference = (decimal?)null,
                    Message    = "Ehepartner-Angaben unvollständig: "
                        + string.Join(" · ", qstChk.PartnerDatenMaengel ?? new List<string>())
                });
            }

            // QST-Kanton ≠ Wohnkanton (Walter-Vorgabe 04.08.2026, historisiert
            // 12.08.2026): der QST-Tarif richtet sich IMMER nach dem Wohnkanton
            // — aber nach dem Wohnkanton IN DER PERIODE (Wohnort-Historie am
            // Periodenende), nicht nach dem heutigen. Weicht der Kanton der in
            // der Periode aktiven QST-Erfassung ab, erscheint der MA mit
            // problem="QST_KANTON_MISMATCH" in derselben «mit Lohnproblem»-
            // Liste → ⚠ in der MA-Liste + Banner auf dem Lohnzettel. Bewusst
            // NUR Warnung, KEIN 409-Block in ConfirmPayroll/Freigeben — der
            // Lohnlauf darf durchlaufen, aber der Tarif muss geprüft werden
            // (realer Fall Artiles Santana: BE vs. AG).
            var wohnKanton = wohnKantonByEmp.TryGetValue(em.EmployeeId, out var histKanton)
                ? histKanton
                : em.Employee!.CantonCode;
            if (qstKantonByEmp.TryGetValue(em.EmployeeId, out var qstKanton)
                && !string.IsNullOrWhiteSpace(qstKanton)
                && !string.IsNullOrWhiteSpace(wohnKanton)
                && !string.Equals(qstKanton.Trim(), wohnKanton.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                underpaid.Add(new
                {
                    employeeId = em.EmployeeId,
                    firstName  = em.Employee!.FirstName,
                    lastName   = em.Employee!.LastName,
                    problem    = "QST_KANTON_MISMATCH",
                    Minimum    = (decimal?)null,
                    Actual     = (decimal?)null,
                    Unit       = (string?)null,
                    Difference = (decimal?)null,
                    Message    = $"QST-Tarif Kanton {qstKanton.Trim().ToUpperInvariant()}, Wohnkanton {wohnKanton.Trim().ToUpperInvariant()} — Tarif prüfen."
                });
            }
        }
        return Ok(underpaid);
    }
}

public record MinWageAmountDto(decimal Amount);
public record MinWageCopyDto(DateOnly EffectiveDate);

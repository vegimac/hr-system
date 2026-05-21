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
// Lohn-Edit-Lock: NICHT relevant — das sind Katalog-/Stammdaten, kein MA-Lohn.
// Eine Mindestlohn-Änderung verändert keine bereits abgeschlossene Abrechnung;
// der Compliance-Check liest den am Vertrags-/Stichtag gültigen Satz.
// (Im Audit-Test EditLockEndpointAuditTests entsprechend whitelisted.)
// ============================================================================
[ApiController]
[Route("api/minimum-wage-rules")]
[Authorize(Roles = "admin,superuser")]
public class MinimumWageRulesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly MinimumWageCheckService _minWage;
    public MinimumWageRulesController(AppDbContext db, MinimumWageCheckService minWage)
    {
        _db = db;
        _minWage = minWage;
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
            .Select(r => new
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
                r.AgeMax
            })
            .ToListAsync();

        return Ok(rules);
    }

    // PUT /api/minimum-wage-rules/{id}  — nur den Betrag ändern.
    // Bewusst NUR amount: Schlüsselfelder (Funktion/Modell/Ausbildung/Datum)
    // dürfen nicht nachträglich umgehängt werden — dafür gibt es /copy.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAmount(int id, [FromBody] MinWageAmountDto dto)
    {
        var rule = await _db.MinimumWageRulesNew.FindAsync(id);
        if (rule is null) return NotFound();

        if (dto.Amount < 0)
            return BadRequest(new { error = "Betrag darf nicht negativ sein." });

        rule.Amount = dto.Amount;
        await _db.SaveChangesAsync();
        return Ok(new { rule.Id, rule.Amount });
    }

    // POST /api/minimum-wage-rules/copy  — Body { effectiveDate: "2026-07-01" }
    // Versionierung an beliebigem Stichtag: alle aktuell offenen Sätze
    // (valid_to == NULL, aktiv, valid_from < Stichtag) werden auf Stichtag − 1
    // begrenzt; je eine Kopie mit valid_from = Stichtag, valid_to = NULL wird
    // angelegt. Danach kann der User die neuen Beträge anpassen.
    // Läuft als ein SaveChangesAsync = atomar (implizite Transaktion).
    [HttpPost("copy")]
    public async Task<IActionResult> Copy([FromBody] MinWageCopyDto dto)
    {
        var eff    = dto.EffectiveDate;
        var effDt  = eff.ToDateTime(TimeOnly.MinValue);
        var prevDt = eff.AddDays(-1).ToDateTime(TimeOnly.MinValue);

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
                JobGroupCode        = old.JobGroupCode,
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
            .Where(e => e.IsActive
                     && e.CompanyProfileId == companyProfileId
                     && e.Employee != null
                     && e.Employee.IsActive
                     && !e.Employee.IsPayrollExcluded
                     && e.JobTitle != null && e.JobTitle != ""
                     && e.ContractStartDate <= periodToDt
                     && (e.ContractEndDate == null || e.ContractEndDate >= periodFromDt))
            .ToListAsync();

        // Pro MA nur den in der Periode jüngsten Vertrag prüfen (analog Engine-Auswahl).
        var byEmp = ems
            .GroupBy(e => e.EmployeeId)
            .Select(g => g.OrderByDescending(e => e.ContractStartDate).First());

        var underpaid = new List<object>();
        foreach (var em in byEmp)
        {
            var chk = await _minWage.CheckAsync(
                em.JobTitle, em.EducationLevelCode, em.EmploymentModel,
                em.EmploymentPercentage, em.HourlyRate, em.MonthlySalary,
                em.Employee!.DateOfBirth, periodTo);
            if (chk.Status == "UNDERPAID")
            {
                underpaid.Add(new
                {
                    employeeId = em.EmployeeId,
                    firstName  = em.Employee!.FirstName,
                    lastName   = em.Employee!.LastName,
                    chk.Minimum,
                    chk.Actual,
                    chk.Unit,
                    chk.Difference,
                    chk.Message
                });
            }
        }
        return Ok(underpaid);
    }
}

public record MinWageAmountDto(decimal Amount);
public record MinWageCopyDto(DateOnly EffectiveDate);

using System.Security.Claims;
using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Ferienplaner für FIX-M-Manager (Walter-Vorgabe 14.08.2026):
/// Schwester-Ansicht des Manager-Dienstplans — gleiches Monats-Grid, gleiche
/// Manager-Zeilen, aber NUR Ferien. Der GF zieht Wunsch-Ferien als Balken auf
/// (GEPLANT = orange, verschiebbar). «Definitiv setzen» erzeugt die echte
/// Ferien-Absenz (Balken grün) — damit erscheinen die Ferien automatisch im
/// Manager-Dienstplan (Absenz-Overlay). Rücknahme mit Rückfrage löscht die
/// Absenz wieder (nur solange die Lohnperiode nicht verarbeitet ist).
///
/// Rechte (Walter 14.08.2026): NUR GF (user) + admin — superuser sieht den
/// Planer nicht. Planen darf admin überall, GF nur Filialen mit
/// user_branch_access.can_dienstplan (gleiche Pflege wie Manager-DP).
/// </summary>
[Authorize(Roles = "admin,user")]
[ApiController]
[Route("api/ferien-planung")]
public class FerienPlanungController : ControllerBase
{
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;

    public FerienPlanungController(AppDbContext db, LohnEditLockService editLock)
    {
        _db       = db;
        _editLock = editLock;
    }

    private int? GetUserId()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private async Task<(bool isAdmin, HashSet<int> planBranches, string? name)> GetPlanRechteAsync()
    {
        var uid = GetUserId();
        var isAdmin = User.IsInRole("admin");
        var branches = new HashSet<int>();
        string? name = null;
        if (uid.HasValue)
        {
            var u = await _db.AppUsers.AsNoTracking()
                .Where(x => x.Id == uid.Value)
                .Select(x => new
                {
                    x.FirstName, x.LastName, x.Username,
                    Branches = x.BranchAccess.Where(b => b.CanDienstplan)
                                             .Select(b => b.CompanyProfileId).ToList(),
                })
                .FirstOrDefaultAsync();
            if (u != null)
            {
                var voll = $"{u.FirstName} {u.LastName}".Trim();
                name = string.IsNullOrWhiteSpace(voll) ? u.Username : voll;
                foreach (var b in u.Branches) branches.Add(b);
            }
        }
        return (isAdmin, branches, name);
    }

    /// <summary>Heimatfiliale des Managers (spätester laufender FIX-M-Vertrag).</summary>
    private async Task<int?> ResolveBranchIdAsync(int employeeId)
    {
        return await _db.Employments.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.IsActive)
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => (int?)x.CompanyProfileId)
            .FirstOrDefaultAsync();
    }

    private async Task<IActionResult?> CheckPlanbarAsync(int employeeId)
    {
        var (isAdmin, planBranches, _) = await GetPlanRechteAsync();
        if (isAdmin) return null;
        var branchId = await ResolveBranchIdAsync(employeeId);
        if (branchId.HasValue && planBranches.Contains(branchId.Value)) return null;
        return StatusCode(403, new
        {
            error = "KEIN_PLANUNGSRECHT",
            message = "Für die Filiale dieses Managers fehlt dir das Dienstplan-Planungsrecht.",
        });
    }

    /// <summary>
    /// Lohnlauf-Sperre analog AbsencesController: nur DEFINITIV abgeschlossene
    /// Perioden blocken (Soft-Lock).
    /// </summary>
    private async Task<IActionResult?> CheckLohnLockAsync(int employeeId, DateOnly from, DateOnly to)
    {
        var branchId = await ResolveBranchIdAsync(employeeId);
        if (branchId is null) return null;
        var r = await _editLock.CheckRangePeriodAsync(User, branchId.Value, from, to);
        if (!r.Locked) return null;
        return Conflict(new
        {
            error = "LOHN_EDIT_LOCKED",
            message = r.Reason,
            firstAllowedDate = r.FirstAllowedDate?.ToString("yyyy-MM-dd"),
        });
    }

    // ── GET /api/ferien-planung?year&month ───────────────────────────────
    /// <summary>
    /// Monats-Grid: gleiche Manager-Zeilen wie der Manager-Dienstplan,
    /// dazu pro Zeile die Planungen (orange) + ALLE Ferien-Absenzen (grün,
    /// auch direkt im Absenzen-Tab erfasste — Walter 14.08.2026).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMonth([FromQuery] int year, [FromQuery] int month)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return BadRequest(new { error = "PERIODE_UNGUELTIG" });

        var from = new DateOnly(year, month, 1);
        var to   = from.AddMonths(1).AddDays(-1);
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MinValue);

        // FIX-M-Manager wie im Manager-Dienstplan (gleiche Zeilen-Logik).
        var fixmRows = await _db.Employments.AsNoTracking()
            .Where(em => em.EmploymentModel == "FIX-M"
                      && em.CompanyProfileId != null
                      && em.ContractStartDate <= toDt
                      && (em.ContractEndDate == null || em.ContractEndDate >= fromDt)
                      && !em.Employee!.IsHidden && !em.Employee!.IsPayrollExcluded)
            .Select(em => new
            {
                em.EmployeeId, em.CompanyProfileId, em.ContractStartDate,
                em.Employee!.FirstName, em.Employee!.LastName,
                JobCode = em.JobGroup != null ? em.JobGroup.Code : em.JobTitle,
            })
            .ToListAsync();
        var proMa = fixmRows
            .GroupBy(x => x.EmployeeId)
            .Select(g => g.OrderByDescending(x => x.ContractStartDate).First())
            .ToList();
        var empIds = proMa.Select(x => x.EmployeeId).ToList();

        var branches = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.RestaurantCode, c.BranchName, c.City, c.KantonCode, c.WorkLocation })
            .ToListAsync();

        // Planungen, die das Fenster überlappen (echte from/to mitliefern —
        // Balken können über die Monatsgrenze laufen).
        var planungen = await _db.FerienPlanungen.AsNoTracking()
            .Where(p => empIds.Contains(p.EmployeeId) && p.DateFrom <= to && p.DateTo >= from)
            .ToListAsync();

        // Ferien-Absenzen im Fenster (grün) — inkl. direkt erfasster.
        var ferienAbs = await _db.Absences.AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId)
                     && a.AbsenceType == "FERIEN"
                     && a.DateFrom <= to && a.DateTo >= from)
            .Select(a => new { a.Id, a.EmployeeId, a.DateFrom, a.DateTo })
            .ToListAsync();
        var planungByAbsence = planungen
            .Where(p => p.AbsenceId.HasValue)
            .GroupBy(p => p.AbsenceId!.Value)
            .ToDictionary(g => g.Key, g => g.First().Id);

        var (isAdmin, planBranches, _) = await GetPlanRechteAsync();

        var zeilen = proMa
            .OrderBy(x => branches.FirstOrDefault(b => b.Id == x.CompanyProfileId)?.RestaurantCode ?? "")
            .ThenByDescending(x => x.JobCode == "REST_MANAGER")
            .ThenBy(x => x.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LastName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new
            {
                employeeId = x.EmployeeId,
                vorname = x.FirstName ?? "",
                nachname = x.LastName ?? "",
                companyProfileId = x.CompanyProfileId,
                istGf = x.JobCode == "REST_MANAGER",
                planbar = isAdmin || (x.CompanyProfileId.HasValue && planBranches.Contains(x.CompanyProfileId.Value)),
                // GEPLANT-Balken (orange). Ein DEFINITIV ohne Absenz (via
                // Absenzen-Tab gelöscht) fällt automatisch auf orange zurück.
                planungen = planungen
                    .Where(p => p.EmployeeId == x.EmployeeId
                             && (p.Status == "GEPLANT" || p.AbsenceId == null))
                    .OrderBy(p => p.DateFrom)
                    .Select(p => new
                    {
                        id = p.Id,
                        von = p.DateFrom.ToString("yyyy-MM-dd"),
                        bis = p.DateTo.ToString("yyyy-MM-dd"),
                    }),
                ferien = ferienAbs
                    .Where(a => a.EmployeeId == x.EmployeeId)
                    .OrderBy(a => a.DateFrom)
                    .Select(a => new
                    {
                        absenceId = a.Id,
                        planungId = planungByAbsence.TryGetValue(a.Id, out var pid) ? (int?)pid : null,
                        von = a.DateFrom.ToString("yyyy-MM-dd"),
                        bis = a.DateTo.ToString("yyyy-MM-dd"),
                    }),
            });

        var filialen = branches.Select(b => new
        {
            id = b.Id,
            code = (string?)null,
            name = !string.IsNullOrWhiteSpace(b.WorkLocation) ? b.WorkLocation : (b.City ?? b.BranchName),
            kanton = b.KantonCode,
        });

        return Ok(new { year, month, filialen, zeilen });
    }

    public class PlanungDto
    {
        public int EmployeeId { get; set; }
        public string DateFrom { get; set; } = "";
        public string DateTo { get; set; } = "";
    }

    /// <summary>Überlappungs-Check gegen andere GEPLANT-Balken desselben MA.</summary>
    private async Task<IActionResult?> CheckPlanOverlapAsync(int employeeId, DateOnly from, DateOnly to, int? excludeId = null)
    {
        if (to < from)
            return BadRequest(new { error = "INVALID_RANGE", message = "Datum bis darf nicht vor Datum von liegen." });
        var q = _db.FerienPlanungen.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId && p.DateFrom <= to && p.DateTo >= from);
        if (excludeId is int xid) q = q.Where(p => p.Id != xid);
        var hit = await q.OrderBy(p => p.DateFrom).FirstOrDefaultAsync();
        if (hit is null) return null;
        return Conflict(new
        {
            error = "PLANUNG_OVERLAP",
            message = $"Überlappt mit bestehender Ferien-Planung vom "
                    + $"{hit.DateFrom:dd.MM.yyyy}–{hit.DateTo:dd.MM.yyyy}.",
        });
    }

    /// <summary>
    /// Verschmilzt den neuen Bereich mit angrenzenden (±1 Tag) oder
    /// überlappenden GEPLANT-Balken desselben MA zu EINEM Balken — die
    /// geschluckten Einträge werden entfernt (Walter 14.08.2026).
    /// DEFINITIV-Balken mit Absenz bleiben unangetastet.
    /// </summary>
    private async Task<(DateOnly from, DateOnly to)> MergeGeplantAsync(
        int employeeId, DateOnly from, DateOnly to, int? excludeId)
    {
        var nearFrom = from.AddDays(-1);
        var nearTo   = to.AddDays(1);
        var others = await _db.FerienPlanungen
            .Where(p => p.EmployeeId == employeeId
                     && (p.Status == "GEPLANT" || p.AbsenceId == null)
                     && p.DateFrom <= nearTo && p.DateTo >= nearFrom)
            .ToListAsync();
        foreach (var o in others)
        {
            if (excludeId is int xid && o.Id == xid) continue;
            if (o.DateFrom < from) from = o.DateFrom;
            if (o.DateTo > to)     to = o.DateTo;
            _db.FerienPlanungen.Remove(o);
        }
        return (from, to);
    }

    // ── POST /api/ferien-planung ─────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PlanungDto dto)
    {
        if (dto is null || dto.EmployeeId <= 0)
            return BadRequest(new { error = "INVALID_DTO", message = "Mitarbeiter fehlt." });
        if (!DateOnly.TryParse(dto.DateFrom, out var from) || !DateOnly.TryParse(dto.DateTo, out var to))
            return BadRequest(new { error = "INVALID_DATE", message = "Ungültiges Datum." });

        var recht = await CheckPlanbarAsync(dto.EmployeeId);
        if (recht != null) return recht;
        if (to < from)
            return BadRequest(new { error = "INVALID_RANGE", message = "Datum bis darf nicht vor Datum von liegen." });
        // Angrenzende/überlappende GEPLANT-Balken automatisch zu EINEM
        // Balken verschmelzen (Walter 14.08.2026 — Einzeltage werden so
        // beim Weiterplanen aufgeräumt).
        (from, to) = await MergeGeplantAsync(dto.EmployeeId, from, to, excludeId: null);

        var (_, _, actor) = await GetPlanRechteAsync();
        var p = new FerienPlanung
        {
            EmployeeId = dto.EmployeeId,
            DateFrom   = from,
            DateTo     = to,
            Status     = "GEPLANT",
            CreatedBy  = actor,
            CreatedAt  = DateTime.Now,
            UpdatedAt  = DateTime.Now,
        };
        _db.FerienPlanungen.Add(p);
        await _db.SaveChangesAsync();
        return Ok(new { id = p.Id });
    }

    // ── PUT /api/ferien-planung/{id} (verschieben / Länge ändern) ────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] PlanungDto dto)
    {
        var p = await _db.FerienPlanungen.FindAsync(id);
        if (p is null) return NotFound();
        if (p.Status == "DEFINITIV" && p.AbsenceId != null)
            return Conflict(new { error = "SCHON_DEFINITIV", message = "Definitiv gesetzte Ferien zuerst zurücknehmen." });
        if (!DateOnly.TryParse(dto.DateFrom, out var from) || !DateOnly.TryParse(dto.DateTo, out var to))
            return BadRequest(new { error = "INVALID_DATE", message = "Ungültiges Datum." });

        var recht = await CheckPlanbarAsync(p.EmployeeId);
        if (recht != null) return recht;
        if (to < from)
            return BadRequest(new { error = "INVALID_RANGE", message = "Datum bis darf nicht vor Datum von liegen." });
        (from, to) = await MergeGeplantAsync(p.EmployeeId, from, to, excludeId: id);

        p.DateFrom  = from;
        p.DateTo    = to;
        p.Status    = "GEPLANT";
        p.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { id = p.Id });
    }

    // ── DELETE /api/ferien-planung/{id} ──────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await _db.FerienPlanungen.FindAsync(id);
        if (p is null) return NotFound();
        if (p.Status == "DEFINITIV" && p.AbsenceId != null)
            return Conflict(new { error = "SCHON_DEFINITIV", message = "Definitiv gesetzte Ferien zuerst zurücknehmen." });
        var recht = await CheckPlanbarAsync(p.EmployeeId);
        if (recht != null) return recht;

        _db.FerienPlanungen.Remove(p);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── POST /api/ferien-planung/{id}/definitiv ──────────────────────────
    /// <summary>
    /// Orange → Grün: erzeugt die echte Ferien-Absenz (WorkedDays +
    /// HoursCredited wie der Absenzen-Tab, via AbsenceHoursRecalcService-
    /// Helfer). Prüft Lohnlauf-Sperre + Absenz-Überlappung (pro Tag nur
    /// eine Absenz — Walter 26.07.2026).
    /// </summary>
    [HttpPost("{id:int}/definitiv")]
    public async Task<IActionResult> Definitiv(int id)
    {
        var p = await _db.FerienPlanungen.FindAsync(id);
        if (p is null) return NotFound();
        if (p.Status == "DEFINITIV" && p.AbsenceId != null)
            return Ok(new { id = p.Id, absenceId = p.AbsenceId });

        var recht = await CheckPlanbarAsync(p.EmployeeId);
        if (recht != null) return recht;
        var locked = await CheckLohnLockAsync(p.EmployeeId, p.DateFrom, p.DateTo);
        if (locked != null) return locked;

        // Pro Tag nur EINE Absenz — gegen ALLE Absenz-Typen prüfen.
        var conflict = await _db.Absences.AsNoTracking()
            .Where(a => a.EmployeeId == p.EmployeeId
                     && a.DateFrom <= p.DateTo && a.DateTo >= p.DateFrom)
            .OrderBy(a => a.DateFrom)
            .FirstOrDefaultAsync();
        if (conflict != null)
        {
            return Conflict(new
            {
                error = "ABSENCE_OVERLAP",
                message = $"Überlappung mit bestehender Absenz ({conflict.AbsenceType}) vom "
                        + $"{conflict.DateFrom:dd.MM.yyyy}–{conflict.DateTo:dd.MM.yyyy} — "
                        + "Balken zuerst verschieben.",
            });
        }

        // Stunden/Tage wie im Absenzen-Tab: AbsenzTyp FERIEN + laufender
        // Vertrag + Filial-Profil.
        var typ = await _db.AbsenzTypen.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == "FERIEN");
        if (typ is null)
            return Conflict(new { error = "TYP_FEHLT", message = "Absenz-Typ «Ferien» ist nicht konfiguriert." });

        var fromDt = p.DateFrom.ToDateTime(TimeOnly.MinValue);
        var emp = await _db.Employments.AsNoTracking()
            .Where(x => x.EmployeeId == p.EmployeeId
                     && x.ContractStartDate <= fromDt
                     && (x.ContractEndDate == null || x.ContractEndDate >= fromDt))
            .OrderByDescending(x => x.ContractStartDate)
            .FirstOrDefaultAsync()
            ?? await _db.Employments.AsNoTracking()
                .Where(x => x.EmployeeId == p.EmployeeId && x.IsActive)
                .OrderByDescending(x => x.ContractStartDate)
                .FirstOrDefaultAsync();
        CompanyProfile? profile = null;
        if (emp?.CompanyProfileId is int bid)
            profile = await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == bid);

        var model = emp?.EmploymentModel ?? "";
        var days = AbsenceHoursRecalcService.BuildDaysForModus(p.DateFrom, p.DateTo, typ.GutschriftModus ?? "1/7");
        var hours = AbsenceHoursRecalcService.ComputeHours(
            "FERIEN", model, typ, profile, emp, days.Count, 100m);

        var (_, _, actor) = await GetPlanRechteAsync();
        var absence = new Absence
        {
            EmployeeId    = p.EmployeeId,
            AbsenceType   = "FERIEN",
            DateFrom      = p.DateFrom,
            DateTo        = p.DateTo,
            WorkedDays    = JsonSerializer.Serialize(days),
            HoursCredited = hours,
            Prozent       = 100m,
            Notes         = $"Ferienplaner ({actor ?? "GF"})",
            CreatedAt     = DateTime.Now,
            UpdatedAt     = DateTime.Now,
        };
        _db.Absences.Add(absence);
        p.Status    = "DEFINITIV";
        p.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        p.AbsenceId = absence.Id;
        await _db.SaveChangesAsync();

        return Ok(new { id = p.Id, absenceId = absence.Id });
    }

    // ── POST /api/ferien-planung/{id}/zuruecknehmen ──────────────────────
    /// <summary>Grün → Orange: löscht die Ferien-Absenz wieder (mit Lock-Check).</summary>
    [HttpPost("{id:int}/zuruecknehmen")]
    public async Task<IActionResult> Zuruecknehmen(int id)
    {
        var p = await _db.FerienPlanungen.FindAsync(id);
        if (p is null) return NotFound();
        var recht = await CheckPlanbarAsync(p.EmployeeId);
        if (recht != null) return recht;

        if (p.AbsenceId is int aid)
        {
            var absence = await _db.Absences.FindAsync(aid);
            if (absence != null)
            {
                var locked = await CheckLohnLockAsync(absence.EmployeeId, absence.DateFrom, absence.DateTo);
                if (locked != null) return locked;
                _db.Absences.Remove(absence);
            }
        }
        p.Status    = "GEPLANT";
        p.AbsenceId = null;
        p.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { id = p.Id });
    }

    // ── POST /api/ferien-planung/absenz/{absenceId}/zuruecknehmen ────────
    /// <summary>
    /// Rücknahme einer Ferien-Absenz OHNE Planung (direkt im Absenzen-Tab
    /// erfasst): löscht die Absenz und legt einen orangen Planungs-Balken
    /// mit demselben Zeitraum an — damit bleibt er im Planer verschiebbar.
    /// </summary>
    [HttpPost("absenz/{absenceId:int}/zuruecknehmen")]
    public async Task<IActionResult> AbsenzZuruecknehmen(int absenceId)
    {
        var absence = await _db.Absences.FindAsync(absenceId);
        if (absence is null) return NotFound();
        if (absence.AbsenceType != "FERIEN")
            return BadRequest(new { error = "NICHT_FERIEN", message = "Nur Ferien-Absenzen können hier zurückgenommen werden." });

        var recht = await CheckPlanbarAsync(absence.EmployeeId);
        if (recht != null) return recht;
        var locked = await CheckLohnLockAsync(absence.EmployeeId, absence.DateFrom, absence.DateTo);
        if (locked != null) return locked;

        // Falls doch eine Planung dranhängt → deren Pfad nutzen.
        var linked = await _db.FerienPlanungen.FirstOrDefaultAsync(p => p.AbsenceId == absenceId);
        var (_, _, actor) = await GetPlanRechteAsync();
        if (linked != null)
        {
            linked.Status    = "GEPLANT";
            linked.AbsenceId = null;
            linked.UpdatedAt = DateTime.Now;
        }
        else
        {
            _db.FerienPlanungen.Add(new FerienPlanung
            {
                EmployeeId = absence.EmployeeId,
                DateFrom   = absence.DateFrom,
                DateTo     = absence.DateTo,
                Status     = "GEPLANT",
                CreatedBy  = actor,
                CreatedAt  = DateTime.Now,
                UpdatedAt  = DateTime.Now,
            });
        }
        _db.Absences.Remove(absence);
        await _db.SaveChangesAsync();
        return Ok();
    }
}

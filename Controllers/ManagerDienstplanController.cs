using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Manager-Dienstplan (Walter-Vorgabe 08.08.2026, ersetzt die Excel):
/// Monats-Grid über ALLE Filialen — Zeilen = FIX-M-MA mit laufendem Vertrag
/// (Heimatfiliale = Filiale des laufenden FIX-M-Vertrags), Zellen = Kürzel
/// aus dienstplan_code. Absenzen (Ferien/Krank/…) kommen als Live-Overlay
/// aus den Absences und sperren die Zelle serverseitig.
///
/// Rechte: SEHEN darf jeder mit Zugriff (admin/superuser/user), PLANEN darf
/// admin überall, sonst nur Filialen mit user_branch_access.can_dienstplan
/// (Pflege im Filial-Tab «Unterzeichner»).
/// </summary>
[Authorize(Roles = "admin,superuser,user")]
[ApiController]
[Route("api/manager-dienstplan")]
public class ManagerDienstplanController : ControllerBase
{
    private readonly AppDbContext _db;
    public ManagerDienstplanController(AppDbContext db) => _db = db;

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
            // DisplayName ist ein berechnetes Property (nicht EF-übersetzbar) —
            // Rohfelder laden und im Speicher zusammensetzen.
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

    /// <summary>Monats-Grid: Zeilen, Plan-Zellen, Absenz-Overlay, Kürzel-Katalog.</summary>
    [HttpGet]
    public async Task<IActionResult> GetMonth([FromQuery] int year, [FromQuery] int month)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return BadRequest(new { error = "PERIODE_UNGUELTIG" });
        var from = new DateOnly(year, month, 1);
        var to   = from.AddMonths(1).AddDays(-1);
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MinValue);

        // FIX-M-MA mit im Monat LAUFENDEM Vertrag; Heimatfiliale = Filiale
        // des (spätesten) laufenden FIX-M-Vertrags.
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
            })
            .ToListAsync();
        var proMa = fixmRows
            .GroupBy(x => x.EmployeeId)
            .Select(g => g.OrderByDescending(x => x.ContractStartDate).First())
            .ToList();
        var empIds = proMa.Select(x => x.EmployeeId).ToList();

        var branches = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.RestaurantCode, c.BranchName, c.City })
            .ToListAsync();

        var plan = await _db.ManagerDienstplanEntries.AsNoTracking()
            .Where(p => empIds.Contains(p.EmployeeId) && p.Datum >= from && p.Datum <= to)
            .Select(p => new { p.EmployeeId, p.Datum, p.Code })
            .ToListAsync();

        var absences = await _db.Absences.AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId) && a.DateFrom <= to && a.DateTo >= from)
            .Select(a => new { a.EmployeeId, a.AbsenceType, a.DateFrom, a.DateTo })
            .ToListAsync();

        var codes = await _db.DienstplanCodes.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new { c.Code, c.Bezeichnung, c.Farbe })
            .ToListAsync();

        var (isAdmin, planBranches, _) = await GetPlanRechteAsync();

        var zeilen = proMa
            .OrderBy(x => branches.FirstOrDefault(b => b.Id == x.CompanyProfileId)?.RestaurantCode ?? "")
            .ThenBy(x => x.FirstName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new
            {
                employeeId = x.EmployeeId,
                vorname = x.FirstName,
                nachname = x.LastName,
                companyProfileId = x.CompanyProfileId,
                planbar = isAdmin || (x.CompanyProfileId.HasValue && planBranches.Contains(x.CompanyProfileId.Value)),
                zellen = plan.Where(p => p.EmployeeId == x.EmployeeId)
                    .ToDictionary(p => p.Datum.ToString("yyyy-MM-dd"), p => p.Code),
                absenzen = absences.Where(a => a.EmployeeId == x.EmployeeId)
                    .Select(a => new
                    {
                        typ = a.AbsenceType,
                        von = (a.DateFrom < from ? from : a.DateFrom).ToString("yyyy-MM-dd"),
                        bis = (a.DateTo > to ? to : a.DateTo).ToString("yyyy-MM-dd"),
                    }).ToList(),
            })
            .ToList();

        return Ok(new
        {
            year, month,
            filialen = branches.Select(b => new { b.Id, code = b.RestaurantCode, name = b.BranchName ?? b.City }),
            zeilen,
            codes,
        });
    }

    public class CellDto
    {
        public int EmployeeId { get; set; }
        public string? Datum { get; set; }   // ISO yyyy-MM-dd
        public string? Code { get; set; }    // null/leer = Zelle löschen
    }

    /// <summary>Zelle setzen/löschen — nur mit Planungsrecht auf der Heimatfiliale des MA.</summary>
    [HttpPut("cell")]
    public async Task<IActionResult> PutCell([FromBody] CellDto dto)
    {
        if (!DateOnly.TryParse(dto.Datum, out var datum))
            return BadRequest(new { error = "DATUM_UNGUELTIG" });

        var (isAdmin, planBranches, name) = await GetPlanRechteAsync();
        if (!isAdmin)
        {
            // Heimatfiliale des MA am Datum (laufender FIX-M-Vertrag).
            var dt = datum.ToDateTime(TimeOnly.MinValue);
            var cpId = await _db.Employments.AsNoTracking()
                .Where(em => em.EmployeeId == dto.EmployeeId
                          && em.EmploymentModel == "FIX-M"
                          && em.ContractStartDate <= dt
                          && (em.ContractEndDate == null || em.ContractEndDate >= dt))
                .OrderByDescending(em => em.ContractStartDate)
                .Select(em => em.CompanyProfileId)
                .FirstOrDefaultAsync();
            if (!cpId.HasValue || !planBranches.Contains(cpId.Value))
                return StatusCode(403, new
                {
                    error = "KEIN_PLANRECHT",
                    message = "Kein Planungsrecht für die Filiale dieses MA (Filial-Tab «Unterzeichner» → Häkchen «Dienstplan»).",
                });
        }

        // Absenz-Kollision: Zelle mit Absenz ist gesperrt.
        bool absenz = await _db.Absences.AnyAsync(a =>
            a.EmployeeId == dto.EmployeeId && a.DateFrom <= datum && a.DateTo >= datum);
        if (absenz)
            return Conflict(new
            {
                error = "ABSENZ_GESPERRT",
                message = "An diesem Tag besteht eine Absenz — Planung nicht möglich (Absenzen im MA-Detail pflegen).",
            });

        var entry = await _db.ManagerDienstplanEntries
            .FirstOrDefaultAsync(p => p.EmployeeId == dto.EmployeeId && p.Datum == datum);
        var code = (dto.Code ?? "").Trim();
        if (string.IsNullOrEmpty(code))
        {
            if (entry != null) _db.ManagerDienstplanEntries.Remove(entry);
        }
        else
        {
            bool gueltig = await _db.DienstplanCodes.AnyAsync(c => c.Code == code && c.IsActive);
            if (!gueltig) return BadRequest(new { error = "CODE_UNBEKANNT" });
            if (entry == null)
            {
                entry = new ManagerDienstplanEntry { EmployeeId = dto.EmployeeId, Datum = datum };
                _db.ManagerDienstplanEntries.Add(entry);
            }
            entry.Code = code;
            entry.UpdatedAt = DateTime.Now;
            entry.UpdatedBy = name;
        }
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}

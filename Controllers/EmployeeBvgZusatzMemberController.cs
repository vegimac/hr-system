using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HrSystem.Controllers;

/// <summary>
/// CRUD für die versionierte BVG-Zusatz-Mitgliedschaft eines Mitarbeiters
/// (Walter-Vorgabe 26.05.2026). Löst die hartcodierte „nur FIX-M"-Logik ab —
/// jetzt entscheidet eine Mitgliedschaft pro MA mit Von/Bis-Zeitfenster,
/// ob am Periodenanfang BVG_ZUSATZ-Beiträge berechnet werden.
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/bvg-zusatz-member")]
[Authorize(Roles = "admin,superuser,user")]
public class EmployeeBvgZusatzMemberController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LohnEditLockService _editLock;
    public EmployeeBvgZusatzMemberController(AppDbContext db, LohnEditLockService editLock)
    { _db = db; _editLock = editLock; }

    public record UpsertDto(DateOnly ValidFrom, DateOnly? ValidTo, string? Bemerkung);

    [HttpGet]
    public async Task<IActionResult> List(int employeeId)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var entries = await _db.EmployeeBvgZusatzMembers
            .Where(m => m.EmployeeId == employeeId)
            .OrderByDescending(m => m.ValidFrom)
            .Select(m => new
            {
                m.Id,
                m.EmployeeId,
                m.ValidFrom,
                m.ValidTo,
                m.Bemerkung,
                m.CreatedAt,
                m.CreatedBy,
                isCurrent = m.ValidFrom <= today && (m.ValidTo == null || m.ValidTo >= today)
            })
            .ToListAsync();
        return Ok(entries);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, [FromBody] UpsertDto dto)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound();

        // Lohn-Edit-Lock: ValidFrom nicht rückwirkend in verarbeitete Periode.
        var branchId = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;
        if (firstAllowed.HasValue && dto.ValidFrom < firstAllowed.Value)
            return Conflict(new
            {
                error = "LOHN_EDIT_LOCKED",
                message = $"'Gültig ab {dto.ValidFrom:dd.MM.yyyy}' liegt in einer bereits in Verarbeitung befindlichen Lohnperiode. Frühestes erlaubtes 'Gültig ab': {firstAllowed.Value:dd.MM.yyyy}.",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });

        // Überlappung mit anderen Mitgliedschafts-Einträgen verhindern.
        var overlap = await FindOverlappingAsync(employeeId, dto.ValidFrom, dto.ValidTo, excludeId: null);
        if (overlap != null)
            return Conflict(new
            {
                error = "BVG_MEMBER_OVERLAP",
                message = $"Die Mitgliedschaft {dto.ValidFrom:dd.MM.yyyy}–{(dto.ValidTo?.ToString("dd.MM.yyyy") ?? "offen")} überschneidet sich mit einer bestehenden ({overlap.ValidFrom:dd.MM.yyyy}–{(overlap.ValidTo?.ToString("dd.MM.yyyy") ?? "offen")})."
            });

        var entry = new EmployeeBvgZusatzMember
        {
            EmployeeId = employeeId,
            ValidFrom  = dto.ValidFrom,
            ValidTo    = dto.ValidTo,
            Bemerkung  = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim(),
            CreatedAt  = DateTime.UtcNow,
            CreatedBy  = GetCurrentUserId()
        };
        _db.EmployeeBvgZusatzMembers.Add(entry);
        await _db.SaveChangesAsync();
        return Ok(new { id = entry.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int employeeId, int id, [FromBody] UpsertDto dto)
    {
        var entry = await _db.EmployeeBvgZusatzMembers
            .FirstOrDefaultAsync(m => m.Id == id && m.EmployeeId == employeeId);
        if (entry == null) return NotFound();

        var branchId = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;
        // Bestehender Eintrag: wenn ValidFrom in verarbeiteter Periode, nicht
        // editierbar — neue Mitgliedschaft mit Folge-Datum anlegen.
        if (firstAllowed.HasValue && entry.ValidFrom < firstAllowed.Value)
            return Conflict(new
            {
                error = "LOHN_EDIT_LOCKED",
                message = $"Diese Mitgliedschaft (gültig ab {entry.ValidFrom:dd.MM.yyyy}) wurde bereits in einem Lohnlauf verwendet. Bitte einen neuen Eintrag ab frühestens {firstAllowed:dd.MM.yyyy} anlegen.",
                firstAllowedDate = firstAllowed?.ToString("yyyy-MM-dd")
            });

        var overlap = await FindOverlappingAsync(employeeId, dto.ValidFrom, dto.ValidTo, excludeId: entry.Id);
        if (overlap != null)
            return Conflict(new
            {
                error = "BVG_MEMBER_OVERLAP",
                message = $"Die Mitgliedschaft {dto.ValidFrom:dd.MM.yyyy}–{(dto.ValidTo?.ToString("dd.MM.yyyy") ?? "offen")} überschneidet sich mit einer bestehenden ({overlap.ValidFrom:dd.MM.yyyy}–{(overlap.ValidTo?.ToString("dd.MM.yyyy") ?? "offen")})."
            });

        entry.ValidFrom = dto.ValidFrom;
        entry.ValidTo   = dto.ValidTo;
        entry.Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int employeeId, int id)
    {
        var entry = await _db.EmployeeBvgZusatzMembers
            .FirstOrDefaultAsync(m => m.Id == id && m.EmployeeId == employeeId);
        if (entry == null) return NotFound();

        var branchId = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;
        if (firstAllowed.HasValue && entry.ValidFrom < firstAllowed.Value)
            return Conflict(new
            {
                error = "LOHN_EDIT_LOCKED",
                message = $"Diese Mitgliedschaft (gültig ab {entry.ValidFrom:dd.MM.yyyy}) wurde bereits in einem Lohnlauf verwendet und kann nicht gelöscht werden."
            });

        _db.EmployeeBvgZusatzMembers.Remove(entry);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── Helfer ──────────────────────────────────────────────────────────

    private async Task<EmployeeBvgZusatzMember?> FindOverlappingAsync(
        int employeeId, DateOnly newFrom, DateOnly? newTo, int? excludeId)
    {
        var max = new DateOnly(9999, 12, 31);
        var newToEff = newTo ?? max;
        var others = await _db.EmployeeBvgZusatzMembers
            .Where(m => m.EmployeeId == employeeId
                     && (excludeId == null || m.Id != excludeId.Value))
            .ToListAsync();
        foreach (var o in others)
        {
            var oTo = o.ValidTo ?? max;
            if (newFrom <= oTo && o.ValidFrom <= newToEff) return o;
        }
        return null;
    }

    private async Task<int?> GetEmployeeBranchAsync(int employeeId)
    {
        return await _db.Employments
            .Where(e => e.EmployeeId == employeeId && e.IsActive)
            .OrderByDescending(e => e.ContractStartDate)
            .Select(e => (int?)e.CompanyProfileId)
            .FirstOrDefaultAsync();
    }

    private int? GetCurrentUserId()
    {
        var s = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(s, out var id) ? id : null;
    }
}

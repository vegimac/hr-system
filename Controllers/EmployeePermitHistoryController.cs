using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Bewilligungs-Verlauf pro Mitarbeiter.
///
/// Routen:
///   GET    /api/employees/{employeeId}/permit-history
///   POST   /api/employees/{employeeId}/permit-history
///   PUT    /api/employees/{employeeId}/permit-history/{id}
///   DELETE /api/employees/{employeeId}/permit-history/{id}
///
/// Auto-Sync: nach jedem Schreibvorgang wird der "aktuelle" Eintrag
/// (valid_from &lt;= heute, valid_to NULL oder &gt;= heute, höchstes
/// valid_from) ermittelt und auf employee.permit_type_id +
/// employee.permit_expiry_date geschrieben. Wenn aktuell kein gültiger
/// Eintrag existiert, werden beide Felder auf NULL gesetzt.
///
/// Beim Anlegen eines neuen Eintrags wird der vorherige offene Eintrag
/// automatisch geschlossen (valid_to = neuer.valid_from - 1 Tag).
/// </summary>
[Authorize]
[ApiController]
[Route("api/employees/{employeeId:int}/permit-history")]
public class EmployeePermitHistoryController : ControllerBase
{
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;
    public EmployeePermitHistoryController(AppDbContext db, LohnEditLockService editLock)
    {
        _db = db; _editLock = editLock;
    }

    private Task<int?> GetEmployeeBranchAsync(int employeeId)
        => _db.Employees
            .Where(e => e.Id == employeeId)
            .SelectMany(e => e.Employments)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => (int?)x.CompanyProfileId)
            .FirstOrDefaultAsync();

    public class PermitHistoryDto
    {
        public int     Id { get; set; }
        public int     EmployeeId { get; set; }
        public int?    PermitTypeId { get; set; }
        public string? PermitCode { get; set; }
        public string? PermitDescription { get; set; }
        public DateOnly  ValidFrom { get; set; }
        public DateOnly? ValidTo   { get; set; }
        public DateOnly? PermitExpiryDate { get; set; }
        public string?   Note { get; set; }
        public DateTime CreatedAt { get; set; }
        public int?     CreatedByUserId { get; set; }
        public bool     IsCurrent { get; set; }   // valid_to NULL und valid_from <= heute
        public bool     InLohnVerwendet { get; set; }
    }

    public class PermitHistoryUpsertDto
    {
        public int?    PermitTypeId { get; set; }       // NULL = Einbürgerung / keine Bewilligung mehr
        public DateOnly  ValidFrom { get; set; }
        public DateOnly? ValidTo   { get; set; }
        public DateOnly? PermitExpiryDate { get; set; }
        public string?   Note { get; set; }
    }

    /// <summary>
    /// Liefert die IDs aller Mitarbeiter mit MINDESTENS einem Permit-History-
    /// Eintrag (egal ob aktuell gültig oder abgelaufen). Frontend nutzt das
    /// für den Spezialfilter „Keine Bewilligung" → das Komplement.
    /// Walter-Vorgabe 18.05.2026 — analog /api/employee-bank-accounts/active-employee-ids.
    /// </summary>
    [HttpGet("/api/employee-permit-history/employee-ids-with-history")]
    public async Task<IActionResult> GetEmployeeIdsWithHistory()
    {
        var ids = await _db.EmployeePermitHistories
            .Select(h => h.EmployeeId)
            .Distinct()
            .ToListAsync();
        return Ok(ids);
    }

    [HttpGet]
    public async Task<IActionResult> List(int employeeId)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound();

        var entries = await _db.EmployeePermitHistories
            .AsNoTracking()
            .Include(h => h.PermitType)
            .Where(h => h.EmployeeId == employeeId)
            .OrderByDescending(h => h.ValidFrom)
            .ThenByDescending(h => h.Id)
            .ToListAsync();

        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var dtos = entries.Select(h => new PermitHistoryDto
        {
            Id                = h.Id,
            EmployeeId        = h.EmployeeId,
            PermitTypeId      = h.PermitTypeId,
            PermitCode        = h.PermitType?.Code,
            PermitDescription = h.PermitType?.Description,
            ValidFrom         = h.ValidFrom,
            ValidTo           = h.ValidTo,
            PermitExpiryDate  = h.PermitExpiryDate,
            Note              = h.Note,
            CreatedAt         = h.CreatedAt,
            CreatedByUserId   = h.CreatedByUserId,
            IsCurrent         = h.ValidFrom <= today && (h.ValidTo == null || h.ValidTo >= today),
            InLohnVerwendet   = firstAllowed.HasValue && h.ValidFrom < firstAllowed.Value
        }).ToList();

        return Ok(dtos);
    }

    [HttpPost]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Create(int employeeId, [FromBody] PermitHistoryUpsertDto dto)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound();

        if (dto.PermitTypeId.HasValue)
        {
            var pt = await _db.PermitTypes.FirstOrDefaultAsync(p => p.Id == dto.PermitTypeId.Value);
            if (pt == null) return BadRequest(new { error = "Bewilligungstyp nicht gefunden." });
        }

        // Walter 17.05.2026: ValidFrom darf nicht rückwirkend in verarbeitete Periode.
        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;
        if (firstAllowed.HasValue && dto.ValidFrom < firstAllowed.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"'Gültig ab {dto.ValidFrom:dd.MM.yyyy}' liegt in einer bereits in Verarbeitung befindlichen Lohnperiode. Frühestes erlaubtes 'Gültig ab': {firstAllowed.Value:dd.MM.yyyy}.",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });
        }

        // Vorherigen offenen Eintrag (valid_to = NULL) automatisch schliessen,
        // sofern dessen valid_from < neuer valid_from. Andernfalls bleibt
        // er offen — User kann manuell aufräumen.
        var prev = await _db.EmployeePermitHistories
            .Where(h => h.EmployeeId == employeeId
                     && h.ValidTo == null
                     && h.ValidFrom < dto.ValidFrom)
            .OrderByDescending(h => h.ValidFrom)
            .FirstOrDefaultAsync();
        if (prev != null)
        {
            prev.ValidTo = dto.ValidFrom.AddDays(-1);
        }

        var entry = new EmployeePermitHistory
        {
            EmployeeId       = employeeId,
            PermitTypeId     = dto.PermitTypeId,
            ValidFrom        = dto.ValidFrom,
            ValidTo          = dto.ValidTo,
            PermitExpiryDate = dto.PermitExpiryDate,
            Note             = dto.Note,
            CreatedAt        = DateTime.UtcNow,
            CreatedByUserId  = GetCurrentUserId()
        };
        _db.EmployeePermitHistories.Add(entry);
        await _db.SaveChangesAsync();

        await SyncEmployeeFromHistoryAsync(employeeId);
        await _db.SaveChangesAsync();

        return Ok(new { id = entry.Id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Update(int employeeId, int id, [FromBody] PermitHistoryUpsertDto dto)
    {
        var entry = await _db.EmployeePermitHistories
            .FirstOrDefaultAsync(h => h.Id == id && h.EmployeeId == employeeId);
        if (entry == null) return NotFound();

        // Walter 17.05.2026: bereits in Lohn verwendet → nicht editierbar.
        var branchIdU     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowedU = branchIdU.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchIdU.Value)
            : null;
        if (firstAllowedU.HasValue && entry.ValidFrom < firstAllowedU.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Dieser Bewilligungs-Eintrag (gültig ab {entry.ValidFrom:dd.MM.yyyy}) wurde bereits in einem Lohnlauf verwendet. Bitte einen neuen Eintrag ab frühestens {firstAllowedU:dd.MM.yyyy} anlegen.",
                firstAllowedDate = firstAllowedU?.ToString("yyyy-MM-dd")
            });
        }

        if (dto.PermitTypeId.HasValue)
        {
            var pt = await _db.PermitTypes.FirstOrDefaultAsync(p => p.Id == dto.PermitTypeId.Value);
            if (pt == null) return BadRequest(new { error = "Bewilligungstyp nicht gefunden." });
        }

        entry.PermitTypeId     = dto.PermitTypeId;
        entry.ValidFrom        = dto.ValidFrom;
        entry.ValidTo          = dto.ValidTo;
        entry.PermitExpiryDate = dto.PermitExpiryDate;
        entry.Note             = dto.Note;

        await _db.SaveChangesAsync();
        await SyncEmployeeFromHistoryAsync(employeeId);
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Delete(int employeeId, int id)
    {
        var entry = await _db.EmployeePermitHistories
            .FirstOrDefaultAsync(h => h.Id == id && h.EmployeeId == employeeId);
        if (entry == null) return NotFound();

        var branchIdD     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowedD = branchIdD.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchIdD.Value)
            : null;
        if (firstAllowedD.HasValue && entry.ValidFrom < firstAllowedD.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Dieser Bewilligungs-Eintrag (gültig ab {entry.ValidFrom:dd.MM.yyyy}) wurde bereits in einem Lohnlauf verwendet und kann nicht gelöscht werden.",
                firstAllowedDate = firstAllowedD?.ToString("yyyy-MM-dd")
            });
        }

        _db.EmployeePermitHistories.Remove(entry);
        await _db.SaveChangesAsync();
        await SyncEmployeeFromHistoryAsync(employeeId);
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>
    /// Setzt employee.permit_type_id und employee.permit_expiry_date auf
    /// den Eintrag, der heute gültig ist. Wenn keiner heute gültig ist,
    /// werden beide Felder auf NULL gesetzt.
    /// </summary>
    private async Task SyncEmployeeFromHistoryAsync(int employeeId)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var current = await _db.EmployeePermitHistories
            .Where(h => h.EmployeeId == employeeId
                     && h.ValidFrom <= today
                     && (h.ValidTo == null || h.ValidTo >= today))
            .OrderByDescending(h => h.ValidFrom)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync();

        if (current != null)
        {
            emp.PermitTypeId     = current.PermitTypeId;
            emp.PermitExpiryDate = current.PermitExpiryDate.HasValue
                ? current.PermitExpiryDate.Value.ToDateTime(TimeOnly.MinValue)
                : null;
        }
        else
        {
            emp.PermitTypeId     = null;
            emp.PermitExpiryDate = null;
        }
    }

    private int? GetCurrentUserId()
    {
        var sub = User.FindFirstValue("sub")
               ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }
}

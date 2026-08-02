using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Lohnabtretungen pro Mitarbeiter (Lohnpfändung, Vorschuss Sozialamt etc.).
/// Werden in jedem Lohnlauf im Gültigkeitszeitraum automatisch vom Netto
/// abgezogen.
/// </summary>
[Authorize]
[ApiController]
[Route("api/employee-lohn-assignments")]
public class EmployeeLohnAssignmentsController : ControllerBase
{
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;
    public EmployeeLohnAssignmentsController(AppDbContext db, LohnEditLockService editLock)
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

    // GET /api/employee-lohn-assignments/{employeeId}
    [HttpGet("{employeeId:int}")]
    public async Task<IActionResult> GetByEmployee(int employeeId)
    {
        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;

        var entries = await _db.EmployeeLohnAssignments
            .Include(a => a.Behoerde)
            .Include(a => a.Sachbearbeiter)
            .Where(a => a.EmployeeId == employeeId)
            .OrderBy(a => a.ValidFrom)
            .ToListAsync();
        return Ok(entries.Select(a => MapToDto(a, firstAllowed)).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LohnAssignmentDto dto)
    {
        var err = await ValidateAsync(dto);
        if (err != null) return BadRequest(err);

        var newFrom      = DateOnly.Parse(dto.ValidFrom);
        var branchId     = await GetEmployeeBranchAsync(dto.EmployeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;
        if (firstAllowed.HasValue && newFrom < firstAllowed.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"'Gültig ab {newFrom:dd.MM.yyyy}' liegt in einer bereits in Verarbeitung befindlichen Lohnperiode. Frühestes erlaubtes 'Gültig ab': {firstAllowed.Value:dd.MM.yyyy}.",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });
        }

        var entry = new EmployeeLohnAssignment
        {
            EmployeeId       = dto.EmployeeId,
            BehoerdeId       = dto.BehoerdeId,
            BehoerdeSachbearbeiterId = dto.BehoerdeSachbearbeiterId,
            Bezeichnung      = dto.Bezeichnung?.Trim() ?? "Lohnpfändung",
            Freigrenze       = Math.Round(dto.Freigrenze, 2),
            Zielbetrag       = Math.Round(dto.Zielbetrag, 2),
            BereitsAbgezogen = 0,
            ValidFrom        = DateOnly.Parse(dto.ValidFrom),
            ValidTo          = string.IsNullOrWhiteSpace(dto.ValidTo) ? null : DateOnly.Parse(dto.ValidTo!),
            ReferenzAmt           = dto.ReferenzAmt?.Trim(),
            ZahlungsReferenz      = dto.ZahlungsReferenz?.Trim(),
            Bemerkung             = dto.Bemerkung?.Trim(),
            LohnausweisAnBehoerde = dto.LohnausweisAnBehoerde,
            CreatedAt             = DateTime.Now,
            UpdatedAt             = DateTime.Now
        };
        _db.EmployeeLohnAssignments.Add(entry);
        await _db.SaveChangesAsync();

        var saved = await _db.EmployeeLohnAssignments
            .Include(a => a.Behoerde)
            .Include(a => a.Sachbearbeiter)
            .FirstAsync(a => a.Id == entry.Id);
        return Ok(MapToDto(saved, firstAllowed));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] LohnAssignmentDto dto)
    {
        var entry = await _db.EmployeeLohnAssignments.FindAsync(id);
        if (entry == null) return NotFound();

        var branchIdU     = await GetEmployeeBranchAsync(entry.EmployeeId);
        var firstAllowedU = branchIdU.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchIdU.Value)
            : null;
        if (firstAllowedU.HasValue && entry.ValidFrom < firstAllowedU.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Diese Lohnabtretung (gültig ab {entry.ValidFrom:dd.MM.yyyy}) wurde bereits in einem Lohnlauf verwendet. Bitte einen neuen Eintrag ab frühestens {firstAllowedU:dd.MM.yyyy} anlegen.",
                firstAllowedDate = firstAllowedU?.ToString("yyyy-MM-dd")
            });
        }

        var err = await ValidateAsync(dto);
        if (err != null) return BadRequest(err);

        entry.BehoerdeId       = dto.BehoerdeId;
        entry.BehoerdeSachbearbeiterId = dto.BehoerdeSachbearbeiterId;
        entry.Bezeichnung      = dto.Bezeichnung?.Trim() ?? "Lohnpfändung";
        entry.Freigrenze       = Math.Round(dto.Freigrenze, 2);
        entry.Zielbetrag       = Math.Round(dto.Zielbetrag, 2);
        // BereitsAbgezogen NICHT überschreiben — nur im Confirm-Flow
        entry.ValidFrom        = DateOnly.Parse(dto.ValidFrom);
        entry.ValidTo          = string.IsNullOrWhiteSpace(dto.ValidTo) ? null : DateOnly.Parse(dto.ValidTo!);
        entry.ReferenzAmt           = dto.ReferenzAmt?.Trim();
        entry.ZahlungsReferenz      = dto.ZahlungsReferenz?.Trim();
        entry.Bemerkung             = dto.Bemerkung?.Trim();
        entry.LohnausweisAnBehoerde = dto.LohnausweisAnBehoerde;
        entry.UpdatedAt             = DateTime.Now;
        await _db.SaveChangesAsync();

        var reloaded = await _db.EmployeeLohnAssignments
            .Include(a => a.Behoerde)
            .Include(a => a.Sachbearbeiter)
            .FirstAsync(a => a.Id == entry.Id);
        return Ok(MapToDto(reloaded, firstAllowedU));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await _db.EmployeeLohnAssignments.FindAsync(id);
        if (entry == null) return NotFound();

        var branchIdD     = await GetEmployeeBranchAsync(entry.EmployeeId);
        var firstAllowedD = branchIdD.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchIdD.Value)
            : null;
        if (firstAllowedD.HasValue && entry.ValidFrom < firstAllowedD.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Diese Lohnabtretung (gültig ab {entry.ValidFrom:dd.MM.yyyy}) wurde bereits in einem Lohnlauf verwendet und kann nicht gelöscht werden.",
                firstAllowedDate = firstAllowedD?.ToString("yyyy-MM-dd")
            });
        }

        _db.EmployeeLohnAssignments.Remove(entry);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── Validation & Mapping ────────────────────────────────────────────
    private async Task<string?> ValidateAsync(LohnAssignmentDto dto)
    {
        if (dto.Freigrenze < 0)      return "Freigrenze muss ≥ 0 sein.";
        if (dto.Zielbetrag < 0)      return "Zielbetrag muss ≥ 0 sein.";
        if (!DateOnly.TryParse(dto.ValidFrom, out var from))
            return "Ungültiges 'Gültig ab'-Datum.";
        if (!string.IsNullOrWhiteSpace(dto.ValidTo))
        {
            if (!DateOnly.TryParse(dto.ValidTo, out var to))
                return "Ungültiges 'Gültig bis'-Datum.";
            if (to < from) return "'Gültig bis' muss ≥ 'Gültig ab' sein.";
        }
        var exists = await _db.Behoerden.AnyAsync(b => b.Id == dto.BehoerdeId);
        if (!exists) return "Unbekannte Behörde.";
        if (dto.BehoerdeSachbearbeiterId.HasValue)
        {
            var sbOk = await _db.BehoerdeSachbearbeiter.AnyAsync(s =>
                s.Id == dto.BehoerdeSachbearbeiterId.Value
                && s.BehoerdeId == dto.BehoerdeId
                && s.IsActive);
            if (!sbOk) return "Sachbearbeiter gehört nicht zu dieser Behörde oder ist inaktiv.";
        }
        return null;
    }

    private static object MapToDto(EmployeeLohnAssignment a, DateOnly? firstAllowed = null) => new
    {
        id               = a.Id,
        employeeId       = a.EmployeeId,
        behoerdeId       = a.BehoerdeId,
        behoerdeName     = a.Behoerde?.Name,
        behoerdeTyp      = a.Behoerde?.Typ,
        behoerdeSachbearbeiterId = a.BehoerdeSachbearbeiterId,
        sachbearbeiterName       = a.Sachbearbeiter?.Name,
        sachbearbeiterEmail      = a.Sachbearbeiter?.Email,
        bezeichnung      = a.Bezeichnung,
        freigrenze       = a.Freigrenze,
        zielbetrag       = a.Zielbetrag,
        bereitsAbgezogen = a.BereitsAbgezogen,
        restbetrag       = a.Zielbetrag > 0 ? Math.Max(0, a.Zielbetrag - a.BereitsAbgezogen) : (decimal?)null,
        validFrom        = a.ValidFrom.ToString("yyyy-MM-dd"),
        validTo          = a.ValidTo?.ToString("yyyy-MM-dd"),
        referenzAmt           = a.ReferenzAmt,
        zahlungsReferenz      = a.ZahlungsReferenz,
        bemerkung             = a.Bemerkung,
        lohnausweisAnBehoerde = a.LohnausweisAnBehoerde,
        createdAt             = a.CreatedAt,
        inLohnVerwendet       = firstAllowed.HasValue && a.ValidFrom < firstAllowed.Value
    };
}

public record LohnAssignmentDto(
    int     EmployeeId,
    int     BehoerdeId,
    string? Bezeichnung,
    decimal Freigrenze,
    decimal Zielbetrag,
    string  ValidFrom,
    string? ValidTo,
    string? ReferenzAmt,
    string? ZahlungsReferenz,
    string? Bemerkung,
    bool    LohnausweisAnBehoerde = false,
    int?    BehoerdeSachbearbeiterId = null
);

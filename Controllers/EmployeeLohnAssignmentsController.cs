using HrSystem.Data;
using HrSystem.Models;
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
    private readonly AppDbContext _db;
    public EmployeeLohnAssignmentsController(AppDbContext db) => _db = db;

    // GET /api/employee-lohn-assignments/{employeeId}
    [HttpGet("{employeeId:int}")]
    public async Task<IActionResult> GetByEmployee(int employeeId)
    {
        var list = await _db.EmployeeLohnAssignments
            .Include(a => a.Behoerde)
            .Where(a => a.EmployeeId == employeeId)
            .OrderBy(a => a.ValidFrom)
            .Select(a => MapToDto(a))
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LohnAssignmentDto dto)
    {
        var err = await ValidateAsync(dto);
        if (err != null) return BadRequest(err);

        var entry = new EmployeeLohnAssignment
        {
            EmployeeId       = dto.EmployeeId,
            BehoerdeId       = dto.BehoerdeId,
            Bezeichnung      = dto.Bezeichnung?.Trim() ?? "Lohnpfändung",
            Freigrenze       = Math.Round(dto.Freigrenze, 2),
            Zielbetrag       = Math.Round(dto.Zielbetrag, 2),
            BereitsAbgezogen = 0,
            ValidFrom        = DateOnly.Parse(dto.ValidFrom),
            ValidTo          = string.IsNullOrWhiteSpace(dto.ValidTo) ? null : DateOnly.Parse(dto.ValidTo!),
            ReferenzAmt      = dto.ReferenzAmt?.Trim(),
            ZahlungsReferenz = dto.ZahlungsReferenz?.Trim(),
            Bemerkung        = dto.Bemerkung?.Trim(),
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow
        };
        _db.EmployeeLohnAssignments.Add(entry);
        await _db.SaveChangesAsync();

        var saved = await _db.EmployeeLohnAssignments
            .Include(a => a.Behoerde)
            .FirstAsync(a => a.Id == entry.Id);
        return Ok(MapToDto(saved));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] LohnAssignmentDto dto)
    {
        var entry = await _db.EmployeeLohnAssignments.FindAsync(id);
        if (entry == null) return NotFound();

        var err = await ValidateAsync(dto);
        if (err != null) return BadRequest(err);

        entry.BehoerdeId       = dto.BehoerdeId;
        entry.Bezeichnung      = dto.Bezeichnung?.Trim() ?? "Lohnpfändung";
        entry.Freigrenze       = Math.Round(dto.Freigrenze, 2);
        entry.Zielbetrag       = Math.Round(dto.Zielbetrag, 2);
        // BereitsAbgezogen NICHT überschreiben — nur im Confirm-Flow
        entry.ValidFrom        = DateOnly.Parse(dto.ValidFrom);
        entry.ValidTo          = string.IsNullOrWhiteSpace(dto.ValidTo) ? null : DateOnly.Parse(dto.ValidTo!);
        entry.ReferenzAmt      = dto.ReferenzAmt?.Trim();
        entry.ZahlungsReferenz = dto.ZahlungsReferenz?.Trim();
        entry.Bemerkung        = dto.Bemerkung?.Trim();
        entry.UpdatedAt        = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var reloaded = await _db.EmployeeLohnAssignments
            .Include(a => a.Behoerde)
            .FirstAsync(a => a.Id == entry.Id);
        return Ok(MapToDto(reloaded));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await _db.EmployeeLohnAssignments.FindAsync(id);
        if (entry == null) return NotFound();
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
        return null;
    }

    private static object MapToDto(EmployeeLohnAssignment a) => new
    {
        id               = a.Id,
        employeeId       = a.EmployeeId,
        behoerdeId       = a.BehoerdeId,
        behoerdeName     = a.Behoerde?.Name,
        behoerdeTyp      = a.Behoerde?.Typ,
        bezeichnung      = a.Bezeichnung,
        freigrenze       = a.Freigrenze,
        zielbetrag       = a.Zielbetrag,
        bereitsAbgezogen = a.BereitsAbgezogen,
        restbetrag       = a.Zielbetrag > 0 ? Math.Max(0, a.Zielbetrag - a.BereitsAbgezogen) : (decimal?)null,
        validFrom        = a.ValidFrom.ToString("yyyy-MM-dd"),
        validTo          = a.ValidTo?.ToString("yyyy-MM-dd"),
        referenzAmt      = a.ReferenzAmt,
        zahlungsReferenz = a.ZahlungsReferenz,
        bemerkung        = a.Bemerkung,
        createdAt        = a.CreatedAt
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
    string? Bemerkung
);

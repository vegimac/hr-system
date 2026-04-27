using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Wiederkehrende Zulagen/Abzüge pro Mitarbeiter mit Gültigkeitszeitraum.
/// Single Source of Truth für Posten wie Fahrzeugzulage, Handy-Pauschale,
/// Parkplatz-Abzug — fliessen automatisch in jeden Lohnlauf ein, solange
/// die Periode innerhalb [ValidFrom, ValidTo] liegt.
/// </summary>
[Authorize]
[ApiController]
[Route("api/employee-recurring-wages")]
public class EmployeeRecurringWagesController : ControllerBase
{
    private readonly AppDbContext _db;
    public EmployeeRecurringWagesController(AppDbContext db) => _db = db;

    // GET /api/employee-recurring-wages/{employeeId}
    [HttpGet("{employeeId:int}")]
    public async Task<IActionResult> GetByEmployee(int employeeId)
    {
        var list = await _db.EmployeeRecurringWages
            .Include(r => r.Lohnposition)
            .Where(r => r.EmployeeId == employeeId)
            .OrderBy(r => r.ValidFrom)
            .Select(r => MapToDto(r))
            .ToListAsync();
        return Ok(list);
    }

    // POST /api/employee-recurring-wages
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RecurringWageDto dto)
    {
        var err = await ValidateAsync(dto);
        if (err != null) return BadRequest(err);

        var entry = new EmployeeRecurringWage
        {
            EmployeeId     = dto.EmployeeId,
            LohnpositionId = dto.LohnpositionId,
            Betrag         = Math.Round(dto.Betrag, 2),
            ValidFrom      = DateOnly.Parse(dto.ValidFrom),
            ValidTo        = string.IsNullOrWhiteSpace(dto.ValidTo) ? null : DateOnly.Parse(dto.ValidTo!),
            Bemerkung      = dto.Bemerkung?.Trim(),
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow
        };
        _db.EmployeeRecurringWages.Add(entry);
        await _db.SaveChangesAsync();

        // Reload mit Include um Lohnposition-Infos zurückzugeben
        var saved = await _db.EmployeeRecurringWages
            .Include(r => r.Lohnposition)
            .FirstAsync(r => r.Id == entry.Id);
        return Ok(MapToDto(saved));
    }

    // PUT /api/employee-recurring-wages/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RecurringWageDto dto)
    {
        var entry = await _db.EmployeeRecurringWages
            .Include(r => r.Lohnposition)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (entry == null) return NotFound();

        var err = await ValidateAsync(dto);
        if (err != null) return BadRequest(err);

        entry.LohnpositionId = dto.LohnpositionId;
        entry.Betrag         = Math.Round(dto.Betrag, 2);
        entry.ValidFrom      = DateOnly.Parse(dto.ValidFrom);
        entry.ValidTo        = string.IsNullOrWhiteSpace(dto.ValidTo) ? null : DateOnly.Parse(dto.ValidTo!);
        entry.Bemerkung      = dto.Bemerkung?.Trim();
        entry.UpdatedAt      = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // Include neu auflösen falls Lohnposition geändert wurde
        var reloaded = await _db.EmployeeRecurringWages
            .Include(r => r.Lohnposition)
            .FirstAsync(r => r.Id == entry.Id);
        return Ok(MapToDto(reloaded));
    }

    // DELETE /api/employee-recurring-wages/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await _db.EmployeeRecurringWages.FindAsync(id);
        if (entry == null) return NotFound();
        _db.EmployeeRecurringWages.Remove(entry);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── Validation & Mapping ────────────────────────────────────────────

    private async Task<string?> ValidateAsync(RecurringWageDto dto)
    {
        if (dto.Betrag <= 0) return "Betrag muss grösser als 0 sein.";
        if (!DateOnly.TryParse(dto.ValidFrom, out var from))
            return "Ungültiges 'Gültig ab'-Datum.";
        if (!string.IsNullOrWhiteSpace(dto.ValidTo))
        {
            if (!DateOnly.TryParse(dto.ValidTo, out var to))
                return "Ungültiges 'Gültig bis'-Datum.";
            if (to < from) return "'Gültig bis' muss grösser oder gleich 'Gültig ab' sein.";
        }
        var lp = await _db.Lohnpositionen.FindAsync(dto.LohnpositionId);
        if (lp == null) return "Unbekannte Lohnposition.";
        if (lp.Typ != "ZULAGE" && lp.Typ != "ABZUG")
            return "Lohnposition muss Typ ZULAGE oder ABZUG haben.";
        return null;
    }

    private static object MapToDto(EmployeeRecurringWage r) => new
    {
        id                      = r.Id,
        employeeId              = r.EmployeeId,
        lohnpositionId          = r.LohnpositionId,
        lohnpositionCode        = r.Lohnposition?.Code,
        lohnpositionBezeichnung = r.Lohnposition?.Bezeichnung,
        typ                     = r.Lohnposition?.Typ,
        betrag                  = r.Betrag,
        validFrom               = r.ValidFrom.ToString("yyyy-MM-dd"),
        validTo                 = r.ValidTo?.ToString("yyyy-MM-dd"),
        bemerkung               = r.Bemerkung,
        createdAt               = r.CreatedAt
    };
}

public record RecurringWageDto(
    int     EmployeeId,
    int     LohnpositionId,
    decimal Betrag,
    string  ValidFrom,
    string? ValidTo,
    string? Bemerkung
);

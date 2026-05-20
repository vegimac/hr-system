using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Wiederkehrende Zulagen/Abzüge pro Mitarbeiter mit Gültigkeitszeitraum.
/// Single Source of Truth für Posten wie Fahrzeugzulage, Handy-Pauschale,
/// Parkplatz-Abzug — fliessen automatisch in jeden Lohnlauf ein, solange
/// die Periode innerhalb [ValidFrom, ValidTo] liegt.
///
/// Walter-Vorgabe 17.05.2026: Sobald ein Eintrag in einem Lohnlauf verwendet
/// wurde (= ValidFrom liegt vor dem FirstAllowedDate), nicht mehr editieren
/// oder löschen — stattdessen einen neuen Eintrag ab dem nächsten freien
/// Datum anlegen.
/// </summary>
[Authorize]
[ApiController]
[Route("api/employee-recurring-wages")]
public class EmployeeRecurringWagesController : ControllerBase
{
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;
    public EmployeeRecurringWagesController(AppDbContext db, LohnEditLockService editLock)
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

    private static bool IsInLohnVerwendet(EmployeeRecurringWage r, DateOnly? firstAllowed)
        => firstAllowed.HasValue && r.ValidFrom < firstAllowed.Value;

    // GET /api/employee-recurring-wages/{employeeId}
    [HttpGet("{employeeId:int}")]
    public async Task<IActionResult> GetByEmployee(int employeeId)
    {
        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;

        var entries = await _db.EmployeeRecurringWages
            .Include(r => r.Lohnposition)
            .Where(r => r.EmployeeId == employeeId)
            .OrderBy(r => r.ValidFrom)
            .ToListAsync();
        return Ok(entries.Select(r => MapToDto(r, firstAllowed)).ToList());
    }

    // POST /api/employee-recurring-wages
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RecurringWageDto dto)
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

        var entry = new EmployeeRecurringWage
        {
            EmployeeId     = dto.EmployeeId,
            LohnpositionId = dto.LohnpositionId,
            Betrag         = Math.Round(dto.Betrag, 2),
            ValidFrom      = newFrom,
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
        return Ok(MapToDto(saved, firstAllowed));
    }

    // PUT /api/employee-recurring-wages/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RecurringWageDto dto)
    {
        var entry = await _db.EmployeeRecurringWages
            .Include(r => r.Lohnposition)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (entry == null) return NotFound();

        var branchId     = await GetEmployeeBranchAsync(entry.EmployeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;
        if (IsInLohnVerwendet(entry, firstAllowed))
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Dieser Eintrag (gültig ab {entry.ValidFrom:dd.MM.yyyy}) wurde bereits in einem Lohnlauf verwendet. Bitte einen neuen Eintrag ab frühestens {firstAllowed:dd.MM.yyyy} anlegen.",
                firstAllowedDate = firstAllowed?.ToString("yyyy-MM-dd")
            });
        }

        var err = await ValidateAsync(dto);
        if (err != null) return BadRequest(err);

        entry.LohnpositionId = dto.LohnpositionId;
        entry.Betrag         = Math.Round(dto.Betrag, 2);
        entry.ValidFrom      = DateOnly.Parse(dto.ValidFrom);
        entry.ValidTo        = string.IsNullOrWhiteSpace(dto.ValidTo) ? null : DateOnly.Parse(dto.ValidTo!);
        entry.Bemerkung      = dto.Bemerkung?.Trim();
        entry.UpdatedAt      = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var reloaded = await _db.EmployeeRecurringWages
            .Include(r => r.Lohnposition)
            .FirstAsync(r => r.Id == entry.Id);
        return Ok(MapToDto(reloaded, firstAllowed));
    }

    // DELETE /api/employee-recurring-wages/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await _db.EmployeeRecurringWages.FindAsync(id);
        if (entry == null) return NotFound();

        var branchId     = await GetEmployeeBranchAsync(entry.EmployeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;
        if (IsInLohnVerwendet(entry, firstAllowed))
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Dieser Eintrag (gültig ab {entry.ValidFrom:dd.MM.yyyy}) wurde bereits in einem Lohnlauf verwendet und kann nicht gelöscht werden.",
                firstAllowedDate = firstAllowed?.ToString("yyyy-MM-dd")
            });
        }

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

    private static object MapToDto(EmployeeRecurringWage r, DateOnly? firstAllowed = null) => new
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
        createdAt               = r.CreatedAt,
        inLohnVerwendet         = firstAllowed.HasValue && r.ValidFrom < firstAllowed.Value
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

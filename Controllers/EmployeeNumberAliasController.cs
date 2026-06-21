using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Alte/zweite Personalnummern eines MA (Walter-Vorgabe 21.06.2026). Ersetzt die
/// früheren Felder employee_number_alt1/alt2 durch eine eigene Tabelle. Reine
/// Identitäts-/Stammdaten (kein Lohn-Datum) → im Edit-Lock-Audit whitelisted.
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/number-aliases")]
[Authorize(Roles = "admin,superuser")]
public class EmployeeNumberAliasController : ControllerBase
{
    private readonly AppDbContext _db;
    public EmployeeNumberAliasController(AppDbContext db) => _db = db;

    public record NumberAliasDto(string Number, DateOnly? ValidFrom, DateOnly? ValidTo);

    /// <summary>Alle alten Nummern eines MA (neueste zuerst).</summary>
    [HttpGet]
    public async Task<IActionResult> List(int employeeId, CancellationToken ct)
    {
        var rows = await _db.EmployeeNumberAliases.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.ValidTo ?? DateOnly.MaxValue)
            .ThenByDescending(a => a.Id)
            .Select(a => new { a.Id, a.Number, a.ValidFrom, a.ValidTo, a.Source, a.CreatedAt })
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>Manuell eine alte Nummer hinzufügen.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, [FromBody] NumberAliasDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Number))
            return BadRequest(new { error = "NUMBER_REQUIRED", message = "Nummer darf nicht leer sein." });
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (emp == null) return NotFound(new { error = "EMPLOYEE_NOT_FOUND" });

        var num = dto.Number.Trim();
        // Keine Dublette + nicht die aktuelle Nummer selbst.
        if (string.Equals(emp.EmployeeNumber?.Trim(), num, StringComparison.OrdinalIgnoreCase))
            return Conflict(new { error = "IS_CURRENT_NUMBER", message = "Das ist die aktuelle Personalnummer." });
        var dup = await _db.EmployeeNumberAliases
            .AnyAsync(a => a.EmployeeId == employeeId && a.Number.ToLower() == num.ToLower(), ct);
        if (dup) return Conflict(new { error = "ALIAS_EXISTS", message = "Diese alte Nummer ist bereits hinterlegt." });

        _db.EmployeeNumberAliases.Add(new EmployeeNumberAlias
        {
            EmployeeId = employeeId,
            Number     = num,
            ValidFrom  = dto.ValidFrom,
            ValidTo    = dto.ValidTo,
            Source     = "manual",
            CreatedAt  = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    /// <summary>Eine alte Nummer löschen.</summary>
    [HttpDelete("{aliasId:int}")]
    public async Task<IActionResult> Delete(int employeeId, int aliasId, CancellationToken ct)
    {
        var row = await _db.EmployeeNumberAliases
            .FirstOrDefaultAsync(a => a.Id == aliasId && a.EmployeeId == employeeId, ct);
        if (row == null) return NotFound();
        _db.EmployeeNumberAliases.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

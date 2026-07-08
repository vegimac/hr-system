using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HrSystem.Controllers;

/// <summary>
/// CRUD für die versionierte Verfügbarkeit (verfügbare Arbeitszeiten) eines
/// Mitarbeiters — Walter-Vorgabe 07.07.2026 (Etappe 1). Die Verfügbarkeit ist
/// eine L-GAV-Anlage zum Arbeitsvertrag und ändert sich über die Zeit, ohne
/// dass sich der Vertrag ändert → deshalb am MA versioniert (Von/Bis-Fenster).
///
/// Kein LohnEditLockService: die Verfügbarkeit ist KEIN datum-basiertes
/// Lohn-Objekt (kein Betrag, keine Absenz, kein Snapshot-Einfluss) — reine
/// Planungsangabe. Deshalb im EditLock-Audit whitelisted.
/// </summary>
[ApiController]
[Route("api/employees/{empId:int}/availability")]
[Authorize(Roles = "admin,superuser,user")]
public class EmployeeAvailabilityController : ControllerBase
{
    private readonly AppDbContext _db;
    public EmployeeAvailabilityController(AppDbContext db) { _db = db; }

    public record SlotDto(
        TimeOnly? Von, TimeOnly? Bis,
        bool Mon, bool Tue, bool Wed, bool Thu, bool Fri, bool Sat, bool Sun,
        int SortOrder);

    public record UpsertDto(
        string Type, DateOnly ValidFrom, DateOnly? ValidTo, string? Bemerkung,
        List<SlotDto>? Slots);

    [HttpGet]
    public async Task<IActionResult> List(int empId)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var entries = await _db.EmployeeAvailabilities
            .AsNoTracking()
            .Where(a => a.EmployeeId == empId)
            .Include(a => a.Slots)
            .OrderByDescending(a => a.ValidFrom)
            .ThenByDescending(a => a.Id)
            .ToListAsync();

        var result = entries.Select(a => new
        {
            a.Id,
            a.EmployeeId,
            a.Type,
            a.ValidFrom,
            a.ValidTo,
            a.Bemerkung,
            a.CreatedAt,
            a.CreatedBy,
            isCurrent = a.ValidFrom <= today && (!a.ValidTo.HasValue || a.ValidTo.Value >= today),
            slots = a.Slots
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .Select(s => new
                {
                    s.Id,
                    s.Von,
                    s.Bis,
                    s.Mon, s.Tue, s.Wed, s.Thu, s.Fri, s.Sat, s.Sun,
                    s.SortOrder
                })
        });
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int empId, [FromBody] UpsertDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(new { error = "VALIDATION", message = err });

        var exists = await _db.Employees.AnyAsync(e => e.Id == empId);
        if (!exists) return NotFound(new { error = "NOT_FOUND", message = "Mitarbeiter nicht gefunden." });

        var entity = new EmployeeAvailability
        {
            EmployeeId = empId,
            Type       = dto.Type,
            ValidFrom  = dto.ValidFrom,
            ValidTo    = dto.ValidTo,
            Bemerkung  = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim(),
            CreatedAt  = DateTime.Now,
            CreatedBy  = GetCurrentUserId(),
            Slots      = BuildSlots(dto)
        };
        _db.EmployeeAvailabilities.Add(entity);
        await _db.SaveChangesAsync();
        return Ok(new { entity.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int empId, int id, [FromBody] UpsertDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(new { error = "VALIDATION", message = err });

        var entity = await _db.EmployeeAvailabilities
            .Include(a => a.Slots)
            .FirstOrDefaultAsync(a => a.Id == id && a.EmployeeId == empId);
        if (entity == null) return NotFound(new { error = "NOT_FOUND", message = "Verfügbarkeit nicht gefunden." });

        entity.Type      = dto.Type;
        entity.ValidFrom = dto.ValidFrom;
        entity.ValidTo   = dto.ValidTo;
        entity.Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();

        // Slots komplett ersetzen (Cascade räumt die alten weg).
        _db.EmployeeAvailabilitySlots.RemoveRange(entity.Slots);
        entity.Slots = BuildSlots(dto);

        await _db.SaveChangesAsync();
        return Ok(new { entity.Id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int empId, int id)
    {
        var entity = await _db.EmployeeAvailabilities
            .FirstOrDefaultAsync(a => a.Id == id && a.EmployeeId == empId);
        if (entity == null) return NotFound(new { error = "NOT_FOUND", message = "Verfügbarkeit nicht gefunden." });
        _db.EmployeeAvailabilities.Remove(entity);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    // ── Helfer ────────────────────────────────────────────────────────────
    private static string? Validate(UpsertDto dto)
    {
        if (dto.Type != "unrestricted" && dto.Type != "table")
            return "Typ muss «unrestricted» oder «table» sein.";
        if (dto.ValidFrom == default)
            return "Gültig-ab-Datum ist Pflicht.";
        if (dto.ValidTo.HasValue && dto.ValidTo.Value < dto.ValidFrom)
            return "Gültig-bis darf nicht vor Gültig-ab liegen.";
        return null;
    }

    private static List<EmployeeAvailabilitySlot> BuildSlots(UpsertDto dto)
    {
        // Bei «unrestricted» werden Slots ignoriert.
        if (dto.Type != "table" || dto.Slots == null) return new();
        var order = 0;
        return dto.Slots.Select(s => new EmployeeAvailabilitySlot
        {
            Von = s.Von,
            Bis = s.Bis,
            Mon = s.Mon, Tue = s.Tue, Wed = s.Wed, Thu = s.Thu,
            Fri = s.Fri, Sat = s.Sat, Sun = s.Sun,
            SortOrder = s.SortOrder != 0 ? s.SortOrder : order++
        }).ToList();
    }

    private int? GetCurrentUserId()
    {
        var s = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(s, out var id) ? id : null;
    }
}

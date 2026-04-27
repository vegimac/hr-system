using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/employees/{employeeId:int}/timeentries")]
public class EmployeeTimeEntriesController : ControllerBase
{
    private readonly AppDbContext _db;
    public EmployeeTimeEntriesController(AppDbContext db) => _db = db;

    // GET /api/employees/{employeeId}/timeentries?dateFrom=2026-02-21&dateTo=2026-03-20
    // GET /api/employees/{employeeId}/timeentries?year=2026&month=3  (calendar month fallback)
    [HttpGet]
    public async Task<IActionResult> GetAll(
        int employeeId,
        [FromQuery] string? dateFrom,
        [FromQuery] string? dateTo,
        [FromQuery] int? year,
        [FromQuery] int? month)
    {
        var query = _db.EmployeeTimeEntries
            .Where(t => t.EmployeeId == employeeId);

        DateOnly.TryParse(dateFrom, out var from);
        DateOnly.TryParse(dateTo,   out var to);
        var hasDateRange = !string.IsNullOrEmpty(dateFrom) && !string.IsNullOrEmpty(dateTo);

        if (hasDateRange)
        {
            query = query.Where(t => t.EntryDate >= from && t.EntryDate <= to);
        }
        else
        {
            if (year.HasValue)
                query = query.Where(t => t.EntryDate.Year == year.Value);
            if (month.HasValue)
                query = query.Where(t => t.EntryDate.Month == month.Value);
        }

        var entries = await query
            .OrderBy(t => t.EntryDate)
            .ThenBy(t => t.TimeIn)
            .ToListAsync();

        return Ok(entries);
    }

    // GET /api/employees/{employeeId}/timeentries/periods
    // Liefert alle Jahre/Monate, in denen für diesen MA Einträge existieren (neueste zuerst)
    [HttpGet("periods")]
    public async Task<IActionResult> GetPeriods(int employeeId)
    {
        var periods = await _db.EmployeeTimeEntries
            .Where(t => t.EmployeeId == employeeId)
            .GroupBy(t => new { t.EntryDate.Year, t.EntryDate.Month })
            .Select(g => new {
                year  = g.Key.Year,
                month = g.Key.Month,
                count = g.Count()
            })
            .OrderByDescending(x => x.year)
            .ThenByDescending(x => x.month)
            .ToListAsync();
        return Ok(periods);
    }

    // GET /api/employees/{employeeId}/timeentries/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int employeeId, int id)
    {
        var entry = await _db.EmployeeTimeEntries
            .FirstOrDefaultAsync(t => t.Id == id && t.EmployeeId == employeeId);
        return entry is null ? NotFound() : Ok(entry);
    }

    // POST /api/employees/{employeeId}/timeentries
    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, [FromBody] EmployeeTimeEntry dto)
    {
        dto.EmployeeId = employeeId;
        dto.Source     = "manual";
        dto.CreatedAt  = DateTime.UtcNow;
        dto.UpdatedAt  = DateTime.UtcNow;

        // Stempelzeiten werden als Lokalzeit gespeichert (timestamp ohne TZ).
        dto.TimeIn  = DateTime.SpecifyKind(dto.TimeIn, DateTimeKind.Unspecified);
        if (dto.TimeOut.HasValue)
            dto.TimeOut = DateTime.SpecifyKind(dto.TimeOut.Value, DateTimeKind.Unspecified);

        // Auto-calculate DurationHours if not supplied
        if (dto.DurationHours == null && dto.TimeOut.HasValue)
        {
            var span = dto.TimeOut.Value - dto.TimeIn;
            dto.DurationHours = (decimal)Math.Round(span.TotalHours, 2);
            dto.TotalHours    = dto.DurationHours + (dto.NightHours ?? 0);
        }

        _db.EmployeeTimeEntries.Add(dto);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { employeeId, id = dto.Id }, dto);
    }

    // PUT /api/employees/{employeeId}/timeentries/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int employeeId, int id, [FromBody] EmployeeTimeEntry dto)
    {
        var entry = await _db.EmployeeTimeEntries
            .FirstOrDefaultAsync(t => t.Id == id && t.EmployeeId == employeeId);
        if (entry is null) return NotFound();

        // Audit: Originalwerte beim ersten Bearbeiten sichern (Lokalzeit)
        if (entry.EditedBy is null)
        {
            entry.OriginalTimeIn  = DateTime.SpecifyKind(entry.TimeIn, DateTimeKind.Unspecified);
            entry.OriginalTimeOut = entry.TimeOut.HasValue
                ? DateTime.SpecifyKind(entry.TimeOut.Value, DateTimeKind.Unspecified)
                : (DateTime?)null;
            entry.OriginalComment = entry.Comment;
        }

        entry.EntryDate      = dto.EntryDate;
        entry.TimeIn         = DateTime.SpecifyKind(dto.TimeIn, DateTimeKind.Unspecified);
        entry.TimeOut        = dto.TimeOut.HasValue
            ? DateTime.SpecifyKind(dto.TimeOut.Value, DateTimeKind.Unspecified)
            : (DateTime?)null;
        entry.Comment        = dto.Comment;
        entry.NightHours     = dto.NightHours;
        entry.UpdatedAt      = DateTime.UtcNow;

        // Audit: wer hat wann geändert (Name aus JWT-Claim)
        entry.EditedBy = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Unbekannt";
        entry.EditedAt = DateTime.UtcNow;

        // Recalculate duration
        if (entry.TimeOut.HasValue)
        {
            var span = entry.TimeOut.Value - entry.TimeIn;
            entry.DurationHours = (decimal)Math.Round(span.TotalHours, 2);
            entry.TotalHours    = entry.DurationHours + (entry.NightHours ?? 0);
        }
        else
        {
            entry.DurationHours = dto.DurationHours;
            entry.TotalHours    = dto.TotalHours;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, inner = ex.InnerException?.Message });
        }

        // Navigation property ignorieren beim Zurückgeben
        return Ok(new {
            entry.Id, entry.EmployeeId, entry.EntryDate,
            entry.TimeIn, entry.TimeOut, entry.Comment,
            entry.DurationHours, entry.NightHours, entry.TotalHours,
            entry.Source, entry.CreatedAt, entry.UpdatedAt,
            entry.OriginalTimeIn, entry.OriginalTimeOut, entry.OriginalComment,
            entry.EditedBy, entry.EditedAt
        });
    }

    // DELETE /api/employees/{employeeId}/timeentries/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int employeeId, int id)
    {
        var entry = await _db.EmployeeTimeEntries
            .FirstOrDefaultAsync(t => t.Id == id && t.EmployeeId == employeeId);
        if (entry is null) return NotFound();

        _db.EmployeeTimeEntries.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

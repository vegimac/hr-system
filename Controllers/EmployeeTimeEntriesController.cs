using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Stempelzeiten: READ-ONLY in Cowork (Walter-Vorgabe 17.05.2026).
///
/// Die Quelle der Wahrheit ist easy@work — sämtliche Erfassung, Korrektur
/// und Löschung passiert dort. Cowork zeigt die importierten Stempelzeiten
/// nur an. Der Import-Pfad (siehe <c>ImportController.ImportStempelzeiten</c>
/// und <c>ImportMonatlich</c>) schreibt direkt über _db und ist davon
/// nicht betroffen — der ist admin/superuser-only und idempotent.
///
/// Konkrete Konsequenz: POST/PUT/DELETE auf diesem Controller liefern
/// HTTP 403 mit klarer Meldung. Auch admin/superuser sind blockiert —
/// für Korrekturen geht der Weg über easy@work + Re-Import.
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/timeentries")]
public class EmployeeTimeEntriesController : ControllerBase
{
    private readonly AppDbContext _db;
    public EmployeeTimeEntriesController(AppDbContext db)
    {
        _db = db;
    }

    private static IActionResult ReadOnlyResponse() => new ObjectResult(new
    {
        error   = "STEMPELZEITEN_READONLY",
        message = "Stempelzeiten werden in easy@work verwaltet — in Cowork nur Anzeige. " +
                  "Für Korrekturen bitte in easy@work erfassen und anschliessend Stempelzeiten neu importieren."
    })
    { StatusCode = StatusCodes.Status403Forbidden };

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
    // READ-ONLY: easy@work ist die Quelle der Wahrheit. Manuelle Erfassung
    // in Cowork ist nicht erlaubt — auch nicht für admin/superuser.
    [HttpPost]
    public IActionResult Create(int employeeId, [FromBody] EmployeeTimeEntry dto)
        => ReadOnlyResponse();

    // PUT /api/employees/{employeeId}/timeentries/{id} — siehe POST.
    [HttpPut("{id:int}")]
    public IActionResult Update(int employeeId, int id, [FromBody] EmployeeTimeEntry dto)
        => ReadOnlyResponse();

    // DELETE /api/employees/{employeeId}/timeentries/{id} — siehe POST.
    [HttpDelete("{id:int}")]
    public IActionResult Delete(int employeeId, int id)
        => ReadOnlyResponse();
}

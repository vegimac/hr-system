using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

// ============================================================================
// Warnungsverwaltung (Walter-Vorgabe 06.07.2026, Priorität/Farbe 19.07.2026).
// Globale Konfiguration der Dashboard-/ToDo-Warnungen — pro Kategorie:
// an/aus, Vorlauf (Tage), Eskalations-Schwelle (Tage), Schweregrad
// (Basis + eskaliert), ToDo-Priorität, Warnfarbe.
// GLOBAL, nicht pro Filiale.
//
// Lohn-Edit-Lock (LohnEditLockService): NICHT relevant — reine Katalog-/
// Anzeige-Konfiguration, keine MA-/Lohndaten. Im Audit-Test whitelisted.
// ============================================================================
[ApiController]
[Route("api/dashboard-warning-config")]
[Authorize(Roles = "admin")]
public class DashboardWarningConfigController : ControllerBase
{
    private readonly AppDbContext _db;
    public DashboardWarningConfigController(AppDbContext db)
    {
        _db = db;
    }

    private static readonly HashSet<string> ValidSeverities =
        new() { "critical", "warning", "info" };

    private static readonly HashSet<string> ValidWarnColors =
        new() { "none", "red", "red_overdue" };

    // GET /api/dashboard-warning-config → alle Zeilen nach todo_priority, dann sort_order
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var rows = await _db.DashboardWarningConfigs
            .AsNoTracking()
            .OrderBy(c => c.TodoPriority)
            .ThenBy(c => c.SortOrder)
            .ToListAsync();

        var result = rows.Select(c => new
        {
            c.Id,
            c.Category,
            c.Label,
            c.Enabled,
            c.WarnDays,
            c.EscalateDays,
            c.SeverityBase,
            c.SeverityEscalated,
            c.IsDateBased,
            c.SortOrder,
            c.TodoPriority,
            c.WarnColor
        });
        return Ok(result);
    }

    public class WarnConfigUpdateDto
    {
        public int Id { get; set; }
        public bool Enabled { get; set; }
        public int? WarnDays { get; set; }
        public int? EscalateDays { get; set; }
        public string SeverityBase { get; set; } = "warning";
        public string? SeverityEscalated { get; set; }
        public int TodoPriority { get; set; } = 100;
        public string WarnColor { get; set; } = "none";
    }

    // PUT /api/dashboard-warning-config → Bulk-Update (Liste von Zeilen)
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] List<WarnConfigUpdateDto> updates)
    {
        if (updates == null || updates.Count == 0)
            return BadRequest(new { error = "NO_ROWS", message = "Keine Zeilen übermittelt." });

        foreach (var u in updates)
        {
            if (!ValidSeverities.Contains(u.SeverityBase))
                return BadRequest(new {
                    error = "INVALID_SEVERITY",
                    message = $"Ungültiger Schweregrad «{u.SeverityBase}» (erlaubt: critical/warning/info)."
                });
            if (u.SeverityEscalated != null && !ValidSeverities.Contains(u.SeverityEscalated))
                return BadRequest(new {
                    error = "INVALID_SEVERITY",
                    message = $"Ungültiger eskalierter Schweregrad «{u.SeverityEscalated}» (erlaubt: critical/warning/info)."
                });
            var color = (u.WarnColor ?? "none").Trim().ToLowerInvariant();
            if (!ValidWarnColors.Contains(color))
                return BadRequest(new {
                    error = "INVALID_WARN_COLOR",
                    message = $"Ungültige Warnfarbe «{u.WarnColor}» (erlaubt: none/red/red_overdue)."
                });
            u.WarnColor = color;
            if (u.WarnDays.HasValue && u.WarnDays.Value < 0)
                return BadRequest(new {
                    error = "INVALID_DAYS",
                    message = "Vorlauf (Tage) darf nicht negativ sein."
                });
            if (u.EscalateDays.HasValue && u.EscalateDays.Value < 0)
                return BadRequest(new {
                    error = "INVALID_DAYS",
                    message = "Eskalations-Schwelle (Tage) darf nicht negativ sein."
                });
            if (u.TodoPriority < 0 || u.TodoPriority > 9999)
                return BadRequest(new {
                    error = "INVALID_PRIORITY",
                    message = "Priorität muss zwischen 0 und 9999 liegen (kleinere Zahl = weiter oben)."
                });
        }

        var ids = updates.Select(u => u.Id).ToList();
        var rows = await _db.DashboardWarningConfigs
            .Where(c => ids.Contains(c.Id))
            .ToListAsync();
        var byId = rows.ToDictionary(c => c.Id);

        foreach (var u in updates)
        {
            if (!byId.TryGetValue(u.Id, out var row)) continue;
            row.Enabled           = u.Enabled;
            row.WarnDays          = u.WarnDays;
            row.EscalateDays      = u.EscalateDays;
            row.SeverityBase      = u.SeverityBase;
            row.SeverityEscalated = u.SeverityEscalated;
            row.TodoPriority      = u.TodoPriority;
            row.WarnColor         = u.WarnColor;
        }

        await _db.SaveChangesAsync();
        return Ok(new { updated = rows.Count });
    }
}

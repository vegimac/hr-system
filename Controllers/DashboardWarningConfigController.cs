using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

// ============================================================================
// Warnungsverwaltung (Walter-Vorgabe 06.07.2026).
// Globale Konfiguration der Dashboard-/ToDo-Warnungen — pro Kategorie:
// an/aus, Vorlauf (Tage), Eskalations-Schwelle (Tage), Schweregrad
// (Basis + eskaliert). GLOBAL, nicht pro Filiale.
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

    // GET /api/dashboard-warning-config → alle Zeilen nach sort_order
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var rows = await _db.DashboardWarningConfigs
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
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
            c.SortOrder
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
    }

    // PUT /api/dashboard-warning-config → Bulk-Update (Liste von Zeilen)
    // Aktualisiert enabled/warn_days/escalate_days/severity_base/severity_escalated.
    // Kategorie/Label/is_date_based/sort_order bleiben unverändert (Katalog-Felder).
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] List<WarnConfigUpdateDto> updates)
    {
        if (updates == null || updates.Count == 0)
            return BadRequest(new { error = "NO_ROWS", message = "Keine Zeilen übermittelt." });

        // Validierung vorab (alles-oder-nichts).
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
        }

        await _db.SaveChangesAsync();
        return Ok(new { updated = rows.Count });
    }
}

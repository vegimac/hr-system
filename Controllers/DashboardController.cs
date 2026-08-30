using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Dashboard-Cockpit: liefert Alarme/Erinnerungen.
/// GET /api/dashboard?companyProfileId=X (optional, sonst alle)
/// </summary>
[Authorize]
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly DashboardService _svc;
    private readonly HrSystem.Data.AppDbContext _db;
    public DashboardController(DashboardService svc, HrSystem.Data.AppDbContext db)
    {
        _svc = svc;
        _db  = db;
    }

    /// <summary>
    /// GET /api/dashboard/anleitung — die «so behebst du es»-Texte pro
    /// Warnungskategorie (Walter-Vorgabe 30.08.2026).
    ///
    /// Bewusst nur die TEXTE: die Alerts hat das Frontend bereits geladen, es
    /// gruppiert sie nach Kategorie und setzt den Brief daraus zusammen. So
    /// wird der teure Dashboard-Aufbau nicht ein zweites Mal gerechnet, und es
    /// gibt keine zweite Wahrheit über den Regeln.
    /// </summary>
    [HttpGet("anleitung")]
    public async Task<IActionResult> GetAnleitung()
    {
        var rows = await _db.TodoAnleitungen.AsNoTracking()
            .OrderBy(a => a.SortOrder)
            .Select(a => new { a.Category, a.Titel, a.Anleitung, a.SortOrder })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? companyProfileId)
    {
        var data = await _svc.BuildAsync(companyProfileId);
        // Aktivitäts-Log ist admin-only — Stille-Warnung nicht an GF/HR zeigen.
        if (!User.IsInRole("admin"))
            data.Alerts.RemoveAll(a => a.Category == "audit_log_stumm");
        return Ok(data);
    }
}

using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Fehlerliste des MA-Stammdaten-Syncs (Walter-Vorgabe 01.09.2026).
///
/// Zeigt, welche Verträge der Nachtlauf NICHT importiert hat und warum —
/// im Klartext, mit Personalnummer und Filiale, damit es in easy@work
/// korrigiert werden kann. Vorher gab es dieses Protokoll nicht; blockierte
/// Verträge wären lautlos verschwunden.
///
/// Standardmässig nur der JÜNGSTE Lauf je Filiale und nur offene Punkte —
/// eine Liste, die alte, längst korrigierte Fälle mitschleppt, liest niemand.
///
/// Endpoints:
///   GET  /api/ma-sync-fehler            — offene Punkte (optional ?alle=true)
///   POST /api/ma-sync-fehler/{id}/erledigt — abhaken
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/ma-sync-fehler")]
public class MaSyncFehlerController : ControllerBase
{
    private readonly AppDbContext _db;

    public MaSyncFehlerController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] bool alle = false, CancellationToken ct = default)
    {
        var q = _db.EasyAtWorkMaSyncLogs.AsNoTracking().AsQueryable();
        if (!alle) q = q.Where(x => !x.Erledigt);

        // Nur der jüngste Lauf je Filiale: ältere Zeilen sind entweder erledigt
        // oder tauchen im neuen Lauf ohnehin wieder auf.
        var letzteLaeufe = await _db.EasyAtWorkMaSyncLogs.AsNoTracking()
            .GroupBy(x => x.CompanyProfileId)
            .Select(g => new { Cp = g.Key, RunAt = g.Max(x => x.RunAt) })
            .ToListAsync(ct);

        var rows = await q.OrderByDescending(x => x.RunAt).Take(500).ToListAsync(ct);
        var neueste = letzteLaeufe.ToDictionary(x => x.Cp, x => x.RunAt);
        rows = rows.Where(r => neueste.TryGetValue(r.CompanyProfileId, out var last)
                            && (r.RunAt - last).Duration() < TimeSpan.FromMinutes(30))
                   .ToList();

        var cpIds = rows.Select(r => r.CompanyProfileId).Distinct().ToList();
        var branches = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => cpIds.Contains(c.Id))
            .Select(c => new { c.Id, c.RestaurantCode, Name = c.BranchName ?? c.CompanyName })
            .ToDictionaryAsync(c => c.Id, ct);

        var result = rows.Select(r => new
        {
            id             = r.Id,
            runAt          = r.RunAt,
            companyProfileId = r.CompanyProfileId,
            filiale        = branches.TryGetValue(r.CompanyProfileId, out var b)
                                ? (string.IsNullOrWhiteSpace(b.RestaurantCode) ? b.Name : $"{b.RestaurantCode} – {b.Name}")
                                : $"Filiale {r.CompanyProfileId}",
            employeeNumber = r.EmployeeNumber,
            employeeId     = r.EmployeeId,
            kind           = r.Kind,
            reason         = r.Reason,
            erledigt       = r.Erledigt,
        });

        return Ok(new { anzahl = rows.Count, zeilen = result });
    }

    [HttpPost("{id:int}/erledigt")]
    public async Task<IActionResult> Erledigt(int id, CancellationToken ct)
    {
        var row = await _db.EasyAtWorkMaSyncLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { error = "NICHT_GEFUNDEN" });

        row.Erledigt          = true;
        row.ErledigtAm        = DateTime.Now;
        row.ErledigtVonUserId = GetCurrentUserId();
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    private int? GetCurrentUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var uid) ? uid : null;
    }
}

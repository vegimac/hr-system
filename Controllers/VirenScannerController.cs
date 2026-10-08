using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>Virenscanner: Status, Funde, Bestands-Scan (Walter-Vorgabe 08.10.2026, nur admin).</summary>
[ApiController]
[Route("api/viren-scanner")]
[Authorize(Roles = "admin")]
public class VirenScannerController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly VirenScanner _scanner;
    private readonly VirenBestandScan _bestand;

    public VirenScannerController(AppDbContext db, VirenScanner scanner, VirenBestandScan bestand)
    {
        _db = db;
        _scanner = scanner;
        _bestand = bestand;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var (stand, version) = _scanner.Signaturen();
        return Ok(new
        {
            pflicht = _scanner.Pflicht,
            erreichbar = _scanner.Pflicht && await _scanner.PingAsync(ct),
            signaturenStand = stand,
            signaturenVersion = version,
            offeneFunde = await _db.VirenFunde.CountAsync(f => !f.Erledigt, ct),
            bestand = _bestand.Stand(),
        });
    }

    [HttpGet("funde")]
    public async Task<IActionResult> Funde([FromQuery] bool alle = false, CancellationToken ct = default)
    {
        var q = _db.VirenFunde.AsNoTracking();
        if (!alle) q = q.Where(f => !f.Erledigt);
        var liste = await q.OrderByDescending(f => f.GefundenAm).Take(200)
            .Select(f => new
            {
                f.Id, f.GefundenAm, f.Quelle, f.Dateiname, f.Virus, f.Ort, f.Benutzer,
                f.EmployeeId, f.Erledigt, f.ErledigtAm,
                maName = f.EmployeeId == null ? null
                    : _db.Employees.Where(e => e.Id == f.EmployeeId).Select(e => e.FirstName + " " + e.LastName).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return Ok(liste);
    }

    [HttpPost("funde/{id:int}/erledigt")]
    public async Task<IActionResult> Erledigt(int id, CancellationToken ct)
    {
        var f = await _db.VirenFunde.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f == null) return NotFound();
        f.Erledigt = true;
        f.ErledigtAm = DateTime.Now;
        f.ErledigtVonUserId = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null;
        await _db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpPost("bestand-scan")]
    public IActionResult BestandScan()
    {
        if (!_scanner.Pflicht)
            return Conflict(new { error = "KEIN_SCANNER", message = "Auf diesem Rechner läuft kein Virenscanner (nur auf dem Server)." });
        if (!_bestand.Starten(User.Identity?.Name))
            return Conflict(new { error = "LAEUFT_SCHON", message = "Der Durchlauf läuft bereits." });
        return Ok(_bestand.Stand());
    }
}

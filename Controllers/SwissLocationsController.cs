using HrSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Lookup-Endpoints für Schweizer PLZ/Gemeinden/Kanton-Stammdaten
/// (Quelle: Amtliches Ortschaftenverzeichnis der Schweizerischen Post).
///
/// Wird vom Mitarbeiter-Stamm verwendet: User gibt PLZ ein → Gemeinde
/// und Kanton werden vorgeschlagen. Bei PLZ mit mehreren Gemeinden
/// zeigt das Frontend eine Auswahl.
/// </summary>
[ApiController]
[Route("api/swiss-locations")]
[Authorize]
public class SwissLocationsController : ControllerBase
{
    private readonly AppDbContext _db;
    public SwissLocationsController(AppDbContext db) => _db = db;

    // GET /api/swiss-locations/by-plz?plz=8580
    //   → [{ plz4, gemeindename, bfsNr, kantonskuerzel }, …]
    //
    // Liefert alle Gemeinden zu einer PLZ, sortiert alphabetisch nach
    // Gemeindename. Leere Liste wenn PLZ unbekannt.
    [HttpGet("by-plz")]
    public async Task<IActionResult> GetByPlz([FromQuery] string plz)
    {
        if (string.IsNullOrWhiteSpace(plz))
            return BadRequest(new { error = "plz ist erforderlich." });

        var plzTrim = plz.Trim();
        var list = await _db.SwissLocations
            .Where(l => l.Plz4 == plzTrim)
            .OrderBy(l => l.Gemeindename)
            .Select(l => new {
                plz4           = l.Plz4,
                gemeindename   = l.Gemeindename,
                bfsNr          = l.BfsNr,
                kantonskuerzel = l.Kantonskuerzel
            })
            .ToListAsync();

        return Ok(list);
    }

    // GET /api/swiss-locations/count — nur für Admin-/Debug-Zwecke
    [HttpGet("count")]
    public async Task<IActionResult> Count()
    {
        var c = await _db.SwissLocations.CountAsync();
        return Ok(new { count = c });
    }
}

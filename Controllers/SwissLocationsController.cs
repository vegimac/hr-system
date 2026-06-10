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

    // GET /api/swiss-locations/cantons-by-plz?plzs=4900,6260,8580,…
    //   → { "4900": "BE", "6260": "LU", "8580": "TG" }
    //
    // Bulk-Variante: nimmt eine kommaseparierte PLZ-Liste entgegen, liefert
    // pro EINDEUTIG zuordbarer PLZ den Kantons-Code. Mehrdeutige PLZ (über
    // Kantonsgrenze) sowie unbekannte PLZ erscheinen NICHT in der Antwort —
    // konsistent mit EnrichAddressFromZipAsync, das auch nur bei Eindeutigkeit
    // setzt. Walter-Vorgabe 06.06.2026: vom CSV-Importer benutzt, um die in
    // easy@work hinterlegten Kantonsangaben gegen den amtlichen Lookup
    // gegenzuprüfen und Diskrepanzen sichtbar zu machen.
    [HttpGet("cantons-by-plz")]
    public async Task<IActionResult> CantonsByPlz([FromQuery] string plzs)
    {
        if (string.IsNullOrWhiteSpace(plzs))
            return Ok(new Dictionary<string, string>());

        var plzList = plzs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length >= 3)
            .Distinct()
            .Take(500)        // Schutz vor Missbrauch — Importer braucht selten >100
            .ToList();

        if (plzList.Count == 0)
            return Ok(new Dictionary<string, string>());

        // Pro PLZ alle DISTINCT Kantone holen — wenn genau 1 → in Result.
        var rows = await _db.SwissLocations
            .Where(l => plzList.Contains(l.Plz4))
            .Select(l => new { l.Plz4, l.Kantonskuerzel })
            .Distinct()
            .ToListAsync();

        var result = rows
            .GroupBy(r => r.Plz4)
            .Where(g => g.Count() == 1 && !string.IsNullOrWhiteSpace(g.First().Kantonskuerzel))
            .ToDictionary(g => g.Key, g => g.First().Kantonskuerzel);

        return Ok(result);
    }

    // GET /api/swiss-locations/count — nur für Admin-/Debug-Zwecke
    [HttpGet("count")]
    public async Task<IActionResult> Count()
    {
        var c = await _db.SwissLocations.CountAsync();
        return Ok(new { count = c });
    }

    // ════════════════════════════════════════════════════════════════════
    // ADMIN-PFLEGE (Walter-Vorgabe 07.06.2026)
    // Such-API + CRUD. Backend-Suche, weil 4'000+ PLZ-Einträge clientseitig
    // unhandlich wären. Limit 100/Anfrage damit die Tabelle responsive bleibt.
    // ════════════════════════════════════════════════════════════════════
    public class SwissLocationUpsertDto
    {
        public string Plz4           { get; set; } = "";
        public string Gemeindename   { get; set; } = "";
        public int    BfsNr          { get; set; }
        public string Kantonskuerzel { get; set; } = "";
    }

    [HttpGet("admin")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> SearchAdmin([FromQuery] string? q = null)
    {
        var query = _db.SwissLocations.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var qt = q.Trim();
            var qLower = qt.ToLower();
            query = query.Where(l =>
                l.Plz4 == qt
             || l.Plz4.StartsWith(qt)
             || l.Gemeindename.ToLower().Contains(qLower)
             || l.Kantonskuerzel.ToLower() == qLower);
        }
        // Walter-Vorgabe 07.06.2026: Ohne Suche ALLE Einträge zurück (rund
        // 4'000) — Frontend macht Sortierung/Filter clientseitig. Mit Suche
        // bleibt das Limit von 200 als Sicherheitsnetz.
        var hasQuery = !string.IsNullOrWhiteSpace(q);
        var ordered = query.OrderBy(l => l.Plz4).ThenBy(l => l.Gemeindename);
        var listQuery = hasQuery ? ordered.Take(200) : (IQueryable<Models.SwissLocation>)ordered;
        var list = await listQuery
            .Select(l => new {
                id             = l.Id,
                plz4           = l.Plz4,
                gemeindename   = l.Gemeindename,
                bfsNr          = l.BfsNr,
                kantonskuerzel = l.Kantonskuerzel
            })
            .ToListAsync();
        var total = await query.CountAsync();
        return Ok(new { items = list, total, capped = hasQuery && total > 200 });
    }

    [HttpPost("admin")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Create([FromBody] SwissLocationUpsertDto dto)
    {
        if (!Validate(dto, out var err)) return BadRequest(new { error = err });
        // Duplikate verhindern (PLZ + BFS-Gemeindenummer eindeutig).
        var exists = await _db.SwissLocations
            .AnyAsync(l => l.Plz4 == dto.Plz4.Trim() && l.BfsNr == dto.BfsNr);
        if (exists) return Conflict(new { error = $"PLZ {dto.Plz4} mit BFS-Nr {dto.BfsNr} existiert bereits." });

        var entry = new Models.SwissLocation
        {
            Plz4           = dto.Plz4.Trim(),
            Gemeindename   = dto.Gemeindename.Trim(),
            BfsNr          = dto.BfsNr,
            Kantonskuerzel = dto.Kantonskuerzel.Trim().ToUpperInvariant()
        };
        _db.SwissLocations.Add(entry);
        await _db.SaveChangesAsync();
        return Ok(new {
            id = entry.Id, plz4 = entry.Plz4, gemeindename = entry.Gemeindename,
            bfsNr = entry.BfsNr, kantonskuerzel = entry.Kantonskuerzel
        });
    }

    [HttpPut("admin/{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Update(int id, [FromBody] SwissLocationUpsertDto dto)
    {
        if (!Validate(dto, out var err)) return BadRequest(new { error = err });
        var entry = await _db.SwissLocations.FirstOrDefaultAsync(l => l.Id == id);
        if (entry == null) return NotFound();
        // Duplikat-Schutz beim Ändern (gleiche PLZ + BFS, aber andere ID).
        var dup = await _db.SwissLocations
            .AnyAsync(l => l.Plz4 == dto.Plz4.Trim() && l.BfsNr == dto.BfsNr && l.Id != id);
        if (dup) return Conflict(new { error = $"Eine andere Zeile mit PLZ {dto.Plz4} und BFS-Nr {dto.BfsNr} existiert bereits." });

        entry.Plz4           = dto.Plz4.Trim();
        entry.Gemeindename   = dto.Gemeindename.Trim();
        entry.BfsNr          = dto.BfsNr;
        entry.Kantonskuerzel = dto.Kantonskuerzel.Trim().ToUpperInvariant();
        await _db.SaveChangesAsync();
        return Ok(new {
            id = entry.Id, plz4 = entry.Plz4, gemeindename = entry.Gemeindename,
            bfsNr = entry.BfsNr, kantonskuerzel = entry.Kantonskuerzel
        });
    }

    [HttpDelete("admin/{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await _db.SwissLocations.FirstOrDefaultAsync(l => l.Id == id);
        if (entry == null) return NotFound();
        _db.SwissLocations.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static bool Validate(SwissLocationUpsertDto dto, out string err)
    {
        if (string.IsNullOrWhiteSpace(dto.Plz4) || dto.Plz4.Trim().Length < 4)
        { err = "PLZ muss mindestens 4 Zeichen haben."; return false; }
        if (string.IsNullOrWhiteSpace(dto.Gemeindename))
        { err = "Gemeindename ist Pflicht."; return false; }
        if (dto.BfsNr <= 0)
        { err = "BFS-Gemeindenummer muss > 0 sein."; return false; }
        if (string.IsNullOrWhiteSpace(dto.Kantonskuerzel) || dto.Kantonskuerzel.Trim().Length != 2)
        { err = "Kantons-Kürzel muss genau 2 Zeichen haben (z.B. ZH, BE, AG)."; return false; }
        err = "";
        return true;
    }
}

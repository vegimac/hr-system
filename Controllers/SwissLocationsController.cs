using HrSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Lookup-Endpoints für Schweizer PLZ/Ortschaft/Kanton-Stammdaten
/// (Quelle: Amtliches Ortschaftenverzeichnis swisstopo / AMTOVZ).
///
/// Adress-Ort = Ortschaftsname (z.B. «Bützberg»), nicht die politische
/// Gemeinde («Thunstetten») — Walter 29.07.2026.
/// </summary>
[ApiController]
[Route("api/swiss-locations")]
[Authorize]
public class SwissLocationsController : ControllerBase
{
    private readonly AppDbContext _db;
    public SwissLocationsController(AppDbContext db) => _db = db;

    // GET /api/swiss-locations/by-plz?plz=4922
    //   → [{ plz4, ortschaftsname, gemeindename, bfsNr, kantonskuerzel }, …]
    //
    // Liefert alle Ortschaften zu einer PLZ, sortiert alphabetisch.
    // Frontend füllt das Ort-Feld mit ortschaftsname.
    [HttpGet("by-plz")]
    public async Task<IActionResult> GetByPlz([FromQuery] string plz)
    {
        if (string.IsNullOrWhiteSpace(plz))
            return BadRequest(new { error = "plz ist erforderlich." });

        var plzTrim = plz.Trim();
        var list = await _db.SwissLocations
            .Where(l => l.Plz4 == plzTrim)
            .OrderBy(l => l.Ortschaftsname)
            .Select(l => new {
                plz4             = l.Plz4,
                ortschaftsname   = l.Ortschaftsname,
                // Backward-compat: ältere Clients lasen «gemeindename» als Ort.
                // Ab 29.07.2026 ist der Ort die Ortschaft — deshalb hier spiegeln.
                gemeindename     = l.Ortschaftsname,
                politischeGemeinde = l.Gemeindename,
                bfsNr            = l.BfsNr,
                kantonskuerzel   = l.Kantonskuerzel
            })
            .ToListAsync();

        return Ok(list);
    }

    // GET /api/swiss-locations/cantons-by-plz?plzs=4900,6260,8580,…
    [HttpGet("cantons-by-plz")]
    public async Task<IActionResult> CantonsByPlz([FromQuery] string plzs)
    {
        if (string.IsNullOrWhiteSpace(plzs))
            return Ok(new Dictionary<string, string>());

        var plzList = plzs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length >= 3)
            .Distinct()
            .Take(500)
            .ToList();

        if (plzList.Count == 0)
            return Ok(new Dictionary<string, string>());

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

    [HttpGet("count")]
    public async Task<IActionResult> Count()
    {
        var c = await _db.SwissLocations.CountAsync();
        return Ok(new { count = c });
    }

    public class SwissLocationUpsertDto
    {
        public string Plz4             { get; set; } = "";
        public string Ortschaftsname   { get; set; } = "";
        public string Gemeindename     { get; set; } = "";
        public int    BfsNr            { get; set; }
        public string Kantonskuerzel   { get; set; } = "";
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
             || l.Ortschaftsname.ToLower().Contains(qLower)
             || l.Gemeindename.ToLower().Contains(qLower)
             || l.Kantonskuerzel.ToLower() == qLower);
        }
        var hasQuery = !string.IsNullOrWhiteSpace(q);
        var ordered = query.OrderBy(l => l.Plz4).ThenBy(l => l.Ortschaftsname);
        var listQuery = hasQuery ? ordered.Take(200) : (IQueryable<Models.SwissLocation>)ordered;
        var list = await listQuery
            .Select(l => new {
                id             = l.Id,
                plz4           = l.Plz4,
                ortschaftsname = l.Ortschaftsname,
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
        var ort = ResolveOrtschaft(dto);
        var exists = await _db.SwissLocations
            .AnyAsync(l => l.Plz4 == dto.Plz4.Trim() && l.Ortschaftsname == ort);
        if (exists) return Conflict(new { error = $"PLZ {dto.Plz4} mit Ortschaft «{ort}» existiert bereits." });

        var entry = new Models.SwissLocation
        {
            Plz4           = dto.Plz4.Trim(),
            Ortschaftsname = ort,
            Gemeindename   = string.IsNullOrWhiteSpace(dto.Gemeindename) ? ort : dto.Gemeindename.Trim(),
            BfsNr          = dto.BfsNr,
            Kantonskuerzel = dto.Kantonskuerzel.Trim().ToUpperInvariant()
        };
        _db.SwissLocations.Add(entry);
        await _db.SaveChangesAsync();
        return Ok(ToAdminDto(entry));
    }

    [HttpPut("admin/{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Update(int id, [FromBody] SwissLocationUpsertDto dto)
    {
        if (!Validate(dto, out var err)) return BadRequest(new { error = err });
        var entry = await _db.SwissLocations.FirstOrDefaultAsync(l => l.Id == id);
        if (entry == null) return NotFound();
        var ort = ResolveOrtschaft(dto);
        var dup = await _db.SwissLocations
            .AnyAsync(l => l.Plz4 == dto.Plz4.Trim() && l.Ortschaftsname == ort && l.Id != id);
        if (dup) return Conflict(new { error = $"Eine andere Zeile mit PLZ {dto.Plz4} und Ortschaft «{ort}» existiert bereits." });

        entry.Plz4           = dto.Plz4.Trim();
        entry.Ortschaftsname = ort;
        entry.Gemeindename   = string.IsNullOrWhiteSpace(dto.Gemeindename) ? ort : dto.Gemeindename.Trim();
        entry.BfsNr          = dto.BfsNr;
        entry.Kantonskuerzel = dto.Kantonskuerzel.Trim().ToUpperInvariant();
        await _db.SaveChangesAsync();
        return Ok(ToAdminDto(entry));
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

    private static object ToAdminDto(Models.SwissLocation entry) => new {
        id = entry.Id, plz4 = entry.Plz4, ortschaftsname = entry.Ortschaftsname,
        gemeindename = entry.Gemeindename, bfsNr = entry.BfsNr, kantonskuerzel = entry.Kantonskuerzel
    };

    private static string ResolveOrtschaft(SwissLocationUpsertDto dto)
        => (string.IsNullOrWhiteSpace(dto.Ortschaftsname) ? dto.Gemeindename : dto.Ortschaftsname).Trim();

    private static bool Validate(SwissLocationUpsertDto dto, out string err)
    {
        if (string.IsNullOrWhiteSpace(dto.Plz4) || dto.Plz4.Trim().Length < 4)
        { err = "PLZ muss mindestens 4 Zeichen haben."; return false; }
        if (string.IsNullOrWhiteSpace(dto.Ortschaftsname) && string.IsNullOrWhiteSpace(dto.Gemeindename))
        { err = "Ortschaftsname ist Pflicht."; return false; }
        if (dto.BfsNr <= 0)
        { err = "BFS-Gemeindenummer muss > 0 sein."; return false; }
        if (string.IsNullOrWhiteSpace(dto.Kantonskuerzel) || dto.Kantonskuerzel.Trim().Length != 2)
        { err = "Kantons-Kürzel muss genau 2 Zeichen haben (z.B. ZH, BE, AG)."; return false; }
        err = "";
        return true;
    }
}

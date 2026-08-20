using HrSystem.Data;
using HrSystem.Services;
using HrSystem.Services.EasyAtWork;
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
        var raw = await _db.SwissLocations
            .Where(l => l.Plz4 == plzTrim)
            .OrderBy(l => l.Ortschaftsname)
            .Select(l => new {
                l.Plz4,
                l.Ortschaftsname,
                l.Gemeindename,
                l.BfsNr,
                l.Kantonskuerzel
            })
            .ToListAsync();

        // Walter 29.07.2026: Adress-Ort ohne Kantons-Suffix («Roggwil BE» → «Roggwil»).
        // Rohname bleibt in der Admin-Tabelle ( /admin ).
        var list = raw.Select(l => {
            var ort = EasyAtWorkEmployeeSyncService.StripCityCantonSuffix(l.Ortschaftsname)
                      ?? l.Ortschaftsname;
            return new {
                plz4               = l.Plz4,
                ortschaftsname     = ort,
                // Backward-compat: ältere Clients lasen «gemeindename» als Ort.
                gemeindename       = ort,
                politischeGemeinde = l.Gemeindename,
                bfsNr              = l.BfsNr,
                kantonskuerzel     = l.Kantonskuerzel
            };
        }).ToList();

        return Ok(list);
    }

    // GET /api/swiss-locations/by-name?q=reid — Orts-VORWÄRTSSUCHE
    // (Walter 20.08.2026): wer die PLZ nicht kennt, tippt den Ortsnamen an
    // («Reid» → Reiden LU, Reidermoos LU, …); die Auswahl füllt PLZ + Ort +
    // Kanton. Treffer, die mit der Eingabe BEGINNEN, zuerst. Max. 25.
    [HttpGet("by-name")]
    public async Task<IActionResult> GetByName([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(Array.Empty<object>());
        var qLower = q.Trim().ToLower();
        var raw = await _db.SwissLocations
            .Where(l => l.Ortschaftsname.ToLower().Contains(qLower))
            .OrderBy(l => l.Ortschaftsname.ToLower().StartsWith(qLower) ? 0 : 1)
            .ThenBy(l => l.Ortschaftsname)
            .ThenBy(l => l.Plz4)
            .Take(25)
            .Select(l => new { l.Plz4, l.Ortschaftsname, l.Kantonskuerzel })
            .ToListAsync();
        var list = raw.Select(l => new {
            plz4           = l.Plz4,
            ortschaftsname = EasyAtWorkEmployeeSyncService.StripCityCantonSuffix(l.Ortschaftsname)
                             ?? l.Ortschaftsname,
            kantonskuerzel = l.Kantonskuerzel
        }).ToList();
        return Ok(list);
    }

    // POST /api/swiss-locations/learn — unbekannte PLZ lernen (Walter 06.08.2026).
    // Sonder-PLZ (Postfach-Adressen wie «5001 Aarau SPS») stehen nicht im
    // amtlichen Ortschaftsverzeichnis. Trägt der User Ort (+ Kanton) von Hand
    // ein, merken wir uns das — der nächste Lookup findet die PLZ dann überall.
    // Bewusst NUR wenn die PLZ komplett unbekannt ist (kein Überschreiben/
    // Verwässern amtlicher Einträge durch Tippvarianten).
    [HttpPost("learn")]
    public async Task<IActionResult> Learn([FromBody] SwissLocationLearnDto dto)
    {
        var plz = (dto.Plz ?? "").Trim();
        var ort = (dto.Ort ?? "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(plz, @"^\d{4}$") || ort.Length < 2)
            return BadRequest(new { error = "PLZ_ODER_ORT_UNGUELTIG" });

        var exists = await _db.SwissLocations.AnyAsync(l => l.Plz4 == plz);
        if (exists) return Ok(new { learned = false, reason = "PLZ_BEKANNT" });

        _db.SwissLocations.Add(new Models.SwissLocation
        {
            Plz4           = plz,
            Ortschaftsname = ort,
            Gemeindename   = ort,
            BfsNr          = 0,
            Kantonskuerzel = (dto.Kanton ?? "").Trim().ToUpperInvariant(),
        });
        await _db.SaveChangesAsync();
        return Ok(new { learned = true });
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

    /// <summary>
    /// Walter 29.07.2026: AMTOVZ-CSV komplett neu laden (Truncate + Insert).
    /// Repariert u.a. den alten Fehler «Thörigen unter PLZ 3360».
    /// Body optional: { "force": true } — sonst nur wenn IsStale.
    /// </summary>
    [HttpPost("admin/reimport")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Reimport([FromBody] ReimportDto? dto, CancellationToken ct)
    {
        var force = dto?.Force == true;
        var contentRoot = HttpContext.RequestServices
            .GetRequiredService<IWebHostEnvironment>().ContentRootPath;

        if (!force && !await SwissLocationReimportService.IsStaleAsync(_db, ct))
        {
            var n = await _db.SwissLocations.CountAsync(ct);
            return Ok(new {
                reimported = false,
                count = n,
                message = $"Verzeichnis ist bereits aktuell ({n} Ortschaften)."
            });
        }

        try
        {
            var (count, path) = await SwissLocationReimportService.ReimportAsync(_db, contentRoot, ct);
            return Ok(new {
                reimported = true,
                count,
                csvPath = path,
                message = $"Neu geladen: {count} Ortschaften aus AMTOVZ."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new {
                error = "REIMPORT_FAILED",
                message = ex.Message
            });
        }
    }

    public class ReimportDto
    {
        public bool Force { get; set; }
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

public class SwissLocationLearnDto
{
    public string? Plz { get; set; }
    public string? Ort { get; set; }
    public string? Kanton { get; set; }
}

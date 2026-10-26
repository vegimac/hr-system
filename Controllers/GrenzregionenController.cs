using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Grenzregionen der ESTV. Neue Zeilen nur über den CSV-Import oder den
/// Start-Import. Die Schweizer Gemeinden bleiben in swiss_location.
/// </summary>
[ApiController]
[Route("api/grenzregionen")]
[Authorize]
public class GrenzregionenController : ControllerBase
{
    private readonly AppDbContext _db;
    public GrenzregionenController(AppDbContext db) => _db = db;

    [HttpGet]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> List([FromQuery] string? q = null)
    {
        var query = _db.Grenzregionen.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var qt = q.Trim();
            var lower = qt.ToLower();
            query = query.Where(g =>
                g.Plz == qt
                || g.Plz.StartsWith(qt)
                || g.Ort.ToLower().Contains(lower)
                || g.Region.ToLower().Contains(lower)
                || g.LandIso.ToLower() == lower);
        }
        var items = await query
            .OrderBy(g => g.LandIso).ThenBy(g => g.Plz).ThenBy(g => g.Ort)
            .Select(g => new { g.Id, g.Plz, g.Ort, g.Region, land = g.LandIso })
            .ToListAsync();
        return Ok(new { items, total = items.Count });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await _db.Grenzregionen.FirstOrDefaultAsync(g => g.Id == id);
        if (row == null) return NotFound();
        _db.Grenzregionen.Remove(row);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>CSV plz;ort;region;land — vorhandene gleiche Zeile wird aktualisiert.</summary>
    [HttpPost("import")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Import([FromBody] GrenzregionImportDto dto)
    {
        var text = dto.Csv ?? "";
        var zeilen = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var neu = 0;
        var aktualisiert = 0;
        var fehler = new List<string>();
        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var vorhanden = await _db.Grenzregionen.ToListAsync();
        var index = vorhanden.ToDictionary(g => Schlussel(g.LandIso, g.Plz, g.Ort), g => g);

        for (var i = 0; i < zeilen.Length; i++)
        {
            var roh = zeilen[i].Trim();
            if (roh.Length == 0) continue;
            var teile = roh.Split(roh.Contains(';') ? ';' : ',');
            if (i == 0 && teile[0].Trim().Equals("plz", StringComparison.OrdinalIgnoreCase))
                continue;
            if (teile.Length < 4)
            {
                fehler.Add($"Zeile {i + 1}: PLZ, Ort, Region und Land (ISO) erwartet.");
                continue;
            }
            var eingabe = new GrenzregionEingabe
            {
                Plz = teile[0],
                Ort = teile[1],
                Region = teile[2],
                Land = teile[3],
            };
            if (!Lesen(eingabe, out var zeile, out var err))
            {
                fehler.Add($"Zeile {i + 1}: {err}");
                continue;
            }
            var key = Schlussel(zeile.LandIso, zeile.Plz, zeile.Ort);
            if (!gesehen.Add(key)) continue;
            if (index.TryGetValue(key, out var alt))
            {
                if (alt.Region != zeile.Region)
                {
                    alt.Region = zeile.Region;
                    aktualisiert++;
                }
            }
            else
            {
                _db.Grenzregionen.Add(zeile);
                index[key] = zeile;
                neu++;
            }
        }
        await _db.SaveChangesAsync();
        return Ok(new { neu, aktualisiert, fehler });
    }

    private static string Schlussel(string land, string plz, string ort)
        => land.Trim().ToUpperInvariant() + "|" + plz.Trim() + "|" + ort.Trim().ToLowerInvariant();

    private static bool Lesen(GrenzregionEingabe dto, out Grenzregion zeile, out string err)
    {
        zeile = new Grenzregion();
        err = "";
        var plz = (dto.Plz ?? "").Trim();
        var ort = (dto.Ort ?? "").Trim();
        var region = (dto.Region ?? "").Trim();
        var land = (dto.Land ?? "").Trim().ToUpperInvariant();
        if (plz.Length < 3 || plz.Length > 10 || plz.Any(c => !char.IsDigit(c)))
        {
            err = "PLZ: 3 bis 10 Ziffern.";
            return false;
        }
        if (ort.Length < 2)
        {
            err = "Ort fehlt.";
            return false;
        }
        if (land.Length != 2 || land.Any(c => c < 'A' || c > 'Z'))
        {
            err = "Land muss ein ISO-Code mit 2 Buchstaben sein, z.B. IT.";
            return false;
        }
        if (land == "CH")
        {
            err = "Schweizer Gemeinden gehören in PLZ / Gemeinden, nicht in die Grenzregion.";
            return false;
        }
        zeile.Plz = plz;
        zeile.Ort = ort;
        zeile.Region = region;
        zeile.LandIso = land;
        return true;
    }

    public class GrenzregionEingabe
    {
        public string Plz { get; set; } = "";
        public string Ort { get; set; } = "";
        public string Region { get; set; } = "";
        public string Land { get; set; } = "";
    }

    public class GrenzregionImportDto
    {
        public string Csv { get; set; } = "";
    }
}

using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Ärzte-Verzeichnis (Walter-Vorgabe 16.07.2026): CRUD für behandelnde
/// Ärztinnen/Ärzte — Katalogdaten, kein Lohnbezug. Verwendet im
/// Mutterschafts-Modul («Brief an den behandelnden Arzt»).
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/aerzte")]
public class AerzteController : ControllerBase
{
    private readonly AppDbContext _db;
    public AerzteController(AppDbContext db) => _db = db;

    public class ArztDto
    {
        public string? Titel { get; set; }
        public string Vorname { get; set; } = "";
        public string Nachname { get; set; } = "";
        public string? Fachgebiet { get; set; }
        public string? PraxisName { get; set; }
        public string? Strasse { get; set; }
        public string? Plz { get; set; }
        public string? Ort { get; set; }
        public string? Telefon { get; set; }
        public string? Email { get; set; }
        public string? Bemerkung { get; set; }
        public bool? Aktiv { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool all = false)
    {
        var q = _db.Aerzte.AsNoTracking();
        if (!all) q = q.Where(a => a.Aktiv);
        var list = await q
            .OrderBy(a => a.Nachname).ThenBy(a => a.Vorname)
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ArztDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Nachname))
            return BadRequest(new { error = "NACHNAME_FEHLT", message = "Bitte mindestens den Nachnamen erfassen." });
        var a = new Arzt
        {
            Titel      = Clean(dto.Titel),
            Vorname    = (dto.Vorname ?? "").Trim(),
            Nachname   = dto.Nachname.Trim(),
            Fachgebiet = Clean(dto.Fachgebiet),
            PraxisName = Clean(dto.PraxisName),
            Strasse    = Clean(dto.Strasse),
            Plz        = Clean(dto.Plz),
            Ort        = Clean(dto.Ort),
            Telefon    = Clean(dto.Telefon),
            Email      = Clean(dto.Email),
            Bemerkung  = Clean(dto.Bemerkung),
            Aktiv      = dto.Aktiv ?? true
        };
        _db.Aerzte.Add(a);
        await _db.SaveChangesAsync();
        return Ok(a);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ArztDto dto)
    {
        var a = await _db.Aerzte.FindAsync(id);
        if (a == null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Nachname))
            return BadRequest(new { error = "NACHNAME_FEHLT", message = "Bitte mindestens den Nachnamen erfassen." });
        a.Titel      = Clean(dto.Titel);
        a.Vorname    = (dto.Vorname ?? "").Trim();
        a.Nachname   = dto.Nachname.Trim();
        a.Fachgebiet = Clean(dto.Fachgebiet);
        a.PraxisName = Clean(dto.PraxisName);
        a.Strasse    = Clean(dto.Strasse);
        a.Plz        = Clean(dto.Plz);
        a.Ort        = Clean(dto.Ort);
        a.Telefon    = Clean(dto.Telefon);
        a.Email      = Clean(dto.Email);
        a.Bemerkung  = Clean(dto.Bemerkung);
        if (dto.Aktiv.HasValue) a.Aktiv = dto.Aktiv.Value;
        await _db.SaveChangesAsync();
        return Ok(a);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var a = await _db.Aerzte.FindAsync(id);
        if (a == null) return NotFound();
        _db.Aerzte.Remove(a);
        await _db.SaveChangesAsync();
        return Ok();
    }

    private static string? Clean(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

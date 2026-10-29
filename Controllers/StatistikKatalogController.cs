using System.Text.RegularExpressions;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Katalog Ausbildung und berufliche Stellung für die BFS-Statistik.
/// Lesen: HR. Schreiben: nur admin. Die Zeilen sind der Endstand der Maske,
/// nicht eine Sonderliste für die Zertifizierung.
/// </summary>
[ApiController]
[Route("api/statistik-katalog")]
[Authorize(Roles = "admin,superuser")]
public class StatistikKatalogController : ControllerBase
{
    private static readonly Regex CodeMuster = new("^[A-Za-z][A-Za-z0-9]{0,39}$", RegexOptions.Compiled);

    private readonly AppDbContext _db;

    public StatistikKatalogController(AppDbContext db) => _db = db;

    [HttpGet("ausbildung")]
    public async Task<IActionResult> Ausbildung()
        => Ok(await _db.StatistikAusbildungen.AsNoTracking()
            .OrderBy(a => a.Sortierung).ThenBy(a => a.Id)
            .Select(a => new
            {
                a.Id, a.Code, a.Bezeichnung, a.Beschreibung, a.Sortierung,
                a.LseAusbildung, a.LseTitel, a.Aktiv,
            }).ToListAsync());

    [HttpGet("stellung")]
    public async Task<IActionResult> Stellung()
        => Ok(await _db.StatistikStellungen.AsNoTracking()
            .OrderBy(s => s.Sortierung).ThenBy(s => s.Id)
            .Select(s => new
            {
                s.Id, s.Code, s.Bezeichnung, s.Beschreibung, s.Sortierung,
                s.LseCode, s.Aktiv,
            }).ToListAsync());

    [HttpPut("ausbildung/{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> AusbildungSpeichern(int id, [FromBody] AusbildungDto dto)
    {
        var row = await _db.StatistikAusbildungen.FirstOrDefaultAsync(a => a.Id == id);
        if (row == null) return NotFound();
        var fehler = Pruefe(dto.Code, dto.Bezeichnung, dto.LseAusbildung, 8, dto.LseTitel, 3);
        if (fehler != null) return fehler;
        if (await _db.StatistikAusbildungen.AnyAsync(a => a.Id != id && a.Code == dto.Code!.Trim()))
            return BadRequest(new { error = "CODE_BELEGT", message = "Diesen XML-Wert gibt es schon." });
        row.Code = dto.Code!.Trim();
        row.Bezeichnung = dto.Bezeichnung!.Trim();
        row.Beschreibung = Leer(dto.Beschreibung);
        row.Sortierung = dto.Sortierung;
        row.LseAusbildung = dto.LseAusbildung;
        row.LseTitel = dto.LseTitel;
        row.Aktiv = dto.Aktiv;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPut("stellung/{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> StellungSpeichern(int id, [FromBody] StellungDto dto)
    {
        var row = await _db.StatistikStellungen.FirstOrDefaultAsync(s => s.Id == id);
        if (row == null) return NotFound();
        var fehler = Pruefe(dto.Code, dto.Bezeichnung, dto.LseCode, 5, null, 0);
        if (fehler != null) return fehler;
        if (await _db.StatistikStellungen.AnyAsync(s => s.Id != id && s.Code == dto.Code!.Trim()))
            return BadRequest(new { error = "CODE_BELEGT", message = "Diesen XML-Wert gibt es schon." });
        row.Code = dto.Code!.Trim();
        row.Bezeichnung = dto.Bezeichnung!.Trim();
        row.Beschreibung = Leer(dto.Beschreibung);
        row.Sortierung = dto.Sortierung;
        row.LseCode = dto.LseCode;
        row.Aktiv = dto.Aktiv;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private IActionResult? Pruefe(string? code, string? bezeichnung, int? lse, int lseMax, int? titel, int titelMax)
    {
        var c = (code ?? "").Trim();
        if (!CodeMuster.IsMatch(c))
            return BadRequest(new { error = "CODE_UNGUELTIG", message = "Der XML-Wert muss mit einem Buchstaben beginnen und darf nur Buchstaben und Ziffern enthalten." });
        if (string.IsNullOrWhiteSpace(bezeichnung) || bezeichnung.Trim().Length > 200)
            return BadRequest(new { error = "BEZEICHNUNG_UNGUELTIG", message = "Die Bezeichnung fehlt oder ist länger als 200 Zeichen." });
        if (lse is < 1 || lse > lseMax)
            return BadRequest(new { error = "LSE_UNGUELTIG", message = $"Die LSE-Zahl muss zwischen 1 und {lseMax} liegen oder leer sein." });
        if (titelMax > 0 && (titel is < 1 || titel > titelMax))
            return BadRequest(new { error = "TITEL_UNGUELTIG", message = "Der Hochschultitel ist 1, 2, 3 oder leer." });
        return null;
    }

    private static string? Leer(string? text)
        => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public class AusbildungDto
    {
        public string? Code { get; set; }
        public string? Bezeichnung { get; set; }
        public string? Beschreibung { get; set; }
        public int Sortierung { get; set; }
        public int? LseAusbildung { get; set; }
        public int? LseTitel { get; set; }
        public bool Aktiv { get; set; } = true;
    }

    public class StellungDto
    {
        public string? Code { get; set; }
        public string? Bezeichnung { get; set; }
        public string? Beschreibung { get; set; }
        public int Sortierung { get; set; }
        public int? LseCode { get; set; }
        public bool Aktiv { get; set; } = true;
    }
}

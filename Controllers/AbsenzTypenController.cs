using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[Authorize]
[ApiController]
[Route("api/absenz-typen")]
public class AbsenzTypenController : ControllerBase
{
    private readonly AppDbContext _db;
    public AbsenzTypenController(AppDbContext db) => _db = db;

    /// <summary>Alle aktiven Typen (für Dropdown in Modal)</summary>
    [HttpGet]
    public async Task<IActionResult> GetAktive()
    {
        var list = await _db.AbsenzTypen
            .Where(t => t.Aktiv)
            .OrderBy(t => t.SortOrder)
            .Select(t => new { t.Id, t.Code, t.Bezeichnung, t.Zeitgutschrift, t.GutschriftModus, t.SortOrder })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>Alle Typen inkl. inaktiver (für Admin)</summary>
    [HttpGet("all")]
    public async Task<IActionResult> GetAlle()
    {
        var list = await _db.AbsenzTypen
            .OrderBy(t => t.SortOrder)
            .Select(t => new { t.Id, t.Code, t.Bezeichnung, t.Zeitgutschrift, t.GutschriftModus, t.SortOrder, t.Aktiv })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>Typ aktualisieren (Code + Bezeichnung + Zeitgutschrift + Modus)</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] AbsenzTypDto dto)
    {
        var typ = await _db.AbsenzTypen.FindAsync(id);
        if (typ is null) return NotFound();

        // Code-Eindeutigkeit prüfen (falls geändert)
        if (!string.Equals(typ.Code, dto.Code, StringComparison.OrdinalIgnoreCase))
        {
            bool exists = await _db.AbsenzTypen.AnyAsync(t => t.Id != id && t.Code == dto.Code.ToUpper());
            if (exists) return BadRequest("Ein Typ mit diesem Code existiert bereits.");
        }

        typ.Code             = dto.Code.ToUpper().Trim();
        typ.Bezeichnung      = dto.Bezeichnung.Trim();
        typ.Zeitgutschrift   = dto.Zeitgutschrift;
        typ.GutschriftModus  = dto.Zeitgutschrift ? dto.GutschriftModus : null;
        typ.SortOrder        = dto.SortOrder;
        typ.Aktiv            = dto.Aktiv;

        await _db.SaveChangesAsync();
        return Ok(new { typ.Id, typ.Code, typ.Bezeichnung, typ.Zeitgutschrift, typ.GutschriftModus, typ.SortOrder, typ.Aktiv });
    }

    /// <summary>Neuen Typ anlegen (für zukünftige Erweiterungen)</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AbsenzTypDto dto)
    {
        var code = dto.Code.ToUpper().Trim();
        bool exists = await _db.AbsenzTypen.AnyAsync(t => t.Code == code);
        if (exists) return BadRequest("Ein Typ mit diesem Code existiert bereits.");

        var typ = new AbsenzTyp
        {
            Code            = code,
            Bezeichnung     = dto.Bezeichnung.Trim(),
            Zeitgutschrift  = dto.Zeitgutschrift,
            GutschriftModus = dto.Zeitgutschrift ? dto.GutschriftModus : null,
            SortOrder       = dto.SortOrder,
            Aktiv           = true,
            CreatedAt       = DateTime.UtcNow
        };
        _db.AbsenzTypen.Add(typ);
        await _db.SaveChangesAsync();
        return Ok(new { typ.Id, typ.Code, typ.Bezeichnung, typ.Zeitgutschrift, typ.GutschriftModus, typ.SortOrder, typ.Aktiv });
    }
}

public record AbsenzTypDto(
    string  Code,
    string  Bezeichnung,
    bool    Zeitgutschrift,
    string? GutschriftModus,
    int     SortOrder,
    bool    Aktiv = true
);

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
            .Select(t => new {
                t.Id, t.Code, t.Bezeichnung, t.Zeitgutschrift, t.GutschriftModus,
                t.UtpAuszahlung, t.VerlaengertProbezeit, t.ReduziertSaldo, t.BasisStunden, t.SortOrder, t.ZwischenverdienstKuerzel
            })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>Alle Typen inkl. inaktiver (für Admin)</summary>
    [HttpGet("all")]
    public async Task<IActionResult> GetAlle()
    {
        var list = await _db.AbsenzTypen
            .OrderBy(t => t.SortOrder)
            .Select(t => new {
                t.Id, t.Code, t.Bezeichnung, t.Zeitgutschrift, t.GutschriftModus,
                t.UtpAuszahlung, t.VerlaengertProbezeit, t.ReduziertSaldo, t.BasisStunden, t.SortOrder, t.Aktiv, t.ZwischenverdienstKuerzel
            })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>Typ aktualisieren</summary>
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

        var err = ValidateFlags(dto);
        if (err != null) return BadRequest(err);

        typ.Code             = dto.Code.ToUpper().Trim();
        typ.Bezeichnung      = dto.Bezeichnung.Trim();
        typ.Zeitgutschrift   = dto.Zeitgutschrift;
        // Walter-Vorgabe 27.06.2026: Modus (1/5 vs 1/7) auch OHNE Zeitgutschrift
        // speichern — er steuert auch Lohn-Kürzungen ohne Gutschrift
        // (z.B. unbezahlter Urlaub: 1/7).
        typ.GutschriftModus  = dto.GutschriftModus;
        typ.UtpAuszahlung    = dto.UtpAuszahlung;
        typ.VerlaengertProbezeit = dto.VerlaengertProbezeit;
        typ.ReduziertSaldo   = string.IsNullOrWhiteSpace(dto.ReduziertSaldo) ? null : dto.ReduziertSaldo;
        typ.BasisStunden     = string.IsNullOrWhiteSpace(dto.BasisStunden)   ? "BETRIEB" : dto.BasisStunden;
        typ.SortOrder        = dto.SortOrder;
        typ.Aktiv            = dto.Aktiv;
        typ.ZwischenverdienstKuerzel = string.IsNullOrWhiteSpace(dto.ZwischenverdienstKuerzel)
            ? null
            : dto.ZwischenverdienstKuerzel.ToUpper().Trim();

        await _db.SaveChangesAsync();
        return Ok(new {
            typ.Id, typ.Code, typ.Bezeichnung, typ.Zeitgutschrift, typ.GutschriftModus,
            typ.UtpAuszahlung, typ.VerlaengertProbezeit, typ.ReduziertSaldo, typ.BasisStunden, typ.SortOrder, typ.Aktiv, typ.ZwischenverdienstKuerzel
        });
    }

    /// <summary>Neuen Typ anlegen</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AbsenzTypDto dto)
    {
        var code = dto.Code.ToUpper().Trim();
        bool exists = await _db.AbsenzTypen.AnyAsync(t => t.Code == code);
        if (exists) return BadRequest("Ein Typ mit diesem Code existiert bereits.");

        var err = ValidateFlags(dto);
        if (err != null) return BadRequest(err);

        var typ = new AbsenzTyp
        {
            Code            = code,
            Bezeichnung     = dto.Bezeichnung.Trim(),
            Zeitgutschrift  = dto.Zeitgutschrift,
            GutschriftModus = dto.GutschriftModus,
            UtpAuszahlung   = dto.UtpAuszahlung,
            VerlaengertProbezeit = dto.VerlaengertProbezeit,
            ReduziertSaldo  = string.IsNullOrWhiteSpace(dto.ReduziertSaldo) ? null : dto.ReduziertSaldo,
            BasisStunden    = string.IsNullOrWhiteSpace(dto.BasisStunden)   ? "BETRIEB" : dto.BasisStunden,
            SortOrder       = dto.SortOrder,
            Aktiv           = true,
            CreatedAt       = DateTime.Now,
            ZwischenverdienstKuerzel = string.IsNullOrWhiteSpace(dto.ZwischenverdienstKuerzel)
                ? null
                : dto.ZwischenverdienstKuerzel.ToUpper().Trim()
        };
        _db.AbsenzTypen.Add(typ);
        await _db.SaveChangesAsync();
        return Ok(new {
            typ.Id, typ.Code, typ.Bezeichnung, typ.Zeitgutschrift, typ.GutschriftModus,
            typ.UtpAuszahlung, typ.VerlaengertProbezeit, typ.ReduziertSaldo, typ.BasisStunden, typ.SortOrder, typ.Aktiv, typ.ZwischenverdienstKuerzel
        });
    }

    private static string? ValidateFlags(AbsenzTypDto dto)
    {
        if (dto.ReduziertSaldo != null
            && dto.ReduziertSaldo != ""
            && dto.ReduziertSaldo != "NACHT_STUNDEN"
            && dto.ReduziertSaldo != "FERIEN_TAGE")
            return "ReduziertSaldo: erlaubt sind NACHT_STUNDEN, FERIEN_TAGE oder leer.";
        if (dto.BasisStunden != null
            && dto.BasisStunden != ""
            && dto.BasisStunden != "BETRIEB"
            && dto.BasisStunden != "VERTRAG")
            return "BasisStunden: erlaubt sind BETRIEB oder VERTRAG.";
        return null;
    }
}

public record AbsenzTypDto(
    string  Code,
    string  Bezeichnung,
    bool    Zeitgutschrift,
    string? GutschriftModus,
    int     SortOrder,
    bool    Aktiv                    = true,
    bool    UtpAuszahlung            = false,
    string? ReduziertSaldo           = null,
    string? BasisStunden             = "BETRIEB",
    string? ZwischenverdienstKuerzel = null,
    bool    VerlaengertProbezeit     = false
);

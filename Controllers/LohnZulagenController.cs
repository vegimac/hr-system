using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[Authorize]
[ApiController]
[Route("api")]
public class LohnZulagenController : ControllerBase
{
    private readonly AppDbContext _db;
    public LohnZulagenController(AppDbContext db) => _db = db;

    // ═══════════════════════════════════════════════════════
    //  TYPEN  (Stammdaten)
    // ═══════════════════════════════════════════════════════

    /// <summary>Alle aktiven Typen (für Dropdown-Listen)</summary>
    [HttpGet("lohn-zulag-typen")]
    public async Task<IActionResult> GetTypen()
    {
        var list = await _db.LohnZulagTypen
            .Where(t => t.Aktiv)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Bezeichnung)
            .Select(t => new
            {
                t.Id, t.Bezeichnung, t.Typ, t.SvPflichtig, t.QstPflichtig, t.SortOrder, t.Aktiv
            })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>Alle Typen inkl. inaktiver (für Admin-Verwaltung)</summary>
    [HttpGet("lohn-zulag-typen/all")]
    public async Task<IActionResult> GetTypenAll()
    {
        var list = await _db.LohnZulagTypen
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Bezeichnung)
            .Select(t => new
            {
                t.Id, t.Bezeichnung, t.Typ, t.SvPflichtig, t.QstPflichtig, t.SortOrder, t.Aktiv
            })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>Neuen Typ anlegen</summary>
    [HttpPost("lohn-zulag-typen")]
    public async Task<IActionResult> CreateTyp([FromBody] LohnZulagTypDto dto)
    {
        var typ = new LohnZulagTyp
        {
            Bezeichnung  = dto.Bezeichnung.Trim(),
            Typ          = dto.Typ == "ABZUG" ? "ABZUG" : "ZULAGE",
            SvPflichtig  = dto.SvPflichtig,
            QstPflichtig = dto.QstPflichtig,
            SortOrder    = dto.SortOrder,
            Aktiv        = true,
            CreatedAt    = DateTime.UtcNow
        };
        _db.LohnZulagTypen.Add(typ);
        await _db.SaveChangesAsync();
        return Ok(new { typ.Id, typ.Bezeichnung, typ.Typ, typ.SvPflichtig, typ.QstPflichtig, typ.SortOrder, typ.Aktiv });
    }

    /// <summary>Typ bearbeiten</summary>
    [HttpPut("lohn-zulag-typen/{id}")]
    public async Task<IActionResult> UpdateTyp(int id, [FromBody] LohnZulagTypDto dto)
    {
        var typ = await _db.LohnZulagTypen.FindAsync(id);
        if (typ is null) return NotFound();

        typ.Bezeichnung  = dto.Bezeichnung.Trim();
        typ.Typ          = dto.Typ == "ABZUG" ? "ABZUG" : "ZULAGE";
        typ.SvPflichtig  = dto.SvPflichtig;
        typ.QstPflichtig = dto.QstPflichtig;
        typ.SortOrder    = dto.SortOrder;
        typ.Aktiv        = dto.Aktiv;

        await _db.SaveChangesAsync();
        return Ok(new { typ.Id, typ.Bezeichnung, typ.Typ, typ.SvPflichtig, typ.QstPflichtig, typ.SortOrder, typ.Aktiv });
    }

    /// <summary>Typ (de)aktivieren</summary>
    [HttpDelete("lohn-zulag-typen/{id}")]
    public async Task<IActionResult> DeleteTyp(int id)
    {
        var typ = await _db.LohnZulagTypen.FindAsync(id);
        if (typ is null) return NotFound();

        // Weich-Löschen: deaktivieren statt physisch löschen
        typ.Aktiv = false;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ═══════════════════════════════════════════════════════
    //  EINTRÄGE  (pro Mitarbeiter + Periode)
    // ═══════════════════════════════════════════════════════

    /// <summary>Alle Einträge eines Mitarbeiters für eine Periode (YYYY-MM)</summary>
    [HttpGet("lohn-zulagen/{employeeId}/{periode}")]
    public async Task<IActionResult> GetZulagen(int employeeId, string periode)
    {
        var list = await _db.LohnZulagen
            .Include(z => z.Typ)
            .Where(z => z.EmployeeId == employeeId && z.Periode == periode)
            .OrderBy(z => z.Typ!.SortOrder)
            .ThenBy(z => z.CreatedAt)
            .Select(z => new
            {
                z.Id,
                z.EmployeeId,
                z.Periode,
                z.TypId,
                TypBezeichnung  = z.Typ!.Bezeichnung,
                TypTyp          = z.Typ.Typ,
                SvPflichtig     = z.Typ.SvPflichtig,
                QstPflichtig    = z.Typ.QstPflichtig,
                z.Betrag,
                z.Bemerkung,
                z.CreatedAt
            })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>Neuen Eintrag erfassen</summary>
    [HttpPost("lohn-zulagen")]
    public async Task<IActionResult> CreateZulage([FromBody] LohnZulageDto dto)
    {
        // Periode-Format validieren
        if (dto.Periode.Length != 7 || dto.Periode[4] != '-')
            return BadRequest("Periode muss im Format YYYY-MM sein.");
        if (dto.Betrag <= 0)
            return BadRequest("Betrag muss grösser als 0 sein.");

        var typ = await _db.LohnZulagTypen.FindAsync(dto.TypId);
        if (typ is null) return BadRequest("Unbekannter Typ.");

        var entry = new LohnZulage
        {
            EmployeeId = dto.EmployeeId,
            Periode    = dto.Periode,
            TypId      = dto.TypId,
            Betrag     = Math.Round(dto.Betrag, 2),
            Bemerkung  = dto.Bemerkung?.Trim(),
            CreatedAt  = DateTime.UtcNow,
            UpdatedAt  = DateTime.UtcNow
        };
        _db.LohnZulagen.Add(entry);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            entry.Id,
            entry.EmployeeId,
            entry.Periode,
            entry.TypId,
            TypBezeichnung = typ.Bezeichnung,
            TypTyp         = typ.Typ,
            SvPflichtig    = typ.SvPflichtig,
            QstPflichtig   = typ.QstPflichtig,
            entry.Betrag,
            entry.Bemerkung,
            entry.CreatedAt
        });
    }

    /// <summary>Eintrag aktualisieren (Betrag / Bemerkung)</summary>
    [HttpPut("lohn-zulagen/{id}")]
    public async Task<IActionResult> UpdateZulage(int id, [FromBody] LohnZulageUpdateDto dto)
    {
        var entry = await _db.LohnZulagen.Include(z => z.Typ).FirstOrDefaultAsync(z => z.Id == id);
        if (entry is null) return NotFound();

        if (dto.Betrag <= 0) return BadRequest("Betrag muss grösser als 0 sein.");

        entry.Betrag    = Math.Round(dto.Betrag, 2);
        entry.Bemerkung = dto.Bemerkung?.Trim();
        entry.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new
        {
            entry.Id, entry.EmployeeId, entry.Periode, entry.TypId,
            TypBezeichnung = entry.Typ!.Bezeichnung,
            TypTyp         = entry.Typ.Typ,
            SvPflichtig    = entry.Typ.SvPflichtig,
            QstPflichtig   = entry.Typ.QstPflichtig,
            entry.Betrag, entry.Bemerkung, entry.CreatedAt
        });
    }

    /// <summary>Eintrag löschen</summary>
    [HttpDelete("lohn-zulagen/{id}")]
    public async Task<IActionResult> DeleteZulage(int id)
    {
        var entry = await _db.LohnZulagen.FindAsync(id);
        if (entry is null) return NotFound();
        _db.LohnZulagen.Remove(entry);
        await _db.SaveChangesAsync();
        return Ok();
    }
}

// ─── DTOs ───────────────────────────────────────────────────────────────────

public record LohnZulagTypDto(
    string  Bezeichnung,
    string  Typ,
    bool    SvPflichtig,
    bool    QstPflichtig,
    int     SortOrder,
    bool    Aktiv = true
);

public record LohnZulageDto(
    int     EmployeeId,
    string  Periode,
    int     TypId,
    decimal Betrag,
    string? Bemerkung
);

public record LohnZulageUpdateDto(
    decimal Betrag,
    string? Bemerkung
);

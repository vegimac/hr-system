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
    //  LOHNPOSITIONEN ALS TYP-KATALOG  (für Zulagen/Abzüge-Dropdown)
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Aktive Lohnpositionen vom Typ ZULAGE oder ABZUG — für das Erfassungs-Dropdown.
    /// </summary>
    [HttpGet("lohn-zulag-typen")]
    public async Task<IActionResult> GetZulagTypen()
    {
        var list = await _db.Lohnpositionen
            .Where(l => l.IsActive && (l.Typ == "ZULAGE" || l.Typ == "ABZUG"))
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Code)
            .Select(l => new
            {
                l.Id,
                l.Code,
                l.Bezeichnung,
                l.Typ,
                l.AhvAlvPflichtig,
                l.NbuvPflichtig,
                l.KtgPflichtig,
                l.BvgPflichtig,
                l.QstPflichtig,
                SvPflichtig = l.AhvAlvPflichtig || l.NbuvPflichtig || l.KtgPflichtig || l.BvgPflichtig,
                l.SortOrder,
                Aktiv       = l.IsActive
            })
            .ToListAsync();
        return Ok(list);
    }

    // ═══════════════════════════════════════════════════════
    //  EINTRÄGE  (pro Mitarbeiter + Periode)
    // ═══════════════════════════════════════════════════════

    /// <summary>Alle Einträge eines Mitarbeiters für eine Periode (YYYY-MM)</summary>
    [HttpGet("lohn-zulagen/{employeeId}/{periode}")]
    public async Task<IActionResult> GetZulagen(int employeeId, string periode)
    {
        var list = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .Where(z => z.EmployeeId == employeeId && z.Periode == periode)
            .OrderBy(z => z.Lohnposition!.SortOrder)
            .ThenBy(z => z.CreatedAt)
            .Select(z => new
            {
                z.Id,
                z.EmployeeId,
                z.Periode,
                LohnpositionId          = z.LohnpositionId,
                LohnpositionCode        = z.Lohnposition!.Code,
                LohnpositionBezeichnung = z.Lohnposition.Bezeichnung,
                Typ                     = z.Lohnposition.Typ,
                AhvAlvPflichtig         = z.Lohnposition.AhvAlvPflichtig,
                NbuvPflichtig           = z.Lohnposition.NbuvPflichtig,
                KtgPflichtig            = z.Lohnposition.KtgPflichtig,
                BvgPflichtig            = z.Lohnposition.BvgPflichtig,
                QstPflichtig            = z.Lohnposition.QstPflichtig,
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
        if (dto.Periode.Length != 7 || dto.Periode[4] != '-')
            return BadRequest("Periode muss im Format YYYY-MM sein.");
        if (dto.Betrag <= 0)
            return BadRequest("Betrag muss grösser als 0 sein.");

        var lp = await _db.Lohnpositionen.FindAsync(dto.LohnpositionId);
        if (lp is null) return BadRequest("Unbekannte Lohnposition.");
        if (lp.Typ != "ZULAGE" && lp.Typ != "ABZUG")
            return BadRequest("Lohnposition muss Typ ZULAGE oder ABZUG haben.");

        var entry = new LohnZulage
        {
            EmployeeId    = dto.EmployeeId,
            Periode       = dto.Periode,
            LohnpositionId = dto.LohnpositionId,
            Betrag        = Math.Round(dto.Betrag, 2),
            Bemerkung     = dto.Bemerkung?.Trim(),
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow
        };
        _db.LohnZulagen.Add(entry);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            entry.Id, entry.EmployeeId, entry.Periode,
            LohnpositionId          = lp.Id,
            LohnpositionCode        = lp.Code,
            LohnpositionBezeichnung = lp.Bezeichnung,
            Typ                     = lp.Typ,
            AhvAlvPflichtig         = lp.AhvAlvPflichtig,
            NbuvPflichtig           = lp.NbuvPflichtig,
            KtgPflichtig            = lp.KtgPflichtig,
            BvgPflichtig            = lp.BvgPflichtig,
            QstPflichtig            = lp.QstPflichtig,
            entry.Betrag, entry.Bemerkung, entry.CreatedAt
        });
    }

    /// <summary>Eintrag aktualisieren (Betrag / Bemerkung)</summary>
    [HttpPut("lohn-zulagen/{id}")]
    public async Task<IActionResult> UpdateZulage(int id, [FromBody] LohnZulageUpdateDto dto)
    {
        var entry = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .FirstOrDefaultAsync(z => z.Id == id);
        if (entry is null) return NotFound();
        if (dto.Betrag <= 0) return BadRequest("Betrag muss grösser als 0 sein.");

        entry.Betrag    = Math.Round(dto.Betrag, 2);
        entry.Bemerkung = dto.Bemerkung?.Trim();
        entry.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new
        {
            entry.Id, entry.EmployeeId, entry.Periode,
            LohnpositionId          = entry.Lohnposition!.Id,
            LohnpositionCode        = entry.Lohnposition.Code,
            LohnpositionBezeichnung = entry.Lohnposition.Bezeichnung,
            Typ                     = entry.Lohnposition.Typ,
            AhvAlvPflichtig         = entry.Lohnposition.AhvAlvPflichtig,
            NbuvPflichtig           = entry.Lohnposition.NbuvPflichtig,
            KtgPflichtig            = entry.Lohnposition.KtgPflichtig,
            BvgPflichtig            = entry.Lohnposition.BvgPflichtig,
            QstPflichtig            = entry.Lohnposition.QstPflichtig,
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

public record LohnZulageDto(
    int     EmployeeId,
    string  Periode,
    int     LohnpositionId,
    decimal Betrag,
    string? Bemerkung
);

public record LohnZulageUpdateDto(
    decimal Betrag,
    string? Bemerkung
);

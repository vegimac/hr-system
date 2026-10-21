using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

// ============================================================================
// Kontoplan / Lohnart→Konten-Mapping.
// Seit Walter 19.10.2026: Swissdec-Lohnart × Vertrags-Modell (CREW/FLEX/MGMT)
// → Soll/Gegen/MWST. Keine Kostenstellen-Pflege (Abacus nur Konten).
// ============================================================================
[ApiController]
[Route("api/lohn-konto-mapping")]
[Authorize(Roles = "admin,superuser")]
public class LohnKontoMappingController : ControllerBase
{
    private readonly AppDbContext _db;
    public LohnKontoMappingController(AppDbContext db) => _db = db;

    // GET /api/lohn-konto-mapping  — optional ?konto=4000 &modell=CREW
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? konto, [FromQuery] string? modell, [FromQuery] string? kst)
    {
        var q = _db.LohnKontoMappings.Where(m => m.IsActive);
        if (!string.IsNullOrWhiteSpace(konto))
            q = q.Where(m => m.Fibukonto == konto || m.Gegenkonto == konto);
        if (!string.IsNullOrWhiteSpace(modell))
            q = q.Where(m => m.Modell == modell);
        // Legacy-Filter (alte Bookmarks)
        else if (!string.IsNullOrWhiteSpace(kst))
            q = q.Where(m => m.KostenstelleNr == kst);

        var rows = await q
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
            .Select(m => new
            {
                m.Id, m.Position, m.SubPosition,
                m.LohnartCode, m.Modell,
                m.Fibukonto, m.Gegenkonto,
                m.KostenstelleNr, m.KostenstelleName,
                m.Bezeichnung, m.IsVormonat, m.SortOrder,
                m.MwstKonto, m.MwstCode, m.MwstProzent
            })
            .ToListAsync();
        return Ok(rows);
    }

    /// <summary>
    /// Eine Zeile pro aktiver Lohnposition, mit den Konten aus <see cref="FibuKatalog"/>.
    /// Die Liste ist damit dieselbe wie unter Lohnpositionen.
    /// </summary>
    [HttpGet("nach-lohnart")]
    public async Task<IActionResult> NachLohnart()
    {
        var positionen = await _db.Lohnpositionen.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Code)
            .Select(p => new { p.Code, p.Bezeichnung })
            .ToListAsync();
        var maps = await _db.LohnKontoMappings.AsNoTracking()
            .Where(m => m.IsActive)
            .Select(m => new { m.Id, m.Position, m.SubPosition, m.Fibukonto })
            .ToListAsync();

        var zeilen = positionen.Select(p =>
        {
            var k = FibuKatalog.Fuer(p.Code);
            var ohne = FibuKatalog.OhneBuchung(p.Code);
            var ids = k is { MapPosition: int pos } konto
                ? maps.Where(m => m.Position == pos
                        && (konto.MapSub == null || m.SubPosition == konto.MapSub)
                        && (konto.MapFibukonto == null || m.Fibukonto == konto.MapFibukonto))
                    .Select(m => m.Id).ToList()
                : new List<int>();
            return new
            {
                p.Code,
                p.Bezeichnung,
                soll = k?.Soll,
                haben = k?.Haben,
                agSoll = k?.AgSoll,
                agHaben = k?.AgHaben,
                hinweis = k?.Hinweis,
                ohneBuchung = ohne,
                zugeordnet = k != null || ohne,
                ids
            };
        }).ToList();
        return Ok(zeilen);
    }

    [HttpGet("konten")]
    public async Task<IActionResult> Konten()
    {
        var soll  = await _db.LohnKontoMappings.Where(m => m.IsActive).Select(m => m.Fibukonto).ToListAsync();
        var haben = await _db.LohnKontoMappings.Where(m => m.IsActive).Select(m => m.Gegenkonto).ToListAsync();
        var konten = soll.Concat(haben).Distinct().OrderBy(k => k.Length).ThenBy(k => k).ToList();
        return Ok(konten);
    }

    public record MappingEditDto(
        string Fibukonto, string Gegenkonto, string? Bezeichnung,
        string? MwstKonto = null, string? MwstCode = null, decimal? MwstProzent = null);

    [Authorize(Roles = "admin")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] MappingEditDto dto)
    {
        var m = await _db.LohnKontoMappings.FindAsync(id);
        if (m == null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Fibukonto) || string.IsNullOrWhiteSpace(dto.Gegenkonto))
            return BadRequest(new { error = "Soll- und Gegenkonto sind Pflicht." });
        var mk = string.IsNullOrWhiteSpace(dto.MwstKonto) ? null : dto.MwstKonto.Trim();
        var mc = string.IsNullOrWhiteSpace(dto.MwstCode)  ? null : dto.MwstCode.Trim();
        if ((mk == null) != (mc == null))
            return BadRequest(new { error = "MWST-Konto und MWST-Code entweder beide setzen oder beide leer lassen." });
        m.Fibukonto  = dto.Fibukonto.Trim();
        m.Gegenkonto = dto.Gegenkonto.Trim();
        m.MwstKonto  = mk;
        m.MwstCode   = mc;
        m.MwstProzent = mk == null ? null : (dto.MwstProzent ?? 0m);
        if (!string.IsNullOrWhiteSpace(dto.Bezeichnung)) m.Bezeichnung = dto.Bezeichnung.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { m.Id, m.Fibukonto, m.Gegenkonto, m.Bezeichnung, m.MwstKonto, m.MwstCode, m.MwstProzent });
    }
}

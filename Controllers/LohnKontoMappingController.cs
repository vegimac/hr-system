using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

// ============================================================================
// Kontoplan / Lohnart→Konten-Mapping (Walter-Vorgabe 22.05.2026).
// Pflegt die Tabelle lohn_konto_mapping (übernommen aus dem Mirus/McD-CH-
// Buchungsschema). Schlüssel: Position × SubPos × Kostenstelle → Soll/Gegenkonto.
// Treibt den Fibu-Journal-Generator (Etappe 2) und später den Abacus-Export.
//
// Lohn-Edit-Lock: NICHT relevant — Katalog/Stammdaten, kein MA-Lohn. Eine
// Konto-Korrektur verändert keine abgeschlossene Abrechnung. Im Audit-Test
// EditLockEndpointAuditTests whitelisted.
// ============================================================================
[ApiController]
[Route("api/lohn-konto-mapping")]
[Authorize(Roles = "admin,superuser")]
public class LohnKontoMappingController : ControllerBase
{
    private readonly AppDbContext _db;
    public LohnKontoMappingController(AppDbContext db) => _db = db;

    // GET /api/lohn-konto-mapping  → alle aktiven Mappings, sortiert.
    // Optional ?konto=4000 oder ?kst=100 filtern.
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? konto, [FromQuery] string? kst)
    {
        var q = _db.LohnKontoMappings.Where(m => m.IsActive);
        if (!string.IsNullOrWhiteSpace(konto))
            q = q.Where(m => m.Fibukonto == konto || m.Gegenkonto == konto);
        if (!string.IsNullOrWhiteSpace(kst))
            q = q.Where(m => m.KostenstelleNr == kst);

        var rows = await q
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
            .Select(m => new
            {
                m.Id, m.Position, m.SubPosition,
                m.Fibukonto, m.Gegenkonto,
                m.KostenstelleNr, m.KostenstelleName,
                m.Bezeichnung, m.IsVormonat, m.SortOrder
            })
            .ToListAsync();
        return Ok(rows);
    }

    // GET /api/lohn-konto-mapping/konten  → distinkte Kontoliste (für Übersicht/Validierung).
    [HttpGet("konten")]
    public async Task<IActionResult> Konten()
    {
        var soll  = await _db.LohnKontoMappings.Where(m => m.IsActive).Select(m => m.Fibukonto).ToListAsync();
        var haben = await _db.LohnKontoMappings.Where(m => m.IsActive).Select(m => m.Gegenkonto).ToListAsync();
        var konten = soll.Concat(haben).Distinct().OrderBy(k => k.Length).ThenBy(k => k).ToList();
        return Ok(konten);
    }

    public record MappingEditDto(string Fibukonto, string Gegenkonto, string? Bezeichnung);

    // PUT /api/lohn-konto-mapping/{id}  → Konto-Korrektur (nur Konten + Text;
    // Schlüsselfelder Position/SubPos/Kostenstelle bleiben fix).
    [Authorize(Roles = "admin")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] MappingEditDto dto)
    {
        var m = await _db.LohnKontoMappings.FindAsync(id);
        if (m == null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Fibukonto) || string.IsNullOrWhiteSpace(dto.Gegenkonto))
            return BadRequest(new { error = "Soll- und Gegenkonto sind Pflicht." });
        m.Fibukonto  = dto.Fibukonto.Trim();
        m.Gegenkonto = dto.Gegenkonto.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Bezeichnung)) m.Bezeichnung = dto.Bezeichnung.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { m.Id, m.Fibukonto, m.Gegenkonto, m.Bezeichnung });
    }
}

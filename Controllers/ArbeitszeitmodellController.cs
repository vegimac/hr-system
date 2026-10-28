using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Arbeitszeitmodelle einer Rechtseinheit (Walter 27.09.2026). Swissdec meldet sie
/// als <c>CompanyWorkingTime</c>; jede Person verweist auf genau eines.
///
/// <para><b>Die Lohnrechnung liest diese Modelle nicht.</b> Sie arbeitet weiter mit
/// den Wochenstunden der Filiale. Das Modell ist eine Meldeangabe — eine Meldedatei
/// darf keinen Lohn verändern.</para>
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superuser,user,buchhaltung")]
[Route("api/arbeitszeitmodelle")]
public class ArbeitszeitmodellController : ControllerBase
{
    private readonly AppDbContext _db;
    public ArbeitszeitmodellController(AppDbContext db) => _db = db;

    public record ModellDto(int? HauptsitzId, string? Kennung, string? Bezeichnung,
        decimal? Wochenstunden, decimal? Wochenlektionen, decimal? FerientageProJahr, bool? IsActive);

    public record ZuordnungDto(int ArbeitszeitmodellId, DateOnly GueltigAb, string? Bemerkung);

    [HttpGet]
    public async Task<IActionResult> Liste([FromQuery] int? hauptsitzId)
    {
        var q = _db.Arbeitszeitmodelle.AsNoTracking().AsQueryable();
        if (hauptsitzId is > 0) q = q.Where(m => m.HauptsitzId == hauptsitzId);
        var modelle = await q.OrderBy(m => m.HauptsitzId).ThenBy(m => m.Id).ToListAsync();
        var benutzt = await _db.EmployeeArbeitszeitmodelle.AsNoTracking()
            .GroupBy(z => z.ArbeitszeitmodellId)
            .Select(g => new { Id = g.Key, Anzahl = g.Select(x => x.EmployeeId).Distinct().Count() })
            .ToListAsync();
        return Ok(modelle.Select(m => new
        {
            m.Id, m.HauptsitzId, m.Kennung, m.Bezeichnung,
            m.Wochenstunden, m.Wochenlektionen, m.FerientageProJahr, m.IsActive,
            kennungOderId = m.KennungOderId,
            meldefaehig = m.IstMeldefaehig,
            mitarbeiter = benutzt.FirstOrDefault(b => b.Id == m.Id)?.Anzahl ?? 0,
        }));
    }

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<IActionResult> Anlegen([FromBody] ModellDto dto)
    {
        if (dto.HauptsitzId is not > 0)
            return BadRequest(new { error = "HAUPTSITZ_FEHLT", message = "Bitte die Rechtseinheit angeben." });
        var fehler = await PruefeModellAsync(dto, ausschlussId: null);
        if (fehler != null) return fehler;

        var m = new Arbeitszeitmodell
        {
            HauptsitzId = dto.HauptsitzId!.Value,
            Kennung = string.IsNullOrWhiteSpace(dto.Kennung) ? null : dto.Kennung!.Trim(),
            Bezeichnung = dto.Bezeichnung!.Trim(),
            Wochenstunden = dto.Wochenstunden,
            Wochenlektionen = dto.Wochenlektionen,
            FerientageProJahr = dto.FerientageProJahr,
            IsActive = dto.IsActive ?? true,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };
        _db.Arbeitszeitmodelle.Add(m);
        await _db.SaveChangesAsync();
        return Ok(new { m.Id });
    }

    [Authorize(Roles = "admin")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Aendern(int id, [FromBody] ModellDto dto)
    {
        var m = await _db.Arbeitszeitmodelle.FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return NotFound();
        // Hauptsitz bleibt am bestehenden Modell — Wechsel der Rechtseinheit nicht erlaubt.
        dto = dto with { HauptsitzId = m.HauptsitzId };
        var fehler = await PruefeModellAsync(dto, ausschlussId: id);
        if (fehler != null) return fehler;
        m.Bezeichnung = dto.Bezeichnung!.Trim();
        m.Kennung = string.IsNullOrWhiteSpace(dto.Kennung) ? null : dto.Kennung!.Trim();
        m.Wochenstunden = dto.Wochenstunden;
        m.Wochenlektionen = dto.Wochenlektionen;
        m.FerientageProJahr = dto.FerientageProJahr;
        if (dto.IsActive != null) m.IsActive = dto.IsActive.Value;
        m.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { m.Id });
    }

    /// <summary>
    /// Bezeichnung eindeutig pro Hauptsitz; Wochenstunden/Lektionen max. 50;
    /// mind. einer der beiden Zeitwerte &gt; 0. Kein von/bis am Modell —
    /// das gilt nur an der MA-Zuordnung (Gültig-ab).
    /// </summary>
    async Task<IActionResult?> PruefeModellAsync(ModellDto dto, int? ausschlussId)
    {
        if (string.IsNullOrWhiteSpace(dto.Bezeichnung))
            return BadRequest(new { error = "BEZEICHNUNG_FEHLT", message = "Bitte eine Bezeichnung angeben (z.B. «Standard»)." });
        var bez = dto.Bezeichnung.Trim();
        if (dto.Wochenstunden is > 50m)
            return BadRequest(new { error = "STUNDEN_MAX", message = "Wochenstunden höchstens 50." });
        if (dto.Wochenlektionen is > 50m)
            return BadRequest(new { error = "LEKTIONEN_MAX", message = "Wochenlektionen höchstens 50." });
        if (dto.Wochenstunden is < 0m || dto.Wochenlektionen is < 0m)
            return BadRequest(new { error = "ZEIT_NEGATIV", message = "Stunden und Lektionen dürfen nicht negativ sein." });
        if (dto.Wochenstunden is not > 0m && dto.Wochenlektionen is not > 0m)
            return BadRequest(new { error = "ZEIT_FEHLT", message = "Wochenstunden oder Wochenlektionen angeben — Swissdec verlangt einen der beiden Werte." });

        var hsId = dto.HauptsitzId!.Value;
        var namen = await _db.Arbeitszeitmodelle.AsNoTracking()
            .Where(x => x.HauptsitzId == hsId && x.Id != (ausschlussId ?? 0))
            .Select(x => x.Bezeichnung)
            .ToListAsync();
        if (namen.Any(n => string.Equals(n, bez, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new { error = "BEZEICHNUNG_DOPPELT",
                message = $"Die Bezeichnung «{bez}» gibt es an diesem Hauptsitz schon — bitte eine andere wählen." });
        return null;
    }

    /// <summary>
    /// Löschen nur, solange niemand darauf verweist — sonst verlöre die Meldung
    /// die Zuordnung, ohne dass es jemand merkt.
    /// </summary>
    [Authorize(Roles = "admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Loeschen(int id)
    {
        var m = await _db.Arbeitszeitmodelle.FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return NotFound();
        var anzahl = await _db.EmployeeArbeitszeitmodelle.CountAsync(z => z.ArbeitszeitmodellId == id);
        if (anzahl > 0)
            return Conflict(new { error = "IN_VERWENDUNG",
                message = $"{anzahl} Zuordnungen verweisen auf dieses Modell. Zuerst umhängen, dann löschen — oder das Modell inaktiv setzen." });
        _db.Arbeitszeitmodelle.Remove(m);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ── Zuordnung am Mitarbeiter ─────────────────────────────────────────

    [HttpGet("mitarbeiter/{employeeId:int}")]
    public async Task<IActionResult> Zuordnungen(int employeeId)
    {
        var zuord = await _db.EmployeeArbeitszeitmodelle.AsNoTracking()
            .Where(z => z.EmployeeId == employeeId)
            .OrderByDescending(z => z.GueltigAb)
            .ToListAsync();
        var ids = zuord.Select(z => z.ArbeitszeitmodellId).Distinct().ToList();
        var modelle = await _db.Arbeitszeitmodelle.AsNoTracking()
            .Where(m => ids.Contains(m.Id)).ToListAsync();
        return Ok(zuord.Select(z =>
        {
            var m = modelle.FirstOrDefault(x => x.Id == z.ArbeitszeitmodellId);
            return new
            {
                z.Id, z.ArbeitszeitmodellId, z.GueltigAb, z.Bemerkung,
                z.AbweichendeWochenstunden, z.AbweichendeWochenlektionen,
                bezeichnung = m?.Bezeichnung,
                wochenstunden = m?.Wochenstunden,
                wochenlektionen = m?.Wochenlektionen,
            };
        }));
    }

    [HttpPost("mitarbeiter/{employeeId:int}")]
    public async Task<IActionResult> Zuordnen(int employeeId, [FromBody] ZuordnungDto dto)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId)) return NotFound();
        if (!await _db.Arbeitszeitmodelle.AnyAsync(m => m.Id == dto.ArbeitszeitmodellId))
            return BadRequest(new { error = "MODELL_FEHLT", message = "Dieses Arbeitszeitmodell gibt es nicht." });

        // Pro Tag nur eine Zuordnung — ein zweiter Eintrag am selben Tag ersetzt
        // den ersten, statt zwei widersprüchliche Angaben zu hinterlassen.
        var z = await _db.EmployeeArbeitszeitmodelle
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.GueltigAb == dto.GueltigAb);
        if (z == null)
        {
            z = new EmployeeArbeitszeitmodell { EmployeeId = employeeId, GueltigAb = dto.GueltigAb, CreatedAt = DateTime.Now };
            _db.EmployeeArbeitszeitmodelle.Add(z);
        }
        z.ArbeitszeitmodellId = dto.ArbeitszeitmodellId;
        z.Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung!.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { z.Id });
    }

    [HttpDelete("mitarbeiter/zuordnung/{id:int}")]
    public async Task<IActionResult> ZuordnungLoeschen(int id)
    {
        var z = await _db.EmployeeArbeitszeitmodelle.FirstOrDefaultAsync(x => x.Id == id);
        if (z == null) return NotFound();
        _db.EmployeeArbeitszeitmodelle.Remove(z);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}

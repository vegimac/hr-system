using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.LohnFormelwerk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Mirus-Lohnraster für die Migration (Walter 21.10.2026):
/// links Mirus-Code, rechts die Swissdec-Lohnposition in OneCrew.
/// Mehrere Mirus-Zeilen dürfen auf dieselbe Swissdec-Nr. zeigen.
/// «Übernehmen» legt keine Mirus-Nummer mehr an — nur Verknüpfung.
/// </summary>
[Authorize(Roles = "admin")]
[ApiController]
[Route("api/elm-lohnraster")]
public class ElmLohnrasterController : ControllerBase
{
    private readonly AppDbContext _db;
    public ElmLohnrasterController(AppDbContext db) { _db = db; }

    /// <summary>Mirus-Code → OneCrew-Code (Swissdec oder Betrieb/Vortrag).</summary>
    static string? ZielCode(string? mirus)
    {
        if (string.IsNullOrWhiteSpace(mirus)) return null;
        var c = mirus.Trim();
        if (LohnCodeAlias.MirusNachSwissdec.TryGetValue(c, out var swiss)) return swiss;
        var via = SwissdecLohnartZuordnung.FuerOneCrewCode(c);
        if (!string.IsNullOrEmpty(via)) return via;
        // Betrieb / Vortrag: Code ist schon der OneCrew-Code
        if (LohnCodes.IstInternOhneSwissdec(c)) return c;
        return null;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        // Unverlinkte Mirus-Zeilen an die passende Swissdec-Position hängen
        // (mehrere Mirus → eine Swissdec erlaubt).
        var unverlinkt = await _db.ElmLohnraster
            .Where(e => e.VerwendetLohnpositionId == null)
            .ToListAsync();
        if (unverlinkt.Count > 0)
        {
            var lohnpos = await _db.Lohnpositionen.AsNoTracking()
                .Where(l => l.IsActive)
                .Select(l => new { l.Id, l.Code })
                .ToListAsync();
            var byCode = lohnpos
                .GroupBy(l => l.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
            var geaendert = false;
            foreach (var e in unverlinkt)
            {
                var ziel = ZielCode(e.Code);
                if (ziel == null) continue;
                if (!byCode.TryGetValue(ziel, out var lpId)) continue;
                e.VerwendetLohnpositionId = lpId;
                geaendert = true;
            }
            if (geaendert) await _db.SaveChangesAsync();
        }

        var rows = await _db.ElmLohnraster.AsNoTracking()
            .Include(e => e.VerwendetLohnposition)
            .ToListAsync();
        return Ok(rows
            .OrderBy(e => decimal.TryParse(e.Pos, out var p) ? p : 9999)
            .ThenBy(e => decimal.TryParse(e.Sub, out var s) ? s : 0)
            .Select(e => new
            {
                e.Id, e.Code, e.Bezeichnung, e.Gruppe, e.Typ,
                e.UebersetzungIt, e.UebersetzungFr,
                e.Lohnausweisfeld, e.StatistikCode, e.Steuerung, e.BetragProzent,
                e.Inaktiv, e.Ahv, e.Qst, e.QstPeriodisch, e.Bvg, e.Uvg, e.Uvgz, e.Ktg, e.Ml13,
                attrs = e.AttrsJson,
                verwendetLohnpositionId = e.VerwendetLohnpositionId,
                verwendetCode = e.VerwendetLohnposition?.Code,
                verwendetBezeichnung = e.VerwendetLohnposition?.Bezeichnung,
                verwendetAktiv = e.VerwendetLohnposition?.IsActive ?? false,
                swissdecZiel = ZielCode(e.Code),
            }));
    }

    /// <summary>
    /// Verknüpft die Mirus-Zeile mit der Swissdec-Lohnposition.
    /// Legt keine neue Lohnart mit Mirus-Nummer an.
    /// </summary>
    [HttpPost("{id}/uebernehmen")]
    public async Task<IActionResult> Uebernehmen(int id)
    {
        var e = await _db.ElmLohnraster.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        if (e.VerwendetLohnpositionId != null)
            return Conflict(new { error = "SCHON_VERWENDET", message = "Diese Position ist bereits verknüpft." });
        if (e.Typ != "LOHNART" && e.Typ != "ABSENZ" && e.Typ != "SV_ABZUG")
            return BadRequest(new { error = "NUR_LOHNARTEN", message = "Nur Lohnarten, Betrieb und Vorträge können verknüpft werden." });

        var ziel = ZielCode(e.Code);
        if (string.IsNullOrEmpty(ziel))
            return BadRequest(new { error = "KEINE_SWISSDEC", message = $"Für Mirus «{e.Code}» gibt es keine Swissdec-Zuordnung — bitte von Hand verknüpfen oder weglassen." });

        var lp = await _db.Lohnpositionen
            .Where(l => l.Code == ziel && l.IsActive)
            .OrderBy(l => l.Id)
            .FirstOrDefaultAsync()
            ?? await _db.Lohnpositionen.FirstOrDefaultAsync(l => l.Code == ziel);
        if (lp == null)
            return BadRequest(new { error = "SWISSDEC_FEHLT", message = $"Swissdec-Lohnposition «{ziel}» fehlt in OneCrew — zuerst unter Lohnpositionen anlegen." });

        e.VerwendetLohnpositionId = lp.Id;
        await _db.SaveChangesAsync();
        return Ok(new { lohnpositionId = lp.Id, code = lp.Code, verknuepft = true });
    }

    public class VerknuepfenDto { public int LohnpositionId { get; set; } }

    /// <summary>Ordnet eine bestehende Lohnposition zu (mehrere Mirus → eine Swissdec erlaubt).</summary>
    [HttpPost("{id}/verknuepfen")]
    public async Task<IActionResult> Verknuepfen(int id, [FromBody] VerknuepfenDto dto)
    {
        var e = await _db.ElmLohnraster.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        var lp = await _db.Lohnpositionen.FirstOrDefaultAsync(l => l.Id == dto.LohnpositionId);
        if (lp == null) return NotFound(new { error = "LOHNPOSITION_FEHLT" });
        e.VerwendetLohnpositionId = lp.Id;
        await _db.SaveChangesAsync();
        return Ok(new { code = lp.Code });
    }

    [HttpPost("{id}/loesen")]
    public async Task<IActionResult> Loesen(int id)
    {
        var e = await _db.ElmLohnraster.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        e.VerwendetLohnpositionId = null;
        await _db.SaveChangesAsync();
        return Ok();
    }
}

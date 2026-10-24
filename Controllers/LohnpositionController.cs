using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.LohnFormelwerk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/lohnpositionen")]
[Authorize]
public class LohnpositionController : ControllerBase
{
    private readonly AppDbContext _db;
    public LohnpositionController(AppDbContext db) => _db = db;

    // GET – alle Positionen (aktiv + inaktiv), sortiert
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _db.Lohnpositionen
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Code)
            .ToListAsync();
        return Ok(items);
    }

    // GET – nur aktive Positionen
    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var items = await _db.Lohnpositionen
            .Where(l => l.IsActive)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Code)
            .ToListAsync();
        return Ok(items);
    }

    // GET api/lohnpositionen/swissdec-lohnarten — Musterlohnartenstamm für die
    // Auswahlliste «Swissdec-Lohnart» im Dialog (Walter 08.09.2026).
    [HttpGet("swissdec-lohnarten")]
    public IActionResult SwissdecLohnarten()
        => Ok(SwissdecLohnartenKatalog.Alle
            .Select(l => new { l.Code, l.Bezeichnung, l.Kategorie, l.Typ, l.Ahv, l.Uvg, l.Uvgz, l.Ktg, l.Bvg, l.Qst, l.Ml13, l.Lohnausweis, l.FibuKonto }));

    // GET api/lohnpositionen/swissdec-zuordnung — feste OneCrew→Swissdec-Tabelle + Ist-Stand (Walter 17.10.2026).
    [HttpGet("swissdec-zuordnung")]
    public async Task<IActionResult> SwissdecZuordnung()
    {
        var positionen = await _db.Lohnpositionen.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Code)
            .ToListAsync();
        var katalog = SwissdecLohnartenKatalog.Alle.ToDictionary(k => k.Code, k => k.Bezeichnung);
        var zeilen = positionen.Select(p =>
        {
            var vorschlag = SwissdecLohnartZuordnung.FuerOneCrewCode(p.Code);
            var ist = string.IsNullOrWhiteSpace(p.SwissdecLohnart) ? null : p.SwissdecLohnart.Trim();
            return new
            {
                p.Id,
                oneCrewCode = p.Code,
                oneCrewBezeichnung = p.Bezeichnung,
                swissdecIst = ist,
                swissdecIstBez = ist != null && katalog.TryGetValue(ist, out var ib) ? ib : null,
                swissdecVorschlag = vorschlag,
                swissdecVorschlagBez = vorschlag != null && katalog.TryGetValue(vorschlag, out var vb) ? vb : null,
                status = ist == null
                    ? (vorschlag != null ? "leer_mit_vorschlag" : "leer_ohne_vorschlag")
                    : (vorschlag != null && !string.Equals(ist, vorschlag, StringComparison.OrdinalIgnoreCase)
                        ? "abweichend"
                        : "ok")
            };
        }).ToList();
        return Ok(new
        {
            zuordnungstabelle = SwissdecLohnartZuordnung.AlsListe()
                .Select(z => new
                {
                    oneCrewCode = z.OneCrew,
                    swissdecCode = z.Swissdec,
                    swissdecBez = katalog.TryGetValue(z.Swissdec, out var b) ? b : null
                }),
            positionen = zeilen,
            zusammenfassung = new
            {
                total = zeilen.Count,
                ok = zeilen.Count(z => z.status == "ok"),
                leerMitVorschlag = zeilen.Count(z => z.status == "leer_mit_vorschlag"),
                leerOhneVorschlag = zeilen.Count(z => z.status == "leer_ohne_vorschlag"),
                abweichend = zeilen.Count(z => z.status == "abweichend")
            }
        });
    }

    // POST api/lohnpositionen/swissdec-zuordnung-anwenden — leere Felder aus Vorschlag füllen (Walter 17.10.2026).
    // nurLeere=true (Default): überschreibt nie eine vorhandene Swissdec-Lohnart.
    [HttpPost("swissdec-zuordnung-anwenden")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> SwissdecZuordnungAnwenden([FromQuery] bool nurLeere = true)
    {
        var positionen = await _db.Lohnpositionen.Where(p => p.IsActive).ToListAsync();
        int gesetzt = 0, uebersprungen = 0, ohneVorschlag = 0;
        var details = new List<object>();
        foreach (var p in positionen)
        {
            var vorschlag = SwissdecLohnartZuordnung.FuerOneCrewCode(p.Code);
            if (vorschlag == null) { ohneVorschlag++; continue; }
            if (nurLeere && !string.IsNullOrWhiteSpace(p.SwissdecLohnart)) { uebersprungen++; continue; }
            if (string.Equals(p.SwissdecLohnart?.Trim(), vorschlag, StringComparison.OrdinalIgnoreCase))
            { uebersprungen++; continue; }
            var alt = p.SwissdecLohnart;
            p.SwissdecLohnart = vorschlag;
            gesetzt++;
            details.Add(new { p.Code, p.Bezeichnung, vorher = alt, nachher = vorschlag });
        }
        if (gesetzt > 0) await _db.SaveChangesAsync();
        return Ok(new { gesetzt, uebersprungen, ohneVorschlag, details });
    }

    // GET – einzelne Position
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _db.Lohnpositionen.FindAsync(id);
        if (item is null) return NotFound();
        return Ok(item);
    }

    // POST – neue Position anlegen (nur Admin)
    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Create([FromBody] Lohnposition dto)
    {
        dto.Id        = 0;
        dto.IsActive  = true;
        dto.Code      = (dto.Code ?? "").Trim();
        dto.MirusCode = string.IsNullOrWhiteSpace(dto.MirusCode) ? null : dto.MirusCode.Trim();
        dto.FormelId  = string.IsNullOrWhiteSpace(dto.FormelId) ? null : dto.FormelId.Trim();
        if (!LohnCodes.IstSwissdecStammNr(dto.Code) && !LohnCodes.IstInternOhneSwissdec(dto.Code)
            && !string.Equals(dto.Code, "180.3", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "CODE_NICHT_SWISSDEC",
                message = "Nur Swissdec-Stammnummern oder bekannte Betrieb-/Vortrag-Codes (600.24, 901–906, 180.3 …)." });
        // code = Swissdec-Stamm; swissdec_lohnart = vierstelliger ELM-Kopf (leer bei Betrieb)
        dto.SwissdecLohnart = LohnCodes.IstInternOhneSwissdec(dto.Code)
            ? null
            : SwissdecKopf(dto.Code, dto.SwissdecLohnart);
        if (Lohnposition.SwissdecEinmalig(dto.SwissdecLohnart))
            dto.QstPeriodisch = false;
        dto.CreatedAt = DateTime.Now;
        _db.Lohnpositionen.Add(dto);
        await _db.SaveChangesAsync();
        return Ok(dto);
    }

    // PUT – Position aktualisieren (nur Admin)
    [HttpPut("{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Update(int id, [FromBody] Lohnposition dto)
    {
        var item = await _db.Lohnpositionen.FindAsync(id);
        if (item is null) return NotFound();

        item.Code            = (dto.Code ?? "").Trim();
        if (!LohnCodes.IstSwissdecStammNr(item.Code) && !LohnCodes.IstInternOhneSwissdec(item.Code)
            && !string.Equals(item.Code, "180.3", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "CODE_NICHT_SWISSDEC",
                message = "Nur Swissdec-Stammnummern oder bekannte Betrieb-/Vortrag-Codes (600.24, 901–906, 180.3 …)." });
        item.MirusCode       = string.IsNullOrWhiteSpace(dto.MirusCode) ? null : dto.MirusCode.Trim();
        item.FormelId        = string.IsNullOrWhiteSpace(dto.FormelId) ? null : dto.FormelId.Trim();
        item.Bezeichnung     = dto.Bezeichnung;
        item.Typ             = dto.Typ;
        item.AhvAlvPflichtig = dto.AhvAlvPflichtig;
        item.NbuvPflichtig   = dto.NbuvPflichtig;
        item.KtgPflichtig    = dto.KtgPflichtig;
        item.BvgPflichtig    = dto.BvgPflichtig;
        item.QstPflichtig           = dto.QstPflichtig;
        item.QstPeriodisch          = dto.QstPeriodisch;
        item.DreijehnterMlPflichtig = dto.DreijehnterMlPflichtig;
        item.ZaehltAlsBasisFeiertag = dto.ZaehltAlsBasisFeiertag;
        item.ZaehltAlsBasisFerien   = dto.ZaehltAlsBasisFerien;
        item.ZaehltAlsBasis13ml     = dto.ZaehltAlsBasis13ml;
        item.LohnausweisCode = dto.LohnausweisCode;
        item.SwissdecLohnart = LohnCodes.IstInternOhneSwissdec(item.Code)
            ? null
            : SwissdecKopf(item.Code, dto.SwissdecLohnart);
        item.SortOrder       = dto.SortOrder;
        item.IsActive        = dto.IsActive;

        await _db.SaveChangesAsync();
        return Ok(item);
    }

    /// <summary>ELM-Viersteller aus Positions-Code (1000.2 → 1000).</summary>
    static string? SwissdecKopf(string? code, string? explizit)
    {
        if (!string.IsNullOrWhiteSpace(explizit))
        {
            var e = explizit.Trim();
            return e.Length >= 4 ? e[..Math.Min(4, e.Length)] : e;
        }
        var k = LohnCodes.SwissdecVon(code);
        return string.IsNullOrEmpty(k) ? null : k;
    }

    // DELETE – soft-delete (nur Admin)
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _db.Lohnpositionen.FindAsync(id);
        if (item is null) return NotFound();
        item.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Lohnschema pro Vertragsmodell (Walter-Vorgabe 17.08.2026): Standard-
/// Lohnblatt — welche Lohnpositionen gehören zu welchem Vertragsmodell.
/// Reine Stammdaten/Anzeige (Phase 2) — die Rechen-Engine liest das Schema
/// nicht. Reiner Katalog ohne Lohnlauf-Datumsbezug → EditLock-whitelisted.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/lohnschema")]
public class LohnschemaController : ControllerBase
{
    private static readonly string[] Modelle = { "FLEX", "MTP", "FIX", "FIX-M", "ALLE" };
    private static readonly string[] Arten   = { "automatisch", "saldo", "ereignis", "austritt", "manuell" };

    private readonly AppDbContext _db;
    public LohnschemaController(AppDbContext db) { _db = db; }

    /// <summary>Alle Einträge (optional gefiltert nach Modell). «ALLE»-Einträge
    /// werden bei Modell-Filter mitgeliefert (gelten für jedes Modell).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? modell = null)
    {
        var q = _db.LohnschemaEintraege.AsNoTracking().Include(e => e.Lohnposition).AsQueryable();
        if (!string.IsNullOrWhiteSpace(modell))
            q = q.Where(e => e.Modell == modell || e.Modell == "ALLE");
        var rows = await q
            .OrderBy(e => e.Modell).ThenBy(e => e.SortOrder).ThenBy(e => e.Id)
            .Select(e => new
            {
                e.Id, e.Modell, e.Art, e.SortOrder, e.Bemerkung,
                lohnpositionId = e.LohnpositionId,
                code           = e.Lohnposition!.Code,
                bezeichnung    = e.Lohnposition!.Bezeichnung,
                kategorie      = e.Lohnposition!.Kategorie,
                typ            = e.Lohnposition!.Typ,
                ahv            = e.Lohnposition!.AhvAlvPflichtig,
                nbuv           = e.Lohnposition!.NbuvPflichtig,
                ktg            = e.Lohnposition!.KtgPflichtig,
                bvg            = e.Lohnposition!.BvgPflichtig,
                qst            = e.Lohnposition!.QstPflichtig,
                ml13           = e.Lohnposition!.ZaehltAlsBasis13ml,
            })
            .ToListAsync();
        return Ok(rows);
    }

    public class SchemaDto
    {
        public string Modell { get; set; } = "";
        public int LohnpositionId { get; set; }
        public string Art { get; set; } = "automatisch";
        public int? SortOrder { get; set; }
        public string? Bemerkung { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] SchemaDto dto)
    {
        if (!Modelle.Contains(dto.Modell))
            return BadRequest(new { error = "MODELL_UNGUELTIG", message = $"Modell «{dto.Modell}» unbekannt." });
        if (!Arten.Contains(dto.Art))
            return BadRequest(new { error = "ART_UNGUELTIG", message = $"Art «{dto.Art}» unbekannt." });
        if (!await _db.Lohnpositionen.AnyAsync(l => l.Id == dto.LohnpositionId))
            return NotFound(new { error = "LOHNPOSITION_FEHLT" });
        var exists = await _db.LohnschemaEintraege.AnyAsync(e =>
            e.Modell == dto.Modell && e.LohnpositionId == dto.LohnpositionId && e.Art == dto.Art);
        if (exists)
            return Conflict(new { error = "SCHON_VORHANDEN", message = "Diese Position ist im Modell mit dieser Art bereits hinterlegt." });

        int sort = dto.SortOrder
            ?? ((await _db.LohnschemaEintraege.Where(e => e.Modell == dto.Modell)
                    .MaxAsync(e => (int?)e.SortOrder)) ?? 0) + 10;
        var e2 = new VertragsmodellLohnschema
        {
            Modell = dto.Modell, LohnpositionId = dto.LohnpositionId,
            Art = dto.Art, SortOrder = sort, Bemerkung = dto.Bemerkung,
        };
        _db.LohnschemaEintraege.Add(e2);
        await _db.SaveChangesAsync();
        return Ok(new { e2.Id });
    }

    public class UpdateDto { public string? Art { get; set; } public int? SortOrder { get; set; } public string? Bemerkung { get; set; } }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateDto dto)
    {
        var e = await _db.LohnschemaEintraege.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        if (dto.Art != null)
        {
            if (!Arten.Contains(dto.Art))
                return BadRequest(new { error = "ART_UNGUELTIG" });
            var belegt = await _db.LohnschemaEintraege.AnyAsync(x =>
                x.Id != id && x.Modell == e.Modell && x.LohnpositionId == e.LohnpositionId && x.Art == dto.Art);
            if (belegt)
                return Conflict(new { error = "SCHON_VORHANDEN", message = "Diese Position ist im Modell mit dieser Art bereits hinterlegt." });
            e.Art = dto.Art;
        }
        if (dto.SortOrder.HasValue) e.SortOrder = dto.SortOrder.Value;
        if (dto.Bemerkung != null) e.Bemerkung = dto.Bemerkung;
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var e = await _db.LohnschemaEintraege.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        _db.LohnschemaEintraege.Remove(e);
        await _db.SaveChangesAsync();
        return Ok();
    }
}

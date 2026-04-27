using HrSystem.Data;
using HrSystem.Models;
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
        dto.CreatedAt = DateTime.UtcNow;
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

        item.Code            = dto.Code;
        item.Bezeichnung     = dto.Bezeichnung;
        item.Kategorie       = dto.Kategorie;
        item.Typ             = dto.Typ;
        item.AhvAlvPflichtig = dto.AhvAlvPflichtig;
        item.NbuvPflichtig   = dto.NbuvPflichtig;
        item.KtgPflichtig    = dto.KtgPflichtig;
        item.BvgPflichtig    = dto.BvgPflichtig;
        item.QstPflichtig           = dto.QstPflichtig;
        item.DreijehnterMlPflichtig = dto.DreijehnterMlPflichtig;
        item.ZaehltAlsBasisFeiertag = dto.ZaehltAlsBasisFeiertag;
        item.ZaehltAlsBasisFerien   = dto.ZaehltAlsBasisFerien;
        item.ZaehltAlsBasis13ml     = dto.ZaehltAlsBasis13ml;
        item.LohnausweisCode = dto.LohnausweisCode;
        item.SortOrder       = dto.SortOrder;
        item.IsActive        = dto.IsActive;

        await _db.SaveChangesAsync();
        return Ok(item);
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

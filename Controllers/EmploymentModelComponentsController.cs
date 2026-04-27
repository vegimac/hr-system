using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Verwaltung der Komponenten-Zuordnung pro Vertragstyp.
///
/// Endpoints:
///   GET    /api/employment-model-components               – alle (mit Lohnposition-Detail)
///   GET    /api/employment-model-components/{modelCode}   – nur ein Modell (FIX, FIX-M, MTP, UTP)
///   POST   /api/employment-model-components               – neue Zuordnung anlegen
///   PUT    /api/employment-model-components/{id}          – Satz aktualisieren
///   DELETE /api/employment-model-components/{id}          – Soft-Delete (IsActive = false)
///
/// Die Tabelle ist Stammdaten — sie wird in Phase 2 vom PayrollController
/// zum Aufbau des Lohnzettels gelesen.
/// </summary>
[ApiController]
[Route("api/employment-model-components")]
[Authorize]
public class EmploymentModelComponentsController : ControllerBase
{
    private static readonly string[] AllowedModels = { "FIX", "FIX-M", "MTP", "UTP" };

    private readonly AppDbContext _db;
    public EmploymentModelComponentsController(AppDbContext db) => _db = db;

    // GET /api/employment-model-components  – alle, gruppiert nach Model
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _db.EmploymentModelComponents
            .Include(c => c.Lohnposition)
            .OrderBy(c => c.EmploymentModelCode)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Lohnposition!.Code)
            .Select(c => new {
                c.Id,
                c.EmploymentModelCode,
                c.LohnpositionId,
                lohnpositionCode        = c.Lohnposition!.Code,
                lohnpositionBezeichnung = c.Lohnposition.Bezeichnung,
                lohnpositionKategorie   = c.Lohnposition.Kategorie,
                lohnpositionTyp         = c.Lohnposition.Typ,
                c.Rate,
                c.IsActive,
                c.SortOrder,
                c.Bemerkung,
                c.CreatedAt,
                c.UpdatedAt
            })
            .ToListAsync();
        return Ok(list);
    }

    // GET /api/employment-model-components/FIX
    [HttpGet("{modelCode}")]
    public async Task<IActionResult> GetByModel(string modelCode)
    {
        modelCode = modelCode.ToUpperInvariant();
        if (!AllowedModels.Contains(modelCode))
            return BadRequest(new { error = $"Unbekannter Vertragstyp '{modelCode}'. Erlaubt: {string.Join(", ", AllowedModels)}" });

        var list = await _db.EmploymentModelComponents
            .Include(c => c.Lohnposition)
            .Where(c => c.EmploymentModelCode == modelCode)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Lohnposition!.Code)
            .Select(c => new {
                c.Id,
                c.EmploymentModelCode,
                c.LohnpositionId,
                lohnpositionCode        = c.Lohnposition!.Code,
                lohnpositionBezeichnung = c.Lohnposition.Bezeichnung,
                lohnpositionKategorie   = c.Lohnposition.Kategorie,
                lohnpositionTyp         = c.Lohnposition.Typ,
                c.Rate,
                c.IsActive,
                c.SortOrder,
                c.Bemerkung,
                c.CreatedAt,
                c.UpdatedAt
            })
            .ToListAsync();
        return Ok(list);
    }

    // POST /api/employment-model-components
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EmploymentModelComponentDto dto)
    {
        var modelCode = dto.EmploymentModelCode?.ToUpperInvariant() ?? "";
        if (!AllowedModels.Contains(modelCode))
            return BadRequest(new { error = $"Unbekannter Vertragstyp '{modelCode}'. Erlaubt: {string.Join(", ", AllowedModels)}" });

        var lp = await _db.Lohnpositionen.FindAsync(dto.LohnpositionId);
        if (lp is null)
            return BadRequest(new { error = $"Lohnposition id={dto.LohnpositionId} nicht gefunden." });

        // Unique-Check: gleiche (Model, Lohnposition)-Kombination bereits vorhanden?
        var existing = await _db.EmploymentModelComponents
            .FirstOrDefaultAsync(c => c.EmploymentModelCode == modelCode
                                   && c.LohnpositionId == dto.LohnpositionId);
        if (existing is not null)
            return Conflict(new { error = $"Lohnposition '{lp.Code}' ist für {modelCode} bereits zugeordnet." });

        var entity = new EmploymentModelComponent
        {
            EmploymentModelCode = modelCode,
            LohnpositionId      = dto.LohnpositionId,
            Rate                = dto.Rate,
            IsActive            = dto.IsActive,
            SortOrder           = dto.SortOrder,
            Bemerkung           = dto.Bemerkung,
            CreatedAt           = DateTime.UtcNow,
            UpdatedAt           = DateTime.UtcNow,
        };
        _db.EmploymentModelComponents.Add(entity);
        await _db.SaveChangesAsync();

        return Ok(new { id = entity.Id });
    }

    // PUT /api/employment-model-components/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] EmploymentModelComponentDto dto)
    {
        var entity = await _db.EmploymentModelComponents.FindAsync(id);
        if (entity is null) return NotFound();

        // Model-Code wird NICHT geändert (wäre semantisch eine Neu-Zuordnung).
        // Nur Rate, IsActive, SortOrder, Bemerkung sind editierbar.
        entity.Rate      = dto.Rate;
        entity.IsActive  = dto.IsActive;
        entity.SortOrder = dto.SortOrder;
        entity.Bemerkung = dto.Bemerkung;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { id = entity.Id });
    }

    // DELETE /api/employment-model-components/{id} – soft-delete (IsActive=false)
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _db.EmploymentModelComponents.FindAsync(id);
        if (entity is null) return NotFound();
        entity.IsActive  = false;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record EmploymentModelComponentDto(
    string  EmploymentModelCode,
    int     LohnpositionId,
    decimal? Rate,
    bool    IsActive,
    int     SortOrder,
    string? Bemerkung);

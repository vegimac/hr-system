using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/companyprofiles/{companyProfileId:int}/deductions")]
public class DeductionRulesController : ControllerBase
{
    private readonly AppDbContext _db;
    public DeductionRulesController(AppDbContext db) => _db = db;

    // GET – alle aktiven Regeln, gruppiert nach Kategorie
    [HttpGet]
    public async Task<IActionResult> GetAll(int companyProfileId)
    {
        var rules = await _db.DeductionRules
            .Where(r => r.CompanyProfileId == companyProfileId && r.IsActive)
            .OrderBy(r => r.CategoryCode)
            .ThenBy(r => r.SortOrder)
            .ThenBy(r => r.Id)
            .ToListAsync();
        return Ok(rules);
    }

    // POST – neue Regel anlegen
    [HttpPost]
    public async Task<IActionResult> Create(int companyProfileId, [FromBody] DeductionRule dto)
    {
        dto.Id               = 0;
        dto.CompanyProfileId = companyProfileId;
        dto.IsActive         = true;
        _db.DeductionRules.Add(dto);
        await _db.SaveChangesAsync();
        return Ok(dto);
    }

    // PUT – Regel aktualisieren
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int companyProfileId, int id, [FromBody] DeductionRule dto)
    {
        var rule = await _db.DeductionRules
            .FirstOrDefaultAsync(r => r.Id == id && r.CompanyProfileId == companyProfileId);
        if (rule is null) return NotFound();

        rule.CategoryCode          = dto.CategoryCode;
        rule.CategoryName          = dto.CategoryName;
        rule.Name                  = dto.Name;
        rule.Type                  = dto.Type;
        rule.Rate                  = dto.Rate;
        rule.BasisType             = dto.BasisType;
        rule.CoordinationDeduction = dto.CoordinationDeduction;
        rule.FreibetragMonthly     = dto.FreibetragMonthly;
        rule.MinAge                = dto.MinAge;
        rule.MaxAge                = dto.MaxAge;
        rule.OnlyQuellensteuer     = dto.OnlyQuellensteuer;
        rule.ValidFrom             = dto.ValidFrom;
        rule.ValidTo               = dto.ValidTo;
        rule.SortOrder             = dto.SortOrder;

        await _db.SaveChangesAsync();
        return Ok(rule);
    }

    // DELETE – soft-delete
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int companyProfileId, int id)
    {
        var rule = await _db.DeductionRules
            .FirstOrDefaultAsync(r => r.Id == id && r.CompanyProfileId == companyProfileId);
        if (rule is null) return NotFound();
        rule.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/companyprofiles/{sourceId}/deductions/copy-to-all
    // Kopiert alle aktiven Regeln dieser Filiale in alle anderen Filialen.
    // Bestehende Regeln der Zielfilialen werden vorher deaktiviert (soft-delete).
    [HttpPost("copy-to-all")]
    public async Task<IActionResult> CopyToAll(int companyProfileId)
    {
        // Quellregeln laden
        var sourceRules = await _db.DeductionRules
            .Where(r => r.CompanyProfileId == companyProfileId && r.IsActive)
            .OrderBy(r => r.SortOrder)
            .ToListAsync();

        if (!sourceRules.Any())
            return BadRequest("Keine aktiven Abzugsregeln in der Quellfiliale gefunden.");

        // Alle anderen Filialen
        var targetIds = await _db.CompanyProfiles
            .Where(cp => cp.Id != companyProfileId)
            .Select(cp => cp.Id)
            .ToListAsync();

        if (!targetIds.Any())
            return Ok(new { message = "Keine weiteren Filialen vorhanden.", copied = 0 });

        int copied = 0;

        foreach (var targetId in targetIds)
        {
            // Bestehende Regeln der Zielfiliale deaktivieren
            var existing = await _db.DeductionRules
                .Where(r => r.CompanyProfileId == targetId && r.IsActive)
                .ToListAsync();
            foreach (var e in existing)
                e.IsActive = false;

            // Quellregeln als neue Einträge kopieren
            foreach (var src in sourceRules)
            {
                _db.DeductionRules.Add(new DeductionRule
                {
                    CompanyProfileId    = targetId,
                    CategoryCode        = src.CategoryCode,
                    CategoryName        = src.CategoryName,
                    Name                = src.Name,
                    Type                = src.Type,
                    Rate                = src.Rate,
                    BasisType           = src.BasisType,
                    CoordinationDeduction = src.CoordinationDeduction,
                    FreibetragMonthly   = src.FreibetragMonthly,
                    MinAge              = src.MinAge,
                    MaxAge              = src.MaxAge,
                    OnlyQuellensteuer   = src.OnlyQuellensteuer,
                    ValidFrom           = src.ValidFrom,
                    ValidTo             = src.ValidTo,
                    SortOrder           = src.SortOrder,
                    IsActive            = true,
                });
                copied++;
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message  = $"{sourceRules.Count} Regeln in {targetIds.Count} Filialen kopiert ({copied} neue Einträge).",
            source   = companyProfileId,
            targets  = targetIds.Count,
            rules    = sourceRules.Count,
            copied
        });
    }
}

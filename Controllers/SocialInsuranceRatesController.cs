using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/social-insurance-rates")]
[Authorize]
public class SocialInsuranceRatesController : ControllerBase
{
    private readonly AppDbContext _db;
    public SocialInsuranceRatesController(AppDbContext db) => _db = db;

    // GET – alle Sätze (aktiv + inaktiv), sortiert
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var rates = await _db.SocialInsuranceRates
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Code)
            .ThenBy(r => r.ValidFrom)
            .ToListAsync();
        return Ok(rates);
    }

    // GET – nur aktuell gültige Sätze für ein bestimmtes Datum
    [HttpGet("effective")]
    public async Task<IActionResult> GetEffective([FromQuery] DateOnly? date)
    {
        var refDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var rates = await _db.SocialInsuranceRates
            .Where(r => r.IsActive
                     && r.ValidFrom <= refDate
                     && (r.ValidTo == null || r.ValidTo >= refDate))
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Code)
            .ToListAsync();
        return Ok(rates);
    }

    // POST – neuen Satz anlegen
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SocialInsuranceRate dto)
    {
        // Duplikat-Schutz: kein zweiter Eintrag mit denselben Schlüsselfeldern
        // und identischem Gültig-ab-Datum. Verhindert, dass durch Doppelklick
        // im Admin-UI oder paralleles Bearbeiten zwei "gleiche" Sätze entstehen,
        // die der PayrollController sonst zur Laufzeit deduplizieren muss.
        var duplicate = await _db.SocialInsuranceRates.AnyAsync(r =>
                r.Code == dto.Code
             && r.MinAge == dto.MinAge
             && r.MaxAge == dto.MaxAge
             && r.EmploymentModelCode == dto.EmploymentModelCode
             && r.OnlyQuellensteuer == dto.OnlyQuellensteuer
             && r.BasisType == dto.BasisType
             && r.ValidFrom == dto.ValidFrom);
        if (duplicate)
            return Conflict(new {
                error = $"Ein SV-Satz '{dto.Code}' mit gleichem Filter und Gültig-ab {dto.ValidFrom:yyyy-MM-dd} existiert bereits."
            });

        dto.Id        = 0;
        dto.IsActive  = true;
        dto.CreatedAt = DateTime.UtcNow;
        _db.SocialInsuranceRates.Add(dto);
        await _db.SaveChangesAsync();
        return Ok(dto);
    }

    // PUT – Satz aktualisieren
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SocialInsuranceRate dto)
    {
        var rate = await _db.SocialInsuranceRates.FindAsync(id);
        if (rate is null) return NotFound();

        rate.Code                  = dto.Code;
        rate.Name                  = dto.Name;
        rate.Description           = dto.Description;
        rate.Rate                  = dto.Rate;
        rate.BasisType             = dto.BasisType;
        rate.EmploymentModelCode   = dto.EmploymentModelCode;
        rate.MinAge                = dto.MinAge;
        rate.MaxAge                = dto.MaxAge;
        rate.FreibetragMonthly     = dto.FreibetragMonthly;
        rate.CoordinationDeduction = dto.CoordinationDeduction;
        rate.OnlyQuellensteuer     = dto.OnlyQuellensteuer;
        rate.ValidFrom             = dto.ValidFrom;
        rate.ValidTo               = dto.ValidTo;
        rate.SortOrder             = dto.SortOrder;
        rate.IsActive              = dto.IsActive;

        await _db.SaveChangesAsync();
        return Ok(rate);
    }

    // DELETE – soft-delete
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var rate = await _db.SocialInsuranceRates.FindAsync(id);
        if (rate is null) return NotFound();
        rate.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

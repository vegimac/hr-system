using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

// ============================================================================
// Kommunaler/städtischer Mindestlohn pro Filiale (Walter-Vorgabe 23.05.2026).
// Erfasst wird der JAHRESLOHN; Monats- (/13) und Stundenlohn (/52/Wochenstunden
// der Filiale) werden berechnet und mitgeliefert. Versioniert über ValidFrom/
// ValidTo (Generationen) — „Neue Version ab" begrenzt den Vorgänger.
//
// Katalog-/Stammdaten (kein MA-Lohn) → im EditLock-Audit whitelisted. Der
// eigentliche Mindestlohn-Vergleich (max(L-GAV, Filial-Floor)) passiert in
// MinimumWageCheckService.CheckAsync.
// ============================================================================
[ApiController]
[Route("api/branch-min-wage")]
[Authorize(Roles = "admin,superuser")]
public class BranchMinWageController : ControllerBase
{
    private readonly AppDbContext _db;
    public BranchMinWageController(AppDbContext db) => _db = db;

    private async Task<decimal> WeeklyHoursAsync(int companyProfileId)
    {
        var w = (await _db.CompanyProfiles.Where(c => c.Id == companyProfileId)
            .Select(c => c.NormalWeeklyHours).FirstOrDefaultAsync()) ?? 42m;
        return w <= 0 ? 42m : w;
    }

    // GET /api/branch-min-wage?companyProfileId=123 — alle Versionen der Filiale
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int companyProfileId)
    {
        if (companyProfileId <= 0) return BadRequest(new { error = "companyProfileId fehlt." });
        var weekly = await WeeklyHoursAsync(companyProfileId);

        var rows = await _db.BranchMinWages
            .Where(b => b.CompanyProfileId == companyProfileId)
            .OrderBy(b => b.ValidFrom)
            .ToListAsync();

        var items = rows.Select(b => new
        {
            b.Id,
            b.CompanyProfileId,
            b.AnnualSalary,
            b.AppliesToYouth,
            b.ValidFrom,
            b.ValidTo,
            b.IsActive,
            monthly = Math.Round(b.AnnualSalary / 13m, 2),         // 100 % Monatslohn
            hourly  = Math.Round(b.AnnualSalary / 52m / weekly, 2)
        });
        return Ok(new { weeklyHours = weekly, items });
    }

    // POST /api/branch-min-wage — neue Version (begrenzt den offenen Vorgänger)
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] BranchMinWageDto dto)
    {
        if (dto.CompanyProfileId <= 0) return BadRequest(new { error = "companyProfileId fehlt." });
        if (dto.AnnualSalary <= 0)     return BadRequest(new { error = "Jahreslohn muss grösser als 0 sein." });

        var vf = dto.ValidFrom.ToDateTime(TimeOnly.MinValue);

        bool dup = await _db.BranchMinWages.AnyAsync(b =>
            b.CompanyProfileId == dto.CompanyProfileId && b.ValidFrom == vf);
        if (dup) return Conflict(new { error = $"Es existiert bereits eine Version ab {dto.ValidFrom:dd.MM.yyyy}." });

        // Offenen Vorgänger (valid_to == NULL, vor dem neuen Datum) begrenzen.
        var prev = await _db.BranchMinWages
            .Where(b => b.CompanyProfileId == dto.CompanyProfileId && b.IsActive && b.ValidTo == null && b.ValidFrom < vf)
            .OrderByDescending(b => b.ValidFrom)
            .FirstOrDefaultAsync();
        if (prev != null) prev.ValidTo = vf.AddDays(-1);

        var row = new BranchMinWage
        {
            CompanyProfileId = dto.CompanyProfileId,
            AnnualSalary     = dto.AnnualSalary,
            AppliesToYouth   = dto.AppliesToYouth,
            ValidFrom        = vf,
            ValidTo          = null,
            IsActive         = true,
            CreatedAt        = DateTime.UtcNow
        };
        _db.BranchMinWages.Add(row);
        await _db.SaveChangesAsync();
        return Ok(new { row.Id });
    }

    // PUT /api/branch-min-wage/{id} — Version korrigieren (Betrag/Jugend/Datum)
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] BranchMinWageDto dto)
    {
        var row = await _db.BranchMinWages.FindAsync(id);
        if (row == null) return NotFound();
        if (dto.AnnualSalary <= 0) return BadRequest(new { error = "Jahreslohn muss grösser als 0 sein." });

        row.AnnualSalary   = dto.AnnualSalary;
        row.AppliesToYouth = dto.AppliesToYouth;
        if (dto.ValidFrom != default) row.ValidFrom = dto.ValidFrom.ToDateTime(TimeOnly.MinValue);
        await _db.SaveChangesAsync();
        return Ok(new { row.Id });
    }

    // DELETE /api/branch-min-wage/{id} — Version entfernen
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await _db.BranchMinWages.FindAsync(id);
        if (row == null) return NotFound();
        _db.BranchMinWages.Remove(row);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record BranchMinWageDto(int CompanyProfileId, decimal AnnualSalary, bool AppliesToYouth, DateOnly ValidFrom);

using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// CRUD für Familienzulagen pro Familienmitglied. Eine Zulage hat
/// Von/Bis-Datum und einen monatlichen Betrag. Bei einer Änderung
/// (z.B. Lebensstufen-Wechsel KZ → AZ) legt Walter einen neuen Eintrag
/// an statt zu überschreiben — so bleibt die Historie erhalten.
/// </summary>
[Authorize]
[ApiController]
[Route("api/family-members/{familyMemberId:int}/allowances")]
public class FamilyMemberAllowancesController : ControllerBase
{
    private readonly AppDbContext _db;
    public FamilyMemberAllowancesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(int familyMemberId)
    {
        var list = await _db.FamilyMemberAllowances
            .Where(a => a.FamilyMemberId == familyMemberId)
            .OrderByDescending(a => a.ValidFrom)
            .Select(a => MapToDto(a))
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int familyMemberId, [FromBody] AllowanceDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(new { error = err });

        var memberExists = await _db.EmployeeFamilyMembers.AnyAsync(m => m.Id == familyMemberId);
        if (!memberExists) return NotFound(new { error = "Familienmitglied nicht gefunden." });

        var entry = new FamilyMemberAllowance
        {
            FamilyMemberId = familyMemberId,
            ValidFrom      = dto.ValidFrom!.Value,
            ValidTo        = dto.ValidTo,
            MonthlyAmount  = dto.MonthlyAmount ?? 0m,
            AllowanceType  = string.IsNullOrWhiteSpace(dto.AllowanceType) ? null : dto.AllowanceType.Trim().ToUpperInvariant(),
            Note           = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow
        };
        _db.FamilyMemberAllowances.Add(entry);
        await _db.SaveChangesAsync();
        return Ok(MapToDto(entry));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int familyMemberId, int id, [FromBody] AllowanceDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(new { error = err });

        var entry = await _db.FamilyMemberAllowances
            .FirstOrDefaultAsync(a => a.Id == id && a.FamilyMemberId == familyMemberId);
        if (entry is null) return NotFound();

        entry.ValidFrom     = dto.ValidFrom!.Value;
        entry.ValidTo       = dto.ValidTo;
        entry.MonthlyAmount = dto.MonthlyAmount ?? 0m;
        entry.AllowanceType = string.IsNullOrWhiteSpace(dto.AllowanceType) ? null : dto.AllowanceType.Trim().ToUpperInvariant();
        entry.Note          = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        entry.UpdatedAt     = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(MapToDto(entry));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int familyMemberId, int id)
    {
        var entry = await _db.FamilyMemberAllowances
            .FirstOrDefaultAsync(a => a.Id == id && a.FamilyMemberId == familyMemberId);
        if (entry is null) return NotFound();
        _db.FamilyMemberAllowances.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Helpers ────────────────────────────────────────────────────────
    private static string? Validate(AllowanceDto dto)
    {
        if (dto.ValidFrom is null) return "Gültig ab ist Pflicht.";
        if (dto.ValidTo.HasValue && dto.ValidTo.Value < dto.ValidFrom.Value)
            return "Gültig bis darf nicht vor Gültig ab liegen.";
        if (dto.MonthlyAmount.HasValue && dto.MonthlyAmount.Value < 0)
            return "Monatsbetrag darf nicht negativ sein.";
        return null;
    }

    private static object MapToDto(FamilyMemberAllowance a) => new
    {
        id             = a.Id,
        familyMemberId = a.FamilyMemberId,
        validFrom      = a.ValidFrom.ToString("yyyy-MM-dd"),
        validTo        = a.ValidTo?.ToString("yyyy-MM-dd"),
        monthlyAmount  = a.MonthlyAmount,
        allowanceType  = a.AllowanceType,
        note           = a.Note,
        createdAt      = a.CreatedAt,
        updatedAt      = a.UpdatedAt
    };
}

public record AllowanceDto(
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    decimal?  MonthlyAmount,
    string?   AllowanceType,
    string?   Note
);

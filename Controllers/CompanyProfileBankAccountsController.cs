using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Bankverbindungen pro Filiale (mit Historie). Pendant zu
/// <see cref="EmployeeBankAccountsController"/> auf Filial-Ebene. Beim
/// Lohnlauf-DTA wird der Eintrag verwendet, der in der Lohnperiode gültig
/// ist und IsMain=true hat.
/// </summary>
[Authorize]
[ApiController]
[Route("api/company-profile-bank-accounts")]
public class CompanyProfileBankAccountsController : ControllerBase
{
    private readonly AppDbContext _db;
    public CompanyProfileBankAccountsController(AppDbContext db) => _db = db;

    // GET /api/company-profile-bank-accounts/company/{id}
    [HttpGet("company/{companyProfileId:int}")]
    public async Task<IActionResult> GetByCompany(int companyProfileId)
    {
        var list = await _db.CompanyProfileBankAccounts
            .Where(b => b.CompanyProfileId == companyProfileId)
            .OrderByDescending(b => b.IsMain)
            .ThenByDescending(b => b.ValidFrom)
            .Select(b => MapToDto(b))
            .ToListAsync();
        return Ok(list);
    }

    // POST /api/company-profile-bank-accounts
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CompanyProfileBankAccountDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(new { message = err });

        var entry = new CompanyProfileBankAccount
        {
            CompanyProfileId = dto.CompanyProfileId,
            Iban             = NormalizeIban(dto.Iban)!,
            Bic              = NormalizeBic(dto.Bic),
            BankName         = dto.BankName?.Trim(),
            IsMain           = dto.IsMain ?? true,
            Bemerkung        = dto.Bemerkung?.Trim(),
            ValidFrom        = DateOnly.Parse(dto.ValidFrom),
            ValidTo          = string.IsNullOrWhiteSpace(dto.ValidTo) ? null : DateOnly.Parse(dto.ValidTo),
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow
        };
        await EnforceSingleMain(entry);
        _db.CompanyProfileBankAccounts.Add(entry);
        await _db.SaveChangesAsync();
        return Ok(MapToDto(entry));
    }

    // PUT /api/company-profile-bank-accounts/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CompanyProfileBankAccountDto dto)
    {
        var entry = await _db.CompanyProfileBankAccounts.FindAsync(id);
        if (entry == null) return NotFound();

        var err = Validate(dto);
        if (err != null) return BadRequest(new { message = err });

        entry.Iban       = NormalizeIban(dto.Iban)!;
        entry.Bic        = NormalizeBic(dto.Bic);
        entry.BankName   = dto.BankName?.Trim();
        entry.IsMain     = dto.IsMain ?? entry.IsMain;
        entry.Bemerkung  = dto.Bemerkung?.Trim();
        entry.ValidFrom  = DateOnly.Parse(dto.ValidFrom);
        entry.ValidTo    = string.IsNullOrWhiteSpace(dto.ValidTo) ? null : DateOnly.Parse(dto.ValidTo);
        entry.UpdatedAt  = DateTime.UtcNow;

        await EnforceSingleMain(entry);
        await _db.SaveChangesAsync();
        return Ok(MapToDto(entry));
    }

    // DELETE /api/company-profile-bank-accounts/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await _db.CompanyProfileBankAccounts.FindAsync(id);
        if (entry == null) return NotFound();
        _db.CompanyProfileBankAccounts.Remove(entry);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string? Validate(CompanyProfileBankAccountDto dto)
    {
        if (dto.CompanyProfileId <= 0) return "CompanyProfileId fehlt.";
        if (string.IsNullOrWhiteSpace(dto.Iban)) return "IBAN ist erforderlich.";
        if (string.IsNullOrWhiteSpace(dto.ValidFrom)) return "'Gültig ab' ist erforderlich.";
        if (!DateOnly.TryParse(dto.ValidFrom, out var from)) return "'Gültig ab' ungültig.";
        if (!string.IsNullOrWhiteSpace(dto.ValidTo))
        {
            if (!DateOnly.TryParse(dto.ValidTo, out var to)) return "'Gültig bis' ungültig.";
            if (to < from) return "'Gültig bis' muss nach 'Gültig ab' liegen.";
        }
        return null;
    }

    /// <summary>
    /// Wenn der aktuelle Eintrag IsMain ist: alle anderen derselben Filiale
    /// entmarkieren. Garantiert "max. eine Hauptbank pro Filiale" pro Zeitpunkt.
    /// </summary>
    private async Task EnforceSingleMain(CompanyProfileBankAccount current)
    {
        if (!current.IsMain) return;
        var others = await _db.CompanyProfileBankAccounts
            .Where(b => b.CompanyProfileId == current.CompanyProfileId
                     && b.Id != current.Id
                     && b.IsMain)
            .ToListAsync();
        foreach (var o in others)
        {
            o.IsMain    = false;
            o.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static string? NormalizeIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return null;
        return iban.Replace(" ", "").ToUpperInvariant();
    }
    private static string? NormalizeBic(string? bic)
    {
        if (string.IsNullOrWhiteSpace(bic)) return null;
        return bic.Replace(" ", "").ToUpperInvariant();
    }

    private static object MapToDto(CompanyProfileBankAccount b) => new
    {
        id               = b.Id,
        companyProfileId = b.CompanyProfileId,
        iban             = b.Iban,
        bic              = b.Bic,
        bankName         = b.BankName,
        isMain           = b.IsMain,
        bemerkung        = b.Bemerkung,
        validFrom        = b.ValidFrom.ToString("yyyy-MM-dd"),
        validTo          = b.ValidTo?.ToString("yyyy-MM-dd"),
        createdAt        = b.CreatedAt
    };
}

public record CompanyProfileBankAccountDto(
    int     CompanyProfileId,
    string  Iban,
    string? Bic,
    string? BankName,
    bool?   IsMain,
    string? Bemerkung,
    string  ValidFrom,
    string? ValidTo
);

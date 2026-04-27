using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Bankverbindungen pro Mitarbeiter (mit Historie).
/// </summary>
[Authorize]
[ApiController]
[Route("api/employee-bank-accounts")]
public class EmployeeBankAccountsController : ControllerBase
{
    private readonly AppDbContext _db;
    public EmployeeBankAccountsController(AppDbContext db) => _db = db;

    // GET /api/employee-bank-accounts/employee/{id}
    [HttpGet("employee/{employeeId:int}")]
    public async Task<IActionResult> GetByEmployee(int employeeId)
    {
        var list = await _db.EmployeeBankAccounts
            .Where(b => b.EmployeeId == employeeId)
            .OrderByDescending(b => b.ValidFrom)
            .Select(b => MapToDto(b))
            .ToListAsync();
        return Ok(list);
    }

    // POST /api/employee-bank-accounts
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EmployeeBankAccountDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(new { message = err });

        var entry = new EmployeeBankAccount
        {
            EmployeeId       = dto.EmployeeId,
            Iban             = NormalizeIban(dto.Iban)!,
            Bic              = NormalizeBic(dto.Bic),
            BankName         = dto.BankName?.Trim(),
            Kontoinhaber     = dto.Kontoinhaber?.Trim(),
            Zahlungsreferenz = dto.Zahlungsreferenz?.Trim(),
            Bemerkung        = dto.Bemerkung?.Trim(),
            IsHauptbank      = dto.IsHauptbank ?? true,
            AufteilungTyp    = NormalizeAufteilungTyp(dto.AufteilungTyp),
            AufteilungWert   = dto.AufteilungWert,
            ValidFrom        = DateOnly.Parse(dto.ValidFrom),
            ValidTo          = string.IsNullOrWhiteSpace(dto.ValidTo) ? null : DateOnly.Parse(dto.ValidTo),
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow
        };
        await EnforceSingleHauptbank(entry);
        _db.EmployeeBankAccounts.Add(entry);
        await _db.SaveChangesAsync();
        return Ok(MapToDto(entry));
    }

    // PUT /api/employee-bank-accounts/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] EmployeeBankAccountDto dto)
    {
        var entry = await _db.EmployeeBankAccounts.FindAsync(id);
        if (entry == null) return NotFound();

        var err = Validate(dto);
        if (err != null) return BadRequest(new { message = err });

        entry.Iban             = NormalizeIban(dto.Iban)!;
        entry.Bic              = NormalizeBic(dto.Bic);
        entry.BankName         = dto.BankName?.Trim();
        entry.Kontoinhaber     = dto.Kontoinhaber?.Trim();
        entry.Zahlungsreferenz = dto.Zahlungsreferenz?.Trim();
        entry.Bemerkung        = dto.Bemerkung?.Trim();
        entry.IsHauptbank      = dto.IsHauptbank ?? entry.IsHauptbank;
        entry.AufteilungTyp    = NormalizeAufteilungTyp(dto.AufteilungTyp);
        entry.AufteilungWert   = dto.AufteilungWert;
        entry.ValidFrom        = DateOnly.Parse(dto.ValidFrom);
        entry.ValidTo          = string.IsNullOrWhiteSpace(dto.ValidTo) ? null : DateOnly.Parse(dto.ValidTo);
        entry.UpdatedAt        = DateTime.UtcNow;

        await EnforceSingleHauptbank(entry);
        await _db.SaveChangesAsync();
        return Ok(MapToDto(entry));
    }

    // DELETE /api/employee-bank-accounts/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await _db.EmployeeBankAccounts.FindAsync(id);
        if (entry == null) return NotFound();
        _db.EmployeeBankAccounts.Remove(entry);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private static readonly string[] _aufteilungTypen = { "VOLL", "FIXBETRAG", "PROZENT", "NETTO_ABZUEGLICH" };

    private static string? Validate(EmployeeBankAccountDto dto)
    {
        if (dto.EmployeeId <= 0) return "EmployeeId fehlt.";
        if (string.IsNullOrWhiteSpace(dto.Iban)) return "IBAN ist erforderlich.";
        if (string.IsNullOrWhiteSpace(dto.ValidFrom)) return "'Gültig ab' ist erforderlich.";
        if (!DateOnly.TryParse(dto.ValidFrom, out var from)) return "'Gültig ab' ungültig.";
        if (!string.IsNullOrWhiteSpace(dto.ValidTo))
        {
            if (!DateOnly.TryParse(dto.ValidTo, out var to)) return "'Gültig bis' ungültig.";
            if (to < from) return "'Gültig bis' muss nach 'Gültig ab' liegen.";
        }
        var typ = NormalizeAufteilungTyp(dto.AufteilungTyp);
        if (!_aufteilungTypen.Contains(typ))
            return $"Aufteilung-Typ ungültig. Erlaubt: {string.Join(", ", _aufteilungTypen)}.";
        if (typ != "VOLL")
        {
            if (dto.AufteilungWert is null || dto.AufteilungWert <= 0)
                return "Bei FIXBETRAG/PROZENT/NETTO_ABZUEGLICH ist ein Wert > 0 erforderlich.";
            if (typ == "PROZENT" && dto.AufteilungWert > 100)
                return "Prozent-Wert darf max. 100 sein.";
        }
        return null;
    }

    private static string NormalizeAufteilungTyp(string? typ)
        => string.IsNullOrWhiteSpace(typ) ? "VOLL" : typ.Trim().ToUpperInvariant();

    /// <summary>
    /// Wenn der aktuelle Eintrag Hauptbank ist: alle anderen desselben MA
    /// entmarkieren. Garantiert "max. eine Hauptbank pro MA" pro Zeitpunkt.
    /// </summary>
    private async Task EnforceSingleHauptbank(EmployeeBankAccount current)
    {
        if (!current.IsHauptbank) return;
        var others = await _db.EmployeeBankAccounts
            .Where(b => b.EmployeeId == current.EmployeeId
                     && b.Id != current.Id
                     && b.IsHauptbank)
            .ToListAsync();
        foreach (var o in others)
        {
            o.IsHauptbank = false;
            o.UpdatedAt   = DateTime.UtcNow;
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

    private static object MapToDto(EmployeeBankAccount b) => new
    {
        id               = b.Id,
        employeeId       = b.EmployeeId,
        iban             = b.Iban,
        bic              = b.Bic,
        bankName         = b.BankName,
        kontoinhaber     = b.Kontoinhaber,
        zahlungsreferenz = b.Zahlungsreferenz,
        bemerkung        = b.Bemerkung,
        isHauptbank      = b.IsHauptbank,
        aufteilungTyp    = b.AufteilungTyp,
        aufteilungWert   = b.AufteilungWert,
        validFrom        = b.ValidFrom.ToString("yyyy-MM-dd"),
        validTo          = b.ValidTo?.ToString("yyyy-MM-dd"),
        createdAt        = b.CreatedAt
    };
}

public record EmployeeBankAccountDto(
    int    EmployeeId,
    string Iban,
    string? Bic,
    string? BankName,
    string? Kontoinhaber,
    string? Zahlungsreferenz,
    string? Bemerkung,
    bool?   IsHauptbank,
    string? AufteilungTyp,
    decimal? AufteilungWert,
    string ValidFrom,
    string? ValidTo
);

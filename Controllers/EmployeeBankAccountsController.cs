using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Bankverbindungen pro Mitarbeiter (mit Historie).
///
/// Walter-Vorgabe 17.05.2026: Sobald eine Bankverbindung in einem Lohnlauf
/// verwendet wurde (= ValidFrom liegt vor dem FirstAllowedDate der Filiale),
/// darf sie nicht mehr editiert oder gelöscht werden. Stattdessen muss eine
/// NEUE Bankverbindung mit aktuellem ValidFrom erfasst werden — die alte
/// bleibt unverändert bestehen.
/// </summary>
[Authorize]
[ApiController]
[Route("api/employee-bank-accounts")]
public class EmployeeBankAccountsController : ControllerBase
{
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;
    public EmployeeBankAccountsController(AppDbContext db, LohnEditLockService editLock)
    {
        _db       = db;
        _editLock = editLock;
    }

    /// <summary>
    /// Filiale des MA (jüngster aktiver Vertrag) — null wenn kein Vertrag.
    /// </summary>
    private Task<int?> GetEmployeeBranchAsync(int employeeId)
        => _db.Employees
            .Where(e => e.Id == employeeId)
            .SelectMany(e => e.Employments)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => (int?)x.CompanyProfileId)
            .FirstOrDefaultAsync();

    /// <summary>
    /// Prüft ob die Bankverbindung b „in Lohn verwendet" wurde:
    /// b.ValidFrom liegt vor dem FirstAllowedDate der Filiale.
    /// Für admin/superuser ist FirstAllowedDate null → immer false (Bypass).
    /// </summary>
    private Task<bool> IsInLohnVerwendetAsync(EmployeeBankAccount b, DateOnly? firstAllowed)
    {
        if (firstAllowed is null) return Task.FromResult(false);
        return Task.FromResult(b.ValidFrom < firstAllowed.Value);
    }

    // GET /api/employee-bank-accounts/employee/{id}
    [HttpGet("employee/{employeeId:int}")]
    public async Task<IActionResult> GetByEmployee(int employeeId)
    {
        var branchId      = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed  = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;

        var entries = await _db.EmployeeBankAccounts
            .Where(b => b.EmployeeId == employeeId)
            .OrderByDescending(b => b.ValidFrom)
            .ToListAsync();

        var list = entries.Select(b => MapToDto(b, firstAllowed)).ToList();
        return Ok(list);
    }

    /// <summary>
    /// Liefert die IDs aller Mitarbeiter mit einer per heute gültigen
    /// Bankverbindung — Frontend nutzt das Komplement für den Filter
    /// "MA ohne Bankverbindung" auf der Mitarbeiter-Maske.
    /// </summary>
    // GET /api/employee-bank-accounts/active-employee-ids
    [HttpGet("active-employee-ids")]
    public async Task<IActionResult> GetActiveEmployeeIds()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var ids = await _db.EmployeeBankAccounts
            .Where(b => b.ValidFrom <= today
                     && (b.ValidTo == null || b.ValidTo >= today))
            .Select(b => b.EmployeeId)
            .Distinct()
            .ToListAsync();
        return Ok(ids);
    }

    // POST /api/employee-bank-accounts
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EmployeeBankAccountDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(new { message = err });

        // Walter-Vorgabe 17.05.2026: eine neue Bankverbindung darf nicht
        // rückwirkend in einer Periode beginnen, die schon in Verarbeitung
        // ist. Der "Neu ab"-Pfad muss zum frühesten erlaubten Tag der Filiale
        // passen oder später. admin/superuser werden im Service bypassed.
        var newFrom      = DateOnly.Parse(dto.ValidFrom);
        var branchId     = await GetEmployeeBranchAsync(dto.EmployeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;
        if (firstAllowed.HasValue && newFrom < firstAllowed.Value)
        {
            return Conflict(new
            {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"'Gültig ab {newFrom:dd.MM.yyyy}' liegt in einer bereits in Verarbeitung befindlichen Lohnperiode. " +
                                   $"Frühestes erlaubtes 'Gültig ab': {firstAllowed.Value:dd.MM.yyyy}.",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });
        }

        // Walter-Vorgabe 18.05.2026: wenn der MA noch keine andere als
        // Hauptbank markierte aktive Bank hat, ist die neue Bank
        // automatisch Hauptbank — auch wenn dto.IsHauptbank false ist.
        // Verhindert die "Phantom-Bank ohne Markierung"-Falle, die im DTA
        // und in der Akonto-Liste zu fehlenden IBANs führt.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var hasOtherHauptbank = await _db.EmployeeBankAccounts.AnyAsync(b =>
            b.EmployeeId == dto.EmployeeId
            && b.IsHauptbank
            && b.ValidFrom <= today
            && (b.ValidTo == null || b.ValidTo >= today));
        var resolvedIsHauptbank = dto.IsHauptbank ?? !hasOtherHauptbank;
        if (!hasOtherHauptbank) resolvedIsHauptbank = true;

        var entry = new EmployeeBankAccount
        {
            EmployeeId           = dto.EmployeeId,
            Iban                 = NormalizeIban(dto.Iban)!,
            Bic                  = NormalizeBic(dto.Bic),
            BankName             = dto.BankName?.Trim(),
            Kontoinhaber         = dto.Kontoinhaber?.Trim(),
            KontoinhaberStrasse  = dto.KontoinhaberStrasse?.Trim(),
            KontoinhaberPlz      = dto.KontoinhaberPlz?.Trim(),
            KontoinhaberOrt      = dto.KontoinhaberOrt?.Trim(),
            KontoinhaberLand     = NormalizeCountry(dto.KontoinhaberLand),
            Zahlungsreferenz     = dto.Zahlungsreferenz?.Trim(),
            Bemerkung            = dto.Bemerkung?.Trim(),
            IsHauptbank      = resolvedIsHauptbank,
            AufteilungTyp    = NormalizeAufteilungTyp(dto.AufteilungTyp),
            AufteilungWert   = dto.AufteilungWert,
            ValidFrom        = DateOnly.Parse(dto.ValidFrom),
            ValidTo          = string.IsNullOrWhiteSpace(dto.ValidTo) ? null : DateOnly.Parse(dto.ValidTo),
            CreatedAt        = DateTime.Now,
            UpdatedAt        = DateTime.Now
        };
        // Walter-Vorgabe 07.06.2026: Auto-Close — bestehende Vorgänger schliessen,
        // wenn die neue Verbindung als Voll-/Hauptkonto eintritt. Bei einer
        // bewussten Aufteilung (mehrere Konten gleichzeitig aktiv mit
        // AufteilungTyp != VOLL) NICHT eingreifen. Gilt für jede Quelle —
        // manuelle Pflege UND CSV-Import.
        if (entry.IsHauptbank && string.Equals(entry.AufteilungTyp, "VOLL", StringComparison.OrdinalIgnoreCase))
        {
            var vorgaenger = await _db.EmployeeBankAccounts
                .Where(b => b.EmployeeId == dto.EmployeeId
                         && b.ValidFrom < newFrom
                         && (b.ValidTo == null || b.ValidTo >= newFrom))
                .ToListAsync();
            foreach (var p in vorgaenger)
            {
                p.ValidTo     = newFrom.AddDays(-1);
                p.IsHauptbank = false;
                p.UpdatedAt   = DateTime.Now;
            }
        }

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

        // Lohnlauf-Schutz: Bankverbindung darf nicht editiert werden, wenn sie
        // schon in einem Lohnlauf verwendet wurde. Stattdessen: neue Bankver-
        // bindung mit aktuellem ValidFrom erfassen.
        var branchId     = await GetEmployeeBranchAsync(entry.EmployeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;
        if (await IsInLohnVerwendetAsync(entry, firstAllowed))
        {
            return Conflict(new
            {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Diese Bankverbindung wurde bereits in einem Lohnlauf verwendet (gültig ab {entry.ValidFrom:dd.MM.yyyy}). " +
                                   $"Bitte über '+ Neue Bankverbindung' eine neue Verbindung erfassen.",
                firstAllowedDate = firstAllowed?.ToString("yyyy-MM-dd")
            });
        }

        var err = Validate(dto);
        if (err != null) return BadRequest(new { message = err });

        entry.Iban                 = NormalizeIban(dto.Iban)!;
        entry.Bic                  = NormalizeBic(dto.Bic);
        entry.BankName             = dto.BankName?.Trim();
        entry.Kontoinhaber         = dto.Kontoinhaber?.Trim();
        entry.KontoinhaberStrasse  = dto.KontoinhaberStrasse?.Trim();
        entry.KontoinhaberPlz      = dto.KontoinhaberPlz?.Trim();
        entry.KontoinhaberOrt      = dto.KontoinhaberOrt?.Trim();
        entry.KontoinhaberLand     = NormalizeCountry(dto.KontoinhaberLand);
        entry.Zahlungsreferenz     = dto.Zahlungsreferenz?.Trim();
        entry.Bemerkung            = dto.Bemerkung?.Trim();
        entry.IsHauptbank      = dto.IsHauptbank ?? entry.IsHauptbank;
        entry.AufteilungTyp    = NormalizeAufteilungTyp(dto.AufteilungTyp);
        entry.AufteilungWert   = dto.AufteilungWert;
        entry.ValidFrom        = DateOnly.Parse(dto.ValidFrom);
        entry.ValidTo          = string.IsNullOrWhiteSpace(dto.ValidTo) ? null : DateOnly.Parse(dto.ValidTo);
        entry.UpdatedAt        = DateTime.Now;

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

        // Lohnlauf-Schutz: kein Löschen wenn schon in einem Lohnlauf verwendet.
        var branchId     = await GetEmployeeBranchAsync(entry.EmployeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;
        if (await IsInLohnVerwendetAsync(entry, firstAllowed))
        {
            return Conflict(new
            {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Diese Bankverbindung wurde bereits in einem Lohnlauf verwendet (gültig ab {entry.ValidFrom:dd.MM.yyyy}) und kann nicht gelöscht werden.",
                firstAllowedDate = firstAllowed?.ToString("yyyy-MM-dd")
            });
        }

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
            o.UpdatedAt   = DateTime.Now;
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
    /// <summary>ISO-3166-1 alpha-2 normalisieren — 2 Buchstaben, gross.</summary>
    private static string? NormalizeCountry(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = code.Trim().ToUpperInvariant();
        return c.Length == 2 ? c : null;
    }

    private static object MapToDto(EmployeeBankAccount b, DateOnly? firstAllowed = null) => new
    {
        id                  = b.Id,
        employeeId          = b.EmployeeId,
        iban                = b.Iban,
        bic                 = b.Bic,
        bankName            = b.BankName,
        kontoinhaber        = b.Kontoinhaber,
        kontoinhaberStrasse = b.KontoinhaberStrasse,
        kontoinhaberPlz     = b.KontoinhaberPlz,
        kontoinhaberOrt     = b.KontoinhaberOrt,
        kontoinhaberLand    = b.KontoinhaberLand,
        zahlungsreferenz    = b.Zahlungsreferenz,
        bemerkung           = b.Bemerkung,
        isHauptbank         = b.IsHauptbank,
        aufteilungTyp       = b.AufteilungTyp,
        aufteilungWert      = b.AufteilungWert,
        validFrom           = b.ValidFrom.ToString("yyyy-MM-dd"),
        validTo             = b.ValidTo?.ToString("yyyy-MM-dd"),
        // True wenn ValidFrom < FirstAllowedDate (Filiale hat schon einen
        // Lohnlauf für diese oder eine spätere Periode laufen lassen). Bei
        // admin/superuser ist FirstAllowedDate null → immer false.
        inLohnVerwendet     = firstAllowed.HasValue && b.ValidFrom < firstAllowed.Value,
        createdAt           = b.CreatedAt
    };
}

public record EmployeeBankAccountDto(
    int    EmployeeId,
    string Iban,
    string? Bic,
    string? BankName,
    string? Kontoinhaber,
    string? KontoinhaberStrasse,
    string? KontoinhaberPlz,
    string? KontoinhaberOrt,
    string? KontoinhaberLand,
    string? Zahlungsreferenz,
    string? Bemerkung,
    bool?   IsHauptbank,
    string? AufteilungTyp,
    decimal? AufteilungWert,
    string ValidFrom,
    string? ValidTo
);

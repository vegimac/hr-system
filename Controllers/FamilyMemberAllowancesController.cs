using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
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
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;
    public FamilyMemberAllowancesController(AppDbContext db, LohnEditLockService editLock)
    {
        _db = db; _editLock = editLock;
    }

    /// <summary>Findet die Filiale des MA über das FamilyMember → Employee → Employments.</summary>
    private async Task<int?> GetBranchByFamilyMemberAsync(int familyMemberId)
    {
        var employeeId = await _db.EmployeeFamilyMembers
            .Where(m => m.Id == familyMemberId)
            .Select(m => (int?)m.EmployeeId)
            .FirstOrDefaultAsync();
        if (employeeId is null) return null;

        return await _db.Employees
            .Where(e => e.Id == employeeId.Value)
            .SelectMany(e => e.Employments)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => (int?)x.CompanyProfileId)
            .FirstOrDefaultAsync();
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(int familyMemberId)
    {
        var branchId     = await GetBranchByFamilyMemberAsync(familyMemberId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;

        var entries = await _db.FamilyMemberAllowances
            .Where(a => a.FamilyMemberId == familyMemberId)
            .OrderByDescending(a => a.ValidFrom)
            .ToListAsync();
        return Ok(entries.Select(a => MapToDto(a, firstAllowed)).ToList());
    }

    /// <summary>
    /// Walter-Vorgabe 28.05.2026: Live-Vorschau des aktuell gültigen Tarif-
    /// Betrags für ein Kind + Zulagenart + konkreten Tarif-Satz + Stichtag.
    /// Wird im Erfassungsmodal genutzt — der User wählt Kategorie + Satz
    /// (z.B. „KZ Satz 2 ab 12 J."), das System zeigt den Betrag aus dem
    /// FAK-Tarif der Filiale am Stichtag.
    /// </summary>
    [HttpGet("resolve-preview")]
    public async Task<IActionResult> ResolvePreview(int familyMemberId,
        [FromQuery] string allowanceType,
        [FromQuery] int?    tarifSatzNr,
        [FromQuery] DateOnly? effectiveDate,
        [FromServices] FamilienzulagenResolverService resolver)
    {
        if (string.IsNullOrWhiteSpace(allowanceType))
            return BadRequest(new { error = "allowanceType ist Pflicht." });

        var member = await _db.EmployeeFamilyMembers
            .FirstOrDefaultAsync(m => m.Id == familyMemberId);
        if (member == null) return NotFound();

        // Filiale → KantonCode des aktiven Vertrags des MA
        var kantonCode = await _db.Employees
            .Where(e => e.Id == member.EmployeeId)
            .SelectMany(e => e.Employments)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => x.CompanyProfile != null ? x.CompanyProfile.KantonCode : null)
            .FirstOrDefaultAsync();

        var stichtag = effectiveDate ?? DateOnly.FromDateTime(DateTime.Today);
        var tarif    = await resolver.GetTarifAsync(kantonCode, stichtag);
        var resolved = FamilienzulagenResolverService.ResolveBySatz(tarif, allowanceType, tarifSatzNr);

        return Ok(new
        {
            amount         = resolved.Amount,
            allowanceType  = resolved.AllowanceType,
            satzLabel      = resolved.SatzLabel,
            description    = resolved.Description,
            tarifId        = resolved.TarifId,
            tarifValidFrom = resolved.TarifValidFrom,
            kantonCode     = kantonCode,
            stichtag       = stichtag
        });
    }

    /// <summary>
    /// Walter-Vorgabe 28.05.2026: Liefert die im Tarif der Filiale des MA am
    /// Stichtag verfügbaren Sätze als wählbare Optionen (KZ Satz 1/2, AZ
    /// Satz 1/2, GZ, AdoptZ — jeweils mit aktuellem Betrag). Damit baut das
    /// Frontend den Dropdown im Zulagen-Modal auf.
    /// </summary>
    [HttpGet("tarif-options")]
    public async Task<IActionResult> TarifOptions(int familyMemberId,
        [FromQuery] DateOnly? effectiveDate,
        [FromServices] FamilienzulagenResolverService resolver)
    {
        var member = await _db.EmployeeFamilyMembers
            .FirstOrDefaultAsync(m => m.Id == familyMemberId);
        if (member == null) return NotFound();

        var kantonCode = await _db.Employees
            .Where(e => e.Id == member.EmployeeId)
            .SelectMany(e => e.Employments)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => x.CompanyProfile != null ? x.CompanyProfile.KantonCode : null)
            .FirstOrDefaultAsync();

        var stichtag = effectiveDate ?? DateOnly.FromDateTime(DateTime.Today);
        var tarif    = await resolver.GetTarifAsync(kantonCode, stichtag);

        var options = new List<object>();
        if (tarif != null)
        {
            void Add(string type, int? satz, decimal? amount, string label)
            {
                if (amount.HasValue)
                    options.Add(new {
                        allowanceType = type,
                        tarifSatzNr   = satz,
                        amount        = amount.Value,
                        label         = label
                    });
            }
            Add("KZ", 1, tarif.KinderzulageSatz1, "KZ Satz 1 — Kinderzulage");
            if (tarif.KinderzulageSatz2.HasValue)
            {
                var altAb = tarif.KinderzulageSatz2AbAlter.HasValue ? $" ab {tarif.KinderzulageSatz2AbAlter} J." : "";
                Add("KZ", 2, tarif.KinderzulageSatz2, $"KZ Satz 2{altAb}");
            }
            Add("AZ", 1, tarif.AusbildungszulageSatz1, "AZ Satz 1 — Ausbildungszulage");
            if (tarif.AusbildungszulageSatz2.HasValue)
            {
                var altAb = tarif.AusbildungszulageSatz2AbAlter.HasValue ? $" ab {tarif.AusbildungszulageSatz2AbAlter} J." : "";
                Add("AZ", 2, tarif.AusbildungszulageSatz2, $"AZ Satz 2{altAb}");
            }
            if (tarif.GeburtszulageBetrag.HasValue && tarif.GeburtszulageBetrag.Value > 0)
                Add("GZ", null, tarif.GeburtszulageBetrag, "GZ — Geburtszulage (einmalig)");
            if (tarif.AdoptionszulageBetrag.HasValue && tarif.AdoptionszulageBetrag.Value > 0)
                Add("AdoptZ", null, tarif.AdoptionszulageBetrag, "AdoptZ — Adoptionszulage (einmalig)");
        }

        return Ok(new {
            kantonCode     = kantonCode,
            stichtag       = stichtag,
            tarifId        = tarif?.Id,
            tarifValidFrom = tarif?.ValidFrom,
            tarifValidTo   = tarif?.ValidTo,
            options        = options
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(int familyMemberId, [FromBody] AllowanceDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(new { error = err });

        var memberExists = await _db.EmployeeFamilyMembers.AnyAsync(m => m.Id == familyMemberId);
        if (!memberExists) return NotFound(new { error = "Familienmitglied nicht gefunden." });

        // Walter 17.05.2026: ValidFrom darf nicht rückwirkend in verarbeitete Periode.
        var branchId     = await GetBranchByFamilyMemberAsync(familyMemberId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;
        if (firstAllowed.HasValue && dto.ValidFrom!.Value < firstAllowed.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"'Gültig ab {dto.ValidFrom.Value:dd.MM.yyyy}' liegt in einer bereits in Verarbeitung befindlichen Lohnperiode. Frühestes erlaubtes 'Gültig ab': {firstAllowed.Value:dd.MM.yyyy}.",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });
        }

        var entry = new FamilyMemberAllowance
        {
            FamilyMemberId = familyMemberId,
            ValidFrom      = dto.ValidFrom!.Value,
            ValidTo        = dto.ValidTo,
            MonthlyAmount  = dto.MonthlyAmount ?? 0m,
            AllowanceType  = NormalizeAllowanceType(dto.AllowanceType),
            TarifSatzNr    = dto.TarifSatzNr,
            Note           = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow
        };
        _db.FamilyMemberAllowances.Add(entry);
        await _db.SaveChangesAsync();
        return Ok(MapToDto(entry, firstAllowed));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int familyMemberId, int id, [FromBody] AllowanceDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(new { error = err });

        var entry = await _db.FamilyMemberAllowances
            .FirstOrDefaultAsync(a => a.Id == id && a.FamilyMemberId == familyMemberId);
        if (entry is null) return NotFound();

        var branchIdU     = await GetBranchByFamilyMemberAsync(familyMemberId);
        var firstAllowedU = branchIdU.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchIdU.Value)
            : null;
        if (firstAllowedU.HasValue && entry.ValidFrom < firstAllowedU.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Diese Zulage (gültig ab {entry.ValidFrom:dd.MM.yyyy}) wurde bereits in einem Lohnlauf verwendet. Bitte einen neuen Eintrag ab frühestens {firstAllowedU:dd.MM.yyyy} anlegen.",
                firstAllowedDate = firstAllowedU?.ToString("yyyy-MM-dd")
            });
        }

        entry.ValidFrom     = dto.ValidFrom!.Value;
        entry.ValidTo       = dto.ValidTo;
        entry.MonthlyAmount = dto.MonthlyAmount ?? 0m;
        entry.AllowanceType = NormalizeAllowanceType(dto.AllowanceType);
        entry.TarifSatzNr   = dto.TarifSatzNr;
        entry.Note          = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        entry.UpdatedAt     = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(MapToDto(entry, firstAllowedU));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int familyMemberId, int id)
    {
        var entry = await _db.FamilyMemberAllowances
            .FirstOrDefaultAsync(a => a.Id == id && a.FamilyMemberId == familyMemberId);
        if (entry is null) return NotFound();

        var branchIdD     = await GetBranchByFamilyMemberAsync(familyMemberId);
        var firstAllowedD = branchIdD.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchIdD.Value)
            : null;
        if (firstAllowedD.HasValue && entry.ValidFrom < firstAllowedD.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Diese Zulage (gültig ab {entry.ValidFrom:dd.MM.yyyy}) wurde bereits in einem Lohnlauf verwendet und kann nicht gelöscht werden.",
                firstAllowedDate = firstAllowedD?.ToString("yyyy-MM-dd")
            });
        }

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

    private static object MapToDto(FamilyMemberAllowance a, DateOnly? firstAllowed = null) => new
    {
        id              = a.Id,
        familyMemberId  = a.FamilyMemberId,
        validFrom       = a.ValidFrom.ToString("yyyy-MM-dd"),
        validTo         = a.ValidTo?.ToString("yyyy-MM-dd"),
        monthlyAmount   = a.MonthlyAmount,
        allowanceType   = a.AllowanceType,
        tarifSatzNr     = a.TarifSatzNr,
        note            = a.Note,
        createdAt       = a.CreatedAt,
        updatedAt       = a.UpdatedAt,
        inLohnVerwendet = firstAllowed.HasValue && a.ValidFrom < firstAllowed.Value
    };

    /// <summary>
    /// Normalisiert die Zulagenart-Schreibweise. KZ/AZ/GZ als Grossbuchstaben,
    /// AdoptZ in Kanon-Camelcase (sonst würde ToUpperInvariant „ADOPTZ" liefern
    /// und die Engine-Switch-Logik bricht).
    /// </summary>
    private static string? NormalizeAllowanceType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();
        if (string.Equals(t, "AdoptZ", StringComparison.OrdinalIgnoreCase)) return "AdoptZ";
        return t.ToUpperInvariant();
    }
}

public record AllowanceDto(
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    decimal?  MonthlyAmount,
    string?   AllowanceType,
    // Walter-Vorgabe 28.05.2026: konkreter Tarif-Satz (1/2) den der User wählt.
    // NULL für Pauschal-Zulagen (GZ/AdoptZ).
    int?      TarifSatzNr,
    string?   Note
);

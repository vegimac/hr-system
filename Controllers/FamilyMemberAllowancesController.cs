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
    private readonly FamzKorrekturService _famzKorrektur;
    public FamilyMemberAllowancesController(
        AppDbContext db, LohnEditLockService editLock, FamzKorrekturService famzKorrektur)
    {
        _db = db; _editLock = editLock; _famzKorrektur = famzKorrektur;
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
        // Weiche Sperre wie QST/Verträge (Walter 01.08.2026): nur DEFINITIV
        // «abgeschlossen» sperrt — Akonto-ausbezahlt und provisorisch bleiben
        // editierbar, sonst kann man Kinderzulagen nicht mehr für den offenen
        // Definitiv-Monat nachtragen.
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateForContractsAsync(branchId.Value)
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
    /// <summary>
    /// MA-Filter «Mit Kinderzulagen» (Walter 04.09.2026): Ids aller MA, die
    /// per heute mindestens eine laufende Zulage (KZ/AZ/…) an einem Kind haben.
    /// Absolute Route, weil der Controller sonst am Familienmitglied hängt.
    /// </summary>
    [HttpGet("/api/family-allowances/employee-ids-current")]
    public async Task<IActionResult> GetEmployeeIdsWithCurrentAllowance()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var ids = await _db.FamilyMemberAllowances.AsNoTracking()
            .Where(a => a.ValidFrom <= today && (a.ValidTo == null || a.ValidTo >= today))
            .Join(_db.EmployeeFamilyMembers.AsNoTracking().Where(f => f.DateOfDeath == null),
                  a => a.FamilyMemberId, f => f.Id, (a, f) => f.EmployeeId)
            .Distinct()
            .ToListAsync();
        return Ok(ids);
    }

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

    /// <summary>
    /// Kein Zulagen-Anspruch ohne Unterhaltspflicht (Walter-Vorgabe 01.09.2026).
    /// Der Anspruch nach FamZG haengt — wie die QST-Kinderziffer — daran, dass
    /// der/die MA fuer den Unterhalt des Kindes aufkommt. Ist am Kind «keine
    /// Unterhaltspflicht» gesetzt, darf gar keine Zulage erfasst werden.
    /// Die Pruefung sitzt bewusst auch hier im Backend und nicht nur in der
    /// Maske: ein ausgeblendeter Knopf ist keine Sperre.
    /// </summary>
    private async Task<IActionResult?> UnterhaltspflichtGeprueftAsync(int familyMemberId)
    {
        var ohneUnterhalt = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(m => m.Id == familyMemberId)
            .Select(m => m.KeineUnterhaltspflicht)
            .FirstOrDefaultAsync();
        if (!ohneUnterhalt) return null;
        return Conflict(new {
            error   = "KEINE_UNTERHALTSPFLICHT",
            message = "Für dieses Kind ist «keine Unterhaltspflicht» erfasst — "
                    + "damit besteht kein Anspruch auf Familienzulagen. "
                    + "Wenn das nicht stimmt, zuerst den Haken beim Kind entfernen."
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(int familyMemberId, [FromBody] AllowanceDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(new { error = err });

        var memberExists = await _db.EmployeeFamilyMembers.AnyAsync(m => m.Id == familyMemberId);
        if (!memberExists) return NotFound(new { error = "Familienmitglied nicht gefunden." });

        if (await UnterhaltspflichtGeprueftAsync(familyMemberId) is IActionResult sperre)
            return sperre;

        // Rückwirkendes «Gültig ab» erlaubt, wenn «Erfahren am» gesetzt ist
        // (Wissens-Achse → famz_korrektur). Sonst wie bisher Sperre.
        var branchId     = await GetBranchByFamilyMemberAsync(familyMemberId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateForContractsAsync(branchId.Value)
            : null;
        var erfahrenAm = dto.ErfahrenAm ?? dto.ValidFrom!.Value;
        if (firstAllowed.HasValue && dto.ValidFrom!.Value < firstAllowed.Value
            && erfahrenAm < firstAllowed.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"«Gültig ab {dto.ValidFrom.Value:dd.MM.yyyy}» liegt in einer abgeschlossenen Periode — bitte «Erfahren am» auf frühestens {firstAllowed.Value:dd.MM.yyyy} setzen (Nachzahlung im offenen Monat).",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });
        }
        if (erfahrenAm < dto.ValidFrom!.Value)
            return BadRequest(new { error = "«Erfahren am» darf nicht vor «Gültig ab» liegen." });

        var (dokOk, dokId, dokErr) = await ResolveDokumentIdAsync(familyMemberId, dto.DokumentId);
        if (!dokOk) return BadRequest(new { error = dokErr });

        var entry = new FamilyMemberAllowance
        {
            FamilyMemberId = familyMemberId,
            ValidFrom      = dto.ValidFrom!.Value,
            ValidTo        = dto.ValidTo,
            ErfahrenAm     = dto.ErfahrenAm,
            MonthlyAmount  = dto.MonthlyAmount ?? 0m,
            AllowanceType  = NormalizeAllowanceType(dto.AllowanceType),
            TarifSatzNr    = dto.TarifSatzNr,
            Note           = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
            DokumentId     = dokId,
            CreatedAt      = DateTime.Now,
            UpdatedAt      = DateTime.Now
        };
        _db.FamilyMemberAllowances.Add(entry);
        await _db.SaveChangesAsync();

        var actor = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                    ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var korr = await _famzKorrektur.ErzeugeKorrekturenAsync(entry, actor);
        return Ok(new { eintrag = MapToDto(entry, firstAllowed), korrekturen = new {
            anzahl = korr.Anzahl, totalBetrag = korr.TotalBetrag, vorjahr = korr.Vorjahr, posten = korr.Posten
        }});
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int familyMemberId, int id, [FromBody] AllowanceDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(new { error = err });

        var entry = await _db.FamilyMemberAllowances
            .FirstOrDefaultAsync(a => a.Id == id && a.FamilyMemberId == familyMemberId);
        if (entry is null) return NotFound();

        // Bestehende Zulage: aendern gesperrt, solange «keine Unterhaltspflicht»
        // steht. Loeschen bleibt erlaubt — sonst waere ein Altbestand, der jetzt
        // als unberechtigt erkannt ist, nicht mehr aufraeumbar.
        if (await UnterhaltspflichtGeprueftAsync(familyMemberId) is IActionResult sperreU)
            return sperreU;

        var branchIdU     = await GetBranchByFamilyMemberAsync(familyMemberId);
        var firstAllowedU = branchIdU.HasValue
            ? await _editLock.GetFirstAllowedDateForContractsAsync(branchIdU.Value)
            : null;
        // Eingefrorene Zulage: ValidFrom/Typ gesperrt, aber ValidTo + ErfahrenAm
        // bleiben editierbar (Ende + Wissensdatum → Korrekturen).
        bool eingefroren = firstAllowedU.HasValue && entry.ValidFrom < firstAllowedU.Value;
        var erfahrenAmU = dto.ErfahrenAm ?? dto.ValidFrom!.Value;
        if (erfahrenAmU < dto.ValidFrom!.Value)
            return BadRequest(new { error = "«Erfahren am» darf nicht vor «Gültig ab» liegen." });

        if (eingefroren)
        {
            // Nur Bis + Erfahren + Doku + Note ändern — Betrag/Typ/Von bleiben.
            entry.ValidTo     = dto.ValidTo;
            entry.ErfahrenAm  = dto.ErfahrenAm;
            entry.Note        = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            var (dokOkF, dokIdF, dokErrF) = await ResolveDokumentIdAsync(familyMemberId, dto.DokumentId);
            if (!dokOkF) return BadRequest(new { error = dokErrF });
            entry.DokumentId  = dokIdF;
            entry.UpdatedAt   = DateTime.Now;
            await _db.SaveChangesAsync();
            var actorF = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                         ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var korrF = await _famzKorrektur.ErzeugeKorrekturenAsync(entry, actorF);
            return Ok(new { eintrag = MapToDto(entry, firstAllowedU), korrekturen = new {
                anzahl = korrF.Anzahl, totalBetrag = korrF.TotalBetrag, vorjahr = korrF.Vorjahr, posten = korrF.Posten
            }});
        }

        var (dokOkU, dokIdU, dokErrU) = await ResolveDokumentIdAsync(familyMemberId, dto.DokumentId);
        if (!dokOkU) return BadRequest(new { error = dokErrU });

        entry.ValidFrom     = dto.ValidFrom!.Value;
        entry.ValidTo       = dto.ValidTo;
        entry.ErfahrenAm    = dto.ErfahrenAm;
        entry.MonthlyAmount = dto.MonthlyAmount ?? 0m;
        entry.AllowanceType = NormalizeAllowanceType(dto.AllowanceType);
        entry.TarifSatzNr   = dto.TarifSatzNr;
        entry.Note          = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        entry.DokumentId    = dokIdU;
        entry.UpdatedAt     = DateTime.Now;
        await _db.SaveChangesAsync();
        var actorU = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                     ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var korrU = await _famzKorrektur.ErzeugeKorrekturenAsync(entry, actorU);
        return Ok(new { eintrag = MapToDto(entry, firstAllowedU), korrekturen = new {
            anzahl = korrU.Anzahl, totalBetrag = korrU.TotalBetrag, vorjahr = korrU.Vorjahr, posten = korrU.Posten
        }});
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int familyMemberId, int id)
    {
        var entry = await _db.FamilyMemberAllowances
            .FirstOrDefaultAsync(a => a.Id == id && a.FamilyMemberId == familyMemberId);
        if (entry is null) return NotFound();

        var branchIdD     = await GetBranchByFamilyMemberAsync(familyMemberId);
        var firstAllowedD = branchIdD.HasValue
            ? await _editLock.GetFirstAllowedDateForContractsAsync(branchIdD.Value)
            : null;
        if (firstAllowedD.HasValue && entry.ValidFrom < firstAllowedD.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Diese Zulage (gültig ab {entry.ValidFrom:dd.MM.yyyy}) liegt in einer definitiv abgeschlossenen Lohnperiode und kann nicht gelöscht werden.",
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
        erfahrenAm      = a.ErfahrenAm?.ToString("yyyy-MM-dd"),
        monthlyAmount   = a.MonthlyAmount,
        allowanceType   = a.AllowanceType,
        tarifSatzNr     = a.TarifSatzNr,
        note            = a.Note,
        dokumentId      = a.DokumentId,
        createdAt       = a.CreatedAt,
        updatedAt       = a.UpdatedAt,
        inLohnVerwendet = firstAllowed.HasValue && a.ValidFrom < firstAllowed.Value
    };

    /// <summary>
    /// Prüft, dass das Entscheidungsdokument (falls gesetzt) zum gleichen MA
    /// gehört wie das Familienmitglied. null = kein Dokument verknüpft.
    /// </summary>
    private async Task<(bool ok, int? dokId, string? error)> ResolveDokumentIdAsync(
        int familyMemberId, int? dokumentId)
    {
        if (dokumentId is null or <= 0) return (true, null, null);
        var empId = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(m => m.Id == familyMemberId)
            .Select(m => (int?)m.EmployeeId)
            .FirstOrDefaultAsync();
        if (empId is null)
            return (false, null, "Familienmitglied nicht gefunden.");
        var belongs = await _db.EmployeeDokumente.AsNoTracking()
            .AnyAsync(d => d.Id == dokumentId.Value && d.EmployeeId == empId.Value);
        if (!belongs)
            return (false, null, "Dokument gehört nicht zu diesem Mitarbeiter.");
        return (true, dokumentId.Value, null);
    }

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
    string?   Note,
    // Walter-Vorgabe 19.07.2026: FAK-/Entscheidungsdokument aus dem MA-Dossier.
    int?      DokumentId,
    // Wissensdatum (Walter 18.09.2026): Nachzahlung/Rückforderung für Zwischenmonate.
    DateOnly? ErfahrenAm
);

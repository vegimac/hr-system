using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Quellensteuer-Einträge pro Mitarbeiter (mit Historie).
///
/// Walter-Vorgabe 17.05.2026: Sobald ein QST-Eintrag in einem Lohnlauf
/// verwendet wurde (= ValidFrom liegt vor dem FirstAllowedDate der Filiale),
/// darf er nicht mehr editiert oder gelöscht werden. Stattdessen muss ein
/// NEUER Eintrag mit aktuellem ValidFrom erfasst werden — der alte bleibt
/// unverändert bestehen (gleiches Pattern wie bei EmployeeBankAccounts).
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/quellensteuer")]
public class EmployeeQuellensteuerController : ControllerBase
{
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;
    private readonly QstTarifVorschlagService _tarifVorschlag;
    public EmployeeQuellensteuerController(AppDbContext db, LohnEditLockService editLock, QstTarifVorschlagService tarifVorschlag)
    {
        _db             = db;
        _editLock       = editLock;
        _tarifVorschlag = tarifVorschlag;
    }

    /// <summary>Filiale des MA (jüngster aktiver Vertrag) — null wenn keiner.</summary>
    private Task<int?> GetEmployeeBranchAsync(int employeeId)
        => _db.Employees
            .Where(e => e.Id == employeeId)
            .SelectMany(e => e.Employments)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => (int?)x.CompanyProfileId)
            .FirstOrDefaultAsync();

    private static bool IsInLohnVerwendet(EmployeeQuellensteuer q, DateOnly? firstAllowed)
        => firstAllowed.HasValue && q.ValidFrom < firstAllowed.Value;

    private static object MapToDto(EmployeeQuellensteuer q, DateOnly? firstAllowed) => new
    {
        q.Id, q.EmployeeId,
        validFrom = q.ValidFrom.ToString("yyyy-MM-dd"),
        validTo   = q.ValidTo?.ToString("yyyy-MM-dd"),
        q.Steuerkanton, q.SteuerkantonName,
        q.QstGemeinde, q.QstGemeindeBfsNr,
        q.TarifvorschlagQst, q.TarifCode, q.TarifBezeichnung,
        q.AnzahlKinder, q.Kirchensteuer, q.QstCode,
        q.SpezielBewilligt, q.Kategorie, q.Prozentsatz,
        q.MindestlohnSatzbestimmung,
        q.PartnerEmployeeId, q.PartnerEinkommenVon, q.PartnerEinkommenBis,
        q.ArbeitsortKanton, q.WeitereBeschaftigungen,
        q.GesamtpensumWeitereAg, q.GesamteinkommenWeitereAg,
        q.Halbfamilie, q.WohnsitzAusland, q.Wohnsitzstaat, q.AdresseAusland,
        q.LivesInKonkubinat, q.HasJointParentalCare,
        q.PaysAlimonyAdultChildren, q.HasHigherIncomeThanPartner,
        q.IsGrenzgaenger, q.IsWochenaufenthalter,
        q.CreatedAt, q.UpdatedAt,
        // True wenn ValidFrom < FirstAllowedDate (Filiale hat schon einen
        // Lohnlauf für diese oder eine spätere Periode laufen lassen).
        inLohnVerwendet = firstAllowed.HasValue && q.ValidFrom < firstAllowed.Value
    };

    /// <summary>
    /// Liefert die IDs aller Mitarbeiter mit einem per heute aktiven QST-Eintrag.
    /// Frontend nutzt das für den Spezialfilter „Quellensteuerpflichtig".
    /// Walter-Vorgabe 18.05.2026 — analog /api/employee-bank-accounts/active-employee-ids.
    /// </summary>
    // GET /api/employees/0/quellensteuer/active-ids
    // (Route ignoriert die {employeeId} aus der Klassen-Route — Pattern wie active-employee-ids bei Bank.)
    [HttpGet("/api/employee-quellensteuer/active-employee-ids")]
    public async Task<IActionResult> GetActiveQstEmployeeIds(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to   = null)
    {
        // Default ohne Parameter: gültig heute (rückwärtskompatibel — z.B.
        // Spezialfilter „QST-pflichtig" auf der MA-Liste).
        // Mit Periode (Lohnlauf-Kontext, Walter 18.05.2026): mindestens ein
        // QST-Eintrag muss IRGENDWO innerhalb der Periode aktiv gewesen sein —
        // also Überlappung: ValidFrom <= to AND (ValidTo IS NULL OR ValidTo >= from).
        var refFrom = from ?? DateOnly.FromDateTime(DateTime.Today);
        var refTo   = to   ?? refFrom;
        var ids = await _db.EmployeeQuellensteuer
            .Where(q => q.ValidFrom <= refTo
                     && (q.ValidTo == null || q.ValidTo >= refFrom))
            .Select(q => q.EmployeeId)
            .Distinct()
            .ToListAsync();
        return Ok(ids);
    }

    // GET /api/employees/{employeeId}/quellensteuer
    // Gibt alle QST-Einträge eines Mitarbeiters zurück (neueste zuerst)
    [HttpGet]
    public async Task<IActionResult> GetAll(int employeeId)
    {
        var branchId = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;

        var entries = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId)
            .OrderByDescending(q => q.ValidFrom)
            .ToListAsync();

        return Ok(entries.Select(q => MapToDto(q, firstAllowed)).ToList());
    }

    // GET /api/employees/{employeeId}/quellensteuer/current?date=2026-04-01
    // Gibt den für ein Datum gültigen QST-Eintrag zurück
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(int employeeId, [FromQuery] DateOnly? date)
    {
        var refDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var entry = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId
                     && q.ValidFrom <= refDate
                     && (q.ValidTo == null || q.ValidTo >= refDate))
            .OrderByDescending(q => q.ValidFrom)
            .FirstOrDefaultAsync();
        return Ok(entry); // null wenn keiner gefunden
    }

    // GET /api/employees/{employeeId}/quellensteuer/vorschlag?date=2026-04-01
    // Liefert einen serverseitigen Tarifvorschlag aus Zivilstand, Kindern,
    // Konfession und den effektiv geladenen ESTV-Tarifkombinationen.
    [HttpGet("vorschlag")]
    public async Task<IActionResult> GetTarifVorschlag(int employeeId, [FromQuery] DateOnly? date)
    {
        var result = await _tarifVorschlag.VorschlagenAsync(employeeId, date);
        if (result == null) return NotFound();
        return Ok(result);
    }

    // GET /api/employees/{employeeId}/quellensteuer/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int employeeId, int id)
    {
        var entry = await _db.EmployeeQuellensteuer
            .FirstOrDefaultAsync(q => q.Id == id && q.EmployeeId == employeeId);
        if (entry is null) return NotFound();
        return Ok(entry);
    }

    // POST /api/employees/{employeeId}/quellensteuer
    // Neuen QST-Eintrag anlegen; schliesst vorherigen Eintrag automatisch ab
    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, [FromBody] EmployeeQuellensteuer dto)
    {
        // Walter-Vorgabe 17.05.2026: ValidFrom darf nicht rückwirkend in einer
        // in-Verarbeitung-Periode liegen. Frühester gültiger Beginn = FirstAllowedDate.
        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;
        if (firstAllowed.HasValue && dto.ValidFrom < firstAllowed.Value)
        {
            return Conflict(new
            {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"'Gültig ab {dto.ValidFrom:dd.MM.yyyy}' liegt in einer bereits in Verarbeitung befindlichen Lohnperiode. " +
                                   $"Frühestes erlaubtes 'Gültig ab': {firstAllowed.Value:dd.MM.yyyy}.",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });
        }

        // Vorherigen offenen Eintrag abschliessen (ValidTo = dto.ValidFrom - 1 Tag)
        var previous = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId && q.ValidTo == null)
            .OrderByDescending(q => q.ValidFrom)
            .FirstOrDefaultAsync();
        if (previous != null && previous.ValidFrom < dto.ValidFrom)
            previous.ValidTo = dto.ValidFrom.AddDays(-1);

        dto.Id         = 0;
        dto.EmployeeId = employeeId;
        dto.CreatedAt  = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        dto.UpdatedAt  = dto.CreatedAt;

        _db.EmployeeQuellensteuer.Add(dto);
        await _db.SaveChangesAsync();
        return Ok(MapToDto(dto, firstAllowed));
    }

    // PUT /api/employees/{employeeId}/quellensteuer/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int employeeId, int id, [FromBody] EmployeeQuellensteuer dto)
    {
        var entry = await _db.EmployeeQuellensteuer
            .FirstOrDefaultAsync(q => q.Id == id && q.EmployeeId == employeeId);
        if (entry is null) return NotFound();

        // Lohnlauf-Sperre: QST-Eintrag der bereits in einem Lohnlauf verwendet
        // wurde, ist tabu für Edit. Stattdessen einen neuen Eintrag ab dem
        // nächsten freien Datum anlegen — der schliesst den alten automatisch.
        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;
        if (IsInLohnVerwendet(entry, firstAllowed))
        {
            return Conflict(new
            {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Dieser QST-Eintrag (gültig ab {entry.ValidFrom:dd.MM.yyyy}) wurde bereits in einem Lohnlauf verwendet. " +
                                   $"Bitte über '+ Neuer Eintrag' einen neuen QST-Eintrag erfassen.",
                firstAllowedDate = firstAllowed?.ToString("yyyy-MM-dd")
            });
        }

        entry.ValidFrom                  = dto.ValidFrom;
        entry.ValidTo                    = dto.ValidTo;
        entry.Steuerkanton               = dto.Steuerkanton;
        entry.SteuerkantonName           = dto.SteuerkantonName;
        entry.QstGemeinde                = dto.QstGemeinde;
        entry.QstGemeindeBfsNr           = dto.QstGemeindeBfsNr;
        entry.TarifvorschlagQst          = dto.TarifvorschlagQst;
        entry.TarifCode                  = dto.TarifCode;
        entry.TarifBezeichnung           = dto.TarifBezeichnung;
        entry.AnzahlKinder               = dto.AnzahlKinder;
        entry.Kirchensteuer              = dto.Kirchensteuer;
        entry.QstCode                    = dto.QstCode;
        entry.SpezielBewilligt           = dto.SpezielBewilligt;
        entry.Kategorie                  = dto.Kategorie;
        entry.Prozentsatz                = dto.Prozentsatz;
        entry.MindestlohnSatzbestimmung  = dto.MindestlohnSatzbestimmung;
        entry.PartnerEmployeeId          = dto.PartnerEmployeeId;
        entry.PartnerEinkommenVon        = dto.PartnerEinkommenVon;
        entry.PartnerEinkommenBis        = dto.PartnerEinkommenBis;
        entry.ArbeitsortKanton           = dto.ArbeitsortKanton;
        entry.WeitereBeschaftigungen     = dto.WeitereBeschaftigungen;
        entry.GesamtpensumWeitereAg      = dto.GesamtpensumWeitereAg;
        entry.GesamteinkommenWeitereAg   = dto.GesamteinkommenWeitereAg;
        entry.Halbfamilie                = dto.Halbfamilie;
        entry.WohnsitzAusland            = dto.WohnsitzAusland;
        entry.Wohnsitzstaat              = dto.Wohnsitzstaat;
        entry.AdresseAusland             = dto.AdresseAusland;

        // Tarif-relevante Stammdaten (für Anmeldung & Tarifbestimmung).
        // Sind nicht im MA-Stamm — gehören hier hin, weil sie sich mit
        // Lebenslagen ändern und über ValidFrom/ValidTo historisiert werden.
        entry.LivesInKonkubinat          = dto.LivesInKonkubinat;
        entry.HasJointParentalCare       = dto.HasJointParentalCare;
        entry.PaysAlimonyAdultChildren   = dto.PaysAlimonyAdultChildren;
        entry.HasHigherIncomeThanPartner = dto.HasHigherIncomeThanPartner;
        entry.IsGrenzgaenger             = dto.IsGrenzgaenger;
        entry.IsWochenaufenthalter       = dto.IsWochenaufenthalter;

        entry.UpdatedAt                  = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        await _db.SaveChangesAsync();
        return Ok(entry);
    }

    // DELETE /api/employees/{employeeId}/quellensteuer/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int employeeId, int id)
    {
        var entry = await _db.EmployeeQuellensteuer
            .FirstOrDefaultAsync(q => q.Id == id && q.EmployeeId == employeeId);
        if (entry is null) return NotFound();

        // Lohnlauf-Sperre: kein Löschen wenn schon in einem Lohnlauf verwendet.
        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;
        if (IsInLohnVerwendet(entry, firstAllowed))
        {
            return Conflict(new
            {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Dieser QST-Eintrag (gültig ab {entry.ValidFrom:dd.MM.yyyy}) wurde bereits in einem Lohnlauf verwendet und kann nicht gelöscht werden.",
                firstAllowedDate = firstAllowed?.ToString("yyyy-MM-dd")
            });
        }

        _db.EmployeeQuellensteuer.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

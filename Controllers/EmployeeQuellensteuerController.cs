using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Quellensteuer-Einträge pro Mitarbeiter (mit Historie).
///
/// Walter-Vorgabe 01.08.2026: QST bleibt während Akonto und HR-Kontrolle
/// (provisorisch_abgeschlossen) editierbar — genau dort wird der Ansatz
/// oft noch korrigiert. Gesperrt erst wenn der DEFINITIV-Lauf
/// <c>abgeschlossen</c> ist (DTA erstellt). Dann: ValidFrom vor dem
/// FirstAllowedDate → nicht mehr editieren/löschen, sondern NEUEN Eintrag
/// anlegen (Versionierung, gleiches Soft-Lock wie Verträge).
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/quellensteuer")]
public class EmployeeQuellensteuerController : ControllerBase
{
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;
    public EmployeeQuellensteuerController(AppDbContext db, LohnEditLockService editLock)
    {
        _db       = db;
        _editLock = editLock;
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

    /// <summary>Soft-Lock wie Verträge: erst ab Definitiv abgeschlossen.</summary>
    private async Task<DateOnly?> GetQstFirstAllowedAsync(int? branchId)
        => branchId.HasValue
            ? await _editLock.GetFirstAllowedDateForContractsAsync(branchId.Value)
            : null;

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
        // True wenn ValidFrom < FirstAllowedDate (Definitiv der Periode
        // abgeschlossen / DTA erstellt — Soft-Lock wie Verträge).
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
        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = await GetQstFirstAllowedAsync(branchId);

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

    /// <summary>
    /// Walter-Vorgabe 14.06.2026: serverseitiger Tarifvorschlag für einen
    /// neuen QST-Eintrag. Berechnet aus Stammdaten (Zivilstand, Religion,
    /// Wohnkanton, Familie-Kinder) den passenden Tarif + Kinderzahl +
    /// Kirchensteuer und prüft gegen die offizielle ESTV-Tariftabelle
    /// (mit Fallback wenn die exakte Kombi fehlt). Frontend nutzt das
    /// beim Öffnen des „+ Neuer Eintrag"-Modals.
    ///
    /// Stichtag default = heute, kann via ?date= überschrieben werden
    /// (z.B. wenn der Eintrag in der Zukunft starten soll).
    /// </summary>
    [HttpGet("vorschlag")]
    public async Task<IActionResult> GetVorschlag(int employeeId, [FromQuery] DateOnly? date,
        [FromServices] QstTarifVorschlagService service)
    {
        // try/catch mit Klartext-Meldung (Walter 13.07.2026): der Endpoint
        // lieferte einen nackten 500 — das QST-Modal zeigt die message jetzt
        // im Hinweis an, damit die Ursache diagnostizierbar ist.
        try
        {
            var stichtag = date ?? DateOnly.FromDateTime(DateTime.Today);
            var result   = await service.BerechneAsync(employeeId, stichtag);
            if (result == null) return NotFound(new { error = "MA_NICHT_GEFUNDEN" });
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "VORSCHLAG_FEHLGESCHLAGEN",
                message = ex.GetBaseException().Message
            });
        }
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
        // Soft-Lock (Walter 01.08.2026): ValidFrom darf nicht rückwirkend in
        // einer DEFINITIV abgeschlossenen Periode liegen. Während HR-Kontrolle
        // (provisorisch) und Akonto bleibt Anlegen möglich.
        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = await GetQstFirstAllowedAsync(branchId);
        if (firstAllowed.HasValue && dto.ValidFrom < firstAllowed.Value)
        {
            return Conflict(new
            {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"'Gültig ab {dto.ValidFrom:dd.MM.yyyy}' liegt in einer definitiv abgeschlossenen Lohnperiode. " +
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

        // Soft-Lock: Edit tabu erst nach Definitiv-Abschluss (DTA). Davor
        // (inkl. HR-Kontrolle) korrigierbar. Danach: neuen Eintrag anlegen.
        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = await GetQstFirstAllowedAsync(branchId);
        if (IsInLohnVerwendet(entry, firstAllowed))
        {
            return Conflict(new
            {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Dieser QST-Eintrag (gültig ab {entry.ValidFrom:dd.MM.yyyy}) gehört zu einer definitiv abgeschlossenen Lohnperiode. " +
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

        // Soft-Lock: Löschen erst nach Definitiv-Abschluss gesperrt.
        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = await GetQstFirstAllowedAsync(branchId);
        if (IsInLohnVerwendet(entry, firstAllowed))
        {
            return Conflict(new
            {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Dieser QST-Eintrag (gültig ab {entry.ValidFrom:dd.MM.yyyy}) gehört zu einer definitiv abgeschlossenen Lohnperiode und kann nicht gelöscht werden.",
                firstAllowedDate = firstAllowed?.ToString("yyyy-MM-dd")
            });
        }

        _db.EmployeeQuellensteuer.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

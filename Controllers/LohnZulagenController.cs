using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[Authorize]
[ApiController]
[Route("api")]
public class LohnZulagenController : ControllerBase
{
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;
    public LohnZulagenController(AppDbContext db, LohnEditLockService editLock)
    {
        _db       = db;
        _editLock = editLock;
    }

    /// <summary>
    /// Lohnlauf-Lock-Check für eine Periode (YYYY-MM) eines MA — Zulagen-spezifisch.
    ///
    /// Walter-Vorgabe 19.05.2026: Zulagen/Abzüge (z.B. Vorschuss, Spesen) dürfen
    /// während der GESAMTEN GF- UND HR-Bearbeitungsphase erfasst werden — nicht
    /// nur in IN_BEARBEITUNG_GF wie der allgemeine LohnEditLock. Gesperrt sind
    /// nur die finalen Stati:
    ///   • Akonto-Status HR_FREIGEGEBEN oder AUSBEZAHLT
    ///   • Definitivlauf provisorisch_abgeschlossen oder abgeschlossen
    /// In allen anderen Stati (OFFEN, IN_BEARBEITUNG_GF, BEI_HR) ist Erfassung
    /// erlaubt — der GF kann während seiner Phase erfassen, HR kann während
    /// seiner Freigabe-Phase noch ergänzen.
    /// </summary>
    private async Task<IActionResult?> CheckLohnLockAsync(int employeeId, string periode)
    {
        if (periode.Length != 7 || periode[4] != '-') return null;
        if (!int.TryParse(periode[..4], out var y))    return null;
        if (!int.TryParse(periode[5..], out var m))    return null;

        var branchId = await _db.Employees
            .Where(e => e.Id == employeeId)
            .SelectMany(e => e.Employments)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => (int?)x.CompanyProfileId)
            .FirstOrDefaultAsync();
        if (branchId is null) return null;

        var per = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == branchId.Value
                                   && p.Year == y && p.Month == m);
        if (per is null) return null;   // Periode existiert noch nicht → offen

        var blockedDefinitiv = per.Status == "provisorisch_abgeschlossen"
                            || per.Status == "abgeschlossen";
        var blockedAkonto    = per.AkontoStatus == "HR_FREIGEGEBEN"
                            || per.AkontoStatus == "AUSBEZAHLT";
        if (!blockedDefinitiv && !blockedAkonto) return null;

        var grund = blockedDefinitiv ? "Definitivlauf abgeschlossen" : "Akonto HR-freigegeben/ausbezahlt";
        return Conflict(new
        {
            error = "LOHN_EDIT_LOCKED",
            message = $"Periode {m:D2}/{y} - {grund}. Zulagen/Abzuege koennen nicht mehr erfasst werden.",
        });
    }

    // ═══════════════════════════════════════════════════════
    //  LOHNPOSITIONEN ALS TYP-KATALOG  (für Zulagen/Abzüge-Dropdown)
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Aktive Lohnpositionen vom Typ ZULAGE oder ABZUG — für das Erfassungs-Dropdown.
    /// Saldo-Vortrag-Lohnpositionen (Codes 901–906) werden ausgefiltert,
    /// da sie nicht als reguläre Zulagen verwendet werden sollen.
    /// </summary>
    [HttpGet("lohn-zulag-typen")]
    public async Task<IActionResult> GetZulagTypen()
    {
        var list = await _db.Lohnpositionen
            .Where(l => l.IsActive
                     && (l.Typ == "ZULAGE" || l.Typ == "ABZUG")
                     && l.Kategorie != "Saldo-Vortrag")
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Code)
            .Select(l => new
            {
                l.Id,
                l.Code,
                l.Bezeichnung,
                l.Typ,
                l.AhvAlvPflichtig,
                l.NbuvPflichtig,
                l.KtgPflichtig,
                l.BvgPflichtig,
                l.QstPflichtig,
                SvPflichtig = l.AhvAlvPflichtig || l.NbuvPflichtig || l.KtgPflichtig || l.BvgPflichtig,
                l.SortOrder,
                Aktiv       = l.IsActive
            })
            .ToListAsync();
        return Ok(list);
    }

    // ═══════════════════════════════════════════════════════
    //  EINTRÄGE  (pro Mitarbeiter + Periode)
    // ═══════════════════════════════════════════════════════

    /// <summary>Alle Einträge eines Mitarbeiters für eine Periode (YYYY-MM).
    /// Saldo-Vortrag-Einträge (Codes 901–906, Kategorie "Saldo-Vortrag")
    /// werden hier ausgefiltert — sie werden über den separaten
    /// SaldoVortragController verwaltet und sollen nicht doppelt in der
    /// Lohn-Page-Zulagen-Liste auftauchen, wo sie irrtümlich als
    /// reguläre Zulagen erscheinen würden.</summary>
    [HttpGet("lohn-zulagen/{employeeId}/{periode}")]
    public async Task<IActionResult> GetZulagen(int employeeId, string periode)
    {
        var list = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .Where(z => z.EmployeeId == employeeId
                     && z.Periode    == periode
                     && z.Lohnposition!.Kategorie != "Saldo-Vortrag")
            .OrderBy(z => z.Lohnposition!.SortOrder)
            .ThenBy(z => z.CreatedAt)
            .Select(z => new
            {
                z.Id,
                z.EmployeeId,
                z.Periode,
                LohnpositionId          = z.LohnpositionId,
                LohnpositionCode        = z.Lohnposition!.Code,
                LohnpositionBezeichnung = z.Lohnposition.Bezeichnung,
                Typ                     = z.Lohnposition.Typ,
                AhvAlvPflichtig         = z.Lohnposition.AhvAlvPflichtig,
                NbuvPflichtig           = z.Lohnposition.NbuvPflichtig,
                KtgPflichtig            = z.Lohnposition.KtgPflichtig,
                BvgPflichtig            = z.Lohnposition.BvgPflichtig,
                QstPflichtig            = z.Lohnposition.QstPflichtig,
                z.Betrag,
                z.Bemerkung,
                z.CreatedAt
            })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>Neuen Eintrag erfassen</summary>
    [HttpPost("lohn-zulagen")]
    public async Task<IActionResult> CreateZulage([FromBody] LohnZulageDto dto)
    {
        if (dto.Periode.Length != 7 || dto.Periode[4] != '-')
            return BadRequest("Periode muss im Format YYYY-MM sein.");
        if (dto.Betrag <= 0)
            return BadRequest("Betrag muss grösser als 0 sein.");

        // Lohnlauf-Sperre: keine Zulage in einer in-Verarbeitung-Periode anlegen.
        var locked = await CheckLohnLockAsync(dto.EmployeeId, dto.Periode);
        if (locked != null) return locked;

        var lp = await _db.Lohnpositionen.FindAsync(dto.LohnpositionId);
        if (lp is null) return BadRequest("Unbekannte Lohnposition.");
        if (lp.Typ != "ZULAGE" && lp.Typ != "ABZUG")
            return BadRequest("Lohnposition muss Typ ZULAGE oder ABZUG haben.");

        var entry = new LohnZulage
        {
            EmployeeId    = dto.EmployeeId,
            Periode       = dto.Periode,
            LohnpositionId = dto.LohnpositionId,
            Betrag        = Math.Round(dto.Betrag, 2),
            Bemerkung     = dto.Bemerkung?.Trim(),
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow
        };
        _db.LohnZulagen.Add(entry);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            entry.Id, entry.EmployeeId, entry.Periode,
            LohnpositionId          = lp.Id,
            LohnpositionCode        = lp.Code,
            LohnpositionBezeichnung = lp.Bezeichnung,
            Typ                     = lp.Typ,
            AhvAlvPflichtig         = lp.AhvAlvPflichtig,
            NbuvPflichtig           = lp.NbuvPflichtig,
            KtgPflichtig            = lp.KtgPflichtig,
            BvgPflichtig            = lp.BvgPflichtig,
            QstPflichtig            = lp.QstPflichtig,
            entry.Betrag, entry.Bemerkung, entry.CreatedAt
        });
    }

    /// <summary>Eintrag aktualisieren (Betrag / Bemerkung)</summary>
    [HttpPut("lohn-zulagen/{id}")]
    public async Task<IActionResult> UpdateZulage(int id, [FromBody] LohnZulageUpdateDto dto)
    {
        var entry = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .FirstOrDefaultAsync(z => z.Id == id);
        if (entry is null) return NotFound();
        if (dto.Betrag <= 0) return BadRequest("Betrag muss grösser als 0 sein.");

        // Lohnlauf-Sperre: keine Änderung in einer in-Verarbeitung-Periode.
        var locked = await CheckLohnLockAsync(entry.EmployeeId, entry.Periode);
        if (locked != null) return locked;

        entry.Betrag    = Math.Round(dto.Betrag, 2);
        entry.Bemerkung = dto.Bemerkung?.Trim();
        entry.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new
        {
            entry.Id, entry.EmployeeId, entry.Periode,
            LohnpositionId          = entry.Lohnposition!.Id,
            LohnpositionCode        = entry.Lohnposition.Code,
            LohnpositionBezeichnung = entry.Lohnposition.Bezeichnung,
            Typ                     = entry.Lohnposition.Typ,
            AhvAlvPflichtig         = entry.Lohnposition.AhvAlvPflichtig,
            NbuvPflichtig           = entry.Lohnposition.NbuvPflichtig,
            KtgPflichtig            = entry.Lohnposition.KtgPflichtig,
            BvgPflichtig            = entry.Lohnposition.BvgPflichtig,
            QstPflichtig            = entry.Lohnposition.QstPflichtig,
            entry.Betrag, entry.Bemerkung, entry.CreatedAt
        });
    }

    /// <summary>Eintrag löschen</summary>
    [HttpDelete("lohn-zulagen/{id}")]
    public async Task<IActionResult> DeleteZulage(int id)
    {
        var entry = await _db.LohnZulagen.FindAsync(id);
        if (entry is null) return NotFound();

        // Lohnlauf-Sperre: kein Löschen in einer in-Verarbeitung-Periode.
        var locked = await CheckLohnLockAsync(entry.EmployeeId, entry.Periode);
        if (locked != null) return locked;

        _db.LohnZulagen.Remove(entry);
        await _db.SaveChangesAsync();
        return Ok();
    }
}

// ─── DTOs ───────────────────────────────────────────────────────────────────

public record LohnZulageDto(
    int     EmployeeId,
    string  Periode,
    int     LohnpositionId,
    decimal Betrag,
    string? Bemerkung
);

public record LohnZulageUpdateDto(
    decimal Betrag,
    string? Bemerkung
);

using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Migrations-Vortrag pro Mitarbeiter: einmaliges Erfassen der Anfangs-
/// Saldi beim Wechsel vom alten System aufs neue.
///
/// Mechanik: jeder Saldo-Typ wird als LohnZulage-Eintrag in der vom User
/// gewählten Migrations-Periode (z.B. "2026-03") angelegt, mit Referenz
/// auf eine der Vortrag-Lohnpositionen (Codes 901–906). Die 6 Lohn-
/// positionen sind AHV-neutral und fliessen nicht in Bemessungsgrund-
/// lagen ein — sie dienen ausschliesslich als Saldo-Eröffnung. Die
/// PayrollService nutzt diese Beträge als "Vormonat-Saldo" für die
/// erste echte Lohnberechnung.
///
/// Mid-Year-Korrekturen NICHT über diesen Endpoint, sondern über den
/// normalen Zulagen-/Abzüge-Workflow — sonst geht der Audit-Trail
/// verloren. Idempotent: bei erneutem POST werden bestehende Vortrag-
/// Einträge des MA überschrieben (Werte editieren), nicht doppelt
/// angelegt.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/saldo-vortrag")]
public class SaldoVortragController : ControllerBase
{
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;
    public SaldoVortragController(AppDbContext db, LohnEditLockService editLock)
    {
        _db = db; _editLock = editLock;
    }

    /// <summary>Filiale des MA (jüngster aktiver Vertrag).</summary>
    private Task<int?> GetEmployeeBranchAsync(int employeeId)
        => _db.Employees
            .Where(e => e.Id == employeeId)
            .SelectMany(e => e.Employments)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => (int?)x.CompanyProfileId)
            .FirstOrDefaultAsync();

    // Lohnposition-Codes (siehe add_saldo_vortrag.sql)
    private const string CodeZeit         = "901";
    private const string CodeFeiertag     = "902";
    private const string CodeFerienTage   = "903";
    private const string CodeNacht        = "904";
    private const string CodeFerienGeld   = "905";
    private const string CodeDreizehnter  = "906";

    private static readonly string[] AllCodes =
        { CodeZeit, CodeFeiertag, CodeFerienTage, CodeNacht, CodeFerienGeld, CodeDreizehnter };

    /// <summary>
    /// Welche Saldi sind pro Vertragstyp im Vorsystem geführt? Synchron zur
    /// Frontend-Tabelle SV_FIELD_RELEVANCE. Logik:
    ///   • UTP   → Feiertag/Zeit/Nacht/13. werden monatlich ausbezahlt → keine Saldi.
    ///             Nur Ferien-Tage und Ferien-Geld werden akkumuliert.
    ///   • MTP   → wie UTP, plus Stunden- und Nacht-Saldo (garantierte Stunden).
    ///             13. ML wird im Auszahlungsmonat verrechnet → 13.-Saldo wird geführt.
    ///   • FIX   → Stunden, Feiertag-Tage, Ferien-Tage, Nacht, 13. ML.
    ///             Ferien-Geld ist im Festlohn enthalten → kein Saldo.
    ///   • FIX-M → identisch zu FIX.
    /// Unbekanntes Modell → alle Saldi zulassen (defensiv).
    /// </summary>
    private static bool IsRelevantForModel(string saldoCode, string model) => model switch
    {
        "UTP"   => saldoCode is CodeFerienTage or CodeFerienGeld,
        "MTP"   => saldoCode is CodeZeit or CodeFerienTage or CodeNacht or CodeFerienGeld or CodeDreizehnter,
        "FIX"   => saldoCode is CodeZeit or CodeFeiertag or CodeFerienTage or CodeNacht or CodeDreizehnter,
        "FIX-M" => saldoCode is CodeZeit or CodeFeiertag or CodeFerienTage or CodeNacht or CodeDreizehnter,
        _       => true
    };

    public record VortragDto(
        string Periode,                    // "YYYY-MM" — Periode in der die Vortrag-Einträge angelegt werden
        decimal ZeitSaldoH,                // Stunden, signed
        decimal FeiertagSaldoH,            // Stunden, signed
        decimal FerienSaldoTage,           // Tage, signed
        decimal NachtSaldoH,               // Stunden, signed
        decimal FerienGeldSaldoChf,        // CHF, signed
        decimal DreizehnterSaldoChf        // CHF, signed
    );

    public record VortragResponse(
        bool Exists,
        string? Periode,
        decimal ZeitSaldoH,
        decimal FeiertagSaldoH,
        decimal FerienSaldoTage,
        decimal NachtSaldoH,
        decimal FerienGeldSaldoChf,
        decimal DreizehnterSaldoChf,
        DateTime? ErfasstAm
    );

    /// <summary>
    /// Liefert die für diesen MA aktuell erfassten Vortrag-Werte zurück
    /// — oder Exists=false wenn noch kein Vortrag erfasst ist.
    /// </summary>
    [HttpGet("{employeeId}")]
    public async Task<IActionResult> Get(int employeeId)
    {
        var entries = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .Where(z => z.EmployeeId == employeeId
                     && z.Lohnposition!.Kategorie == "Saldo-Vortrag")
            .ToListAsync();

        if (entries.Count == 0)
            return Ok(new VortragResponse(false, null, 0, 0, 0, 0, 0, 0, null));

        decimal Get(string code) =>
            entries.FirstOrDefault(e => e.Lohnposition!.Code == code)?.Betrag ?? 0;

        // Periode aller Vortrag-Einträge — sollte dieselbe sein
        var periode = entries.First().Periode;
        var erfasstAm = entries.Min(e => e.CreatedAt);

        return Ok(new VortragResponse(
            Exists:                true,
            Periode:               periode,
            ZeitSaldoH:            Get(CodeZeit),
            FeiertagSaldoH:        Get(CodeFeiertag),
            FerienSaldoTage:       Get(CodeFerienTage),
            NachtSaldoH:           Get(CodeNacht),
            FerienGeldSaldoChf:    Get(CodeFerienGeld),
            DreizehnterSaldoChf:   Get(CodeDreizehnter),
            ErfasstAm:             erfasstAm
        ));
    }

    /// <summary>
    /// Erfasst oder aktualisiert den Vortrag eines MA. Idempotent —
    /// bestehende Einträge werden überschrieben, nicht dupliziert.
    /// Beträge können positiv oder negativ sein (Vorzeichen wird in der
    /// LohnZulage.Betrag-Spalte gespeichert; das normale Zulagen-Schema
    /// wird so leicht missbraucht, ist aber für diesen einmaligen
    /// Migrations-Datensatz akzeptabel).
    /// </summary>
    [HttpPost("{employeeId}")]
    public async Task<IActionResult> Upsert(int employeeId, [FromBody] VortragDto dto)
    {
        // Periode-Format prüfen
        if (dto.Periode.Length != 7 || dto.Periode[4] != '-')
            return BadRequest("Periode muss im Format YYYY-MM sein.");

        // Walter 17.05.2026: Vortrag-Periode darf nicht in einem schon
        // verarbeiteten Monat liegen. Saldi sind Eröffnungswerte und müssen
        // VOR der ersten Lohnberechnung der Folgeperiode stehen.
        if (int.TryParse(dto.Periode[..4], out var yr) &&
            int.TryParse(dto.Periode[5..], out var mn))
        {
            var branchId     = await GetEmployeeBranchAsync(employeeId);
            var firstAllowed = branchId.HasValue
                ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
                : null;
            var periodStart  = new DateOnly(yr, mn, 1);
            if (firstAllowed.HasValue && periodStart < firstAllowed.Value)
            {
                return Conflict(new {
                    error            = "LOHN_EDIT_LOCKED",
                    message          = $"Saldo-Vortrag-Periode {dto.Periode} liegt in einer bereits in Verarbeitung befindlichen Lohnperiode. Frühestes erlaubtes Datum: {firstAllowed.Value:dd.MM.yyyy}.",
                    firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
                });
            }
        }

        // MA inkl. Verträge laden (für Vertragstyp-basierte Relevanz-Filterung)
        var emp = await _db.Employees
            .Include(e => e.Employments)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound("Mitarbeiter nicht gefunden.");

        // Vertragsmodell des aktivsten Vertrags ermitteln
        var activeEmployment = emp.Employments
            .Where(e => e.IsActive)
            .OrderByDescending(e => e.ContractStartDate)
            .FirstOrDefault();
        var employmentModel = (activeEmployment?.EmploymentModel ?? "").ToUpperInvariant();

        // Vortrag-Lohnpositionen laden (per Code-Map)
        var lps = await _db.Lohnpositionen
            .Where(l => AllCodes.Contains(l.Code) && l.Kategorie == "Saldo-Vortrag")
            .ToDictionaryAsync(l => l.Code, l => l);

        if (lps.Count != AllCodes.Length)
            return Problem("Vortrag-Lohnpositionen 901–906 fehlen in der DB. " +
                           "Bitte add_saldo_vortrag.sql ausführen.", statusCode: 500);

        // Mapping Code → Betrag
        var werte = new Dictionary<string, decimal>
        {
            [CodeZeit]         = dto.ZeitSaldoH,
            [CodeFeiertag]     = dto.FeiertagSaldoH,
            [CodeFerienTage]   = dto.FerienSaldoTage,
            [CodeNacht]        = dto.NachtSaldoH,
            [CodeFerienGeld]   = dto.FerienGeldSaldoChf,
            [CodeDreizehnter]  = dto.DreizehnterSaldoChf,
        };

        // Bestehende Vortrag-Einträge des MA holen (egal in welcher Periode)
        var existing = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .Where(z => z.EmployeeId == employeeId
                     && z.Lohnposition!.Kategorie == "Saldo-Vortrag")
            .ToListAsync();

        foreach (var (code, lp) in lps)
        {
            var entry  = existing.FirstOrDefault(e => e.Lohnposition!.Code == code);
            var betrag = Math.Round(werte[code], 2);

            // Relevanz pro Vertragstyp entscheidet — nicht der Wert.
            // Eine bewusste 0 ist eine sinnvolle Aussage ("keine Restferien
            // aus Vorsystem"); sie wird gespeichert, damit die Saldi-Sektion
            // im Lohnzettel die explizite Eröffnung zeigt. Nur Saldi die für
            // den Vertragstyp gar nicht relevant sind (z.B. Feiertag-Saldo
            // bei UTP) werden weggelassen — falls trotzdem ein alter Eintrag
            // herumfliegt, entfernen.
            bool relevant = IsRelevantForModel(code, employmentModel);

            if (!relevant)
            {
                if (entry != null) _db.LohnZulagen.Remove(entry);
                continue;
            }

            if (entry == null)
            {
                _db.LohnZulagen.Add(new LohnZulage
                {
                    EmployeeId      = employeeId,
                    Periode         = dto.Periode,
                    LohnpositionId  = lp.Id,
                    Betrag          = betrag,
                    Bemerkung       = "Migrations-Vortrag aus Vorsystem",
                    CreatedAt       = DateTime.UtcNow,
                    UpdatedAt       = DateTime.UtcNow
                });
            }
            else
            {
                entry.Periode   = dto.Periode;
                entry.Betrag    = betrag;
                entry.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
        return await Get(employeeId);
    }

    /// <summary>
    /// Vortrag eines MA komplett entfernen. Sollte nur in seltenen
    /// Korrektur-Fällen genutzt werden, da die Saldi danach wieder
    /// bei 0 starten.
    /// </summary>
    [HttpDelete("{employeeId}")]
    public async Task<IActionResult> Delete(int employeeId)
    {
        var entries = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .Where(z => z.EmployeeId == employeeId
                     && z.Lohnposition!.Kategorie == "Saldo-Vortrag")
            .ToListAsync();

        if (entries.Count == 0)
            return Ok(new { deleted = 0 });

        // Walter 17.05.2026: Vortrag-Löschen blockieren wenn die Periode
        // bereits in Verarbeitung ist (Saldi sind dann schon verrechnet).
        var branchIdD     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowedD = branchIdD.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchIdD.Value)
            : null;
        if (firstAllowedD.HasValue)
        {
            // Periode des ältesten Vortrag-Eintrags prüfen
            var oldestPer = entries.Select(e => e.Periode).Min();
            if (!string.IsNullOrEmpty(oldestPer)
                && int.TryParse(oldestPer[..4], out var yr2)
                && int.TryParse(oldestPer[5..], out var mn2))
            {
                var periodStart = new DateOnly(yr2, mn2, 1);
                if (periodStart < firstAllowedD.Value)
                {
                    return Conflict(new {
                        error            = "LOHN_EDIT_LOCKED",
                        message          = $"Saldo-Vortrag in Periode {oldestPer} wurde bereits in einem Lohnlauf verwendet und kann nicht gelöscht werden.",
                        firstAllowedDate = firstAllowedD?.ToString("yyyy-MM-dd")
                    });
                }
            }
        }

        _db.LohnZulagen.RemoveRange(entries);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = entries.Count });
    }

    /// <summary>
    /// Liste aller MA mit Vortrag-Status — für die Admin-Übersicht.
    /// Liefert pro MA: Name, Personalnummer, hatVortrag, erfasstAm.
    ///
    /// Filial-Filter: ein MA "gehört" zu einer Filiale, wenn er dort
    /// einen aktiven Vertrag hat (Employment.CompanyProfileId). Da MA
    /// auch mehrere Verträge in verschiedenen Filialen haben können,
    /// wird der Filter via Any() auf den Employments-Subqueries
    /// ausgeführt.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? companyProfileId)
    {
        // Nur aktive MA, optional auf Filiale gefiltert (über Employments)
        var empQuery = _db.Employees.Where(e => e.IsActive);
        if (companyProfileId.HasValue)
        {
            empQuery = empQuery.Where(e =>
                e.Employments.Any(emp => emp.CompanyProfileId == companyProfileId.Value));
        }

        // Inkl. primäre Filiale + Vertragsmodell des aktivsten Vertrags.
        // Vertragsmodell wird im Modal als Badge angezeigt und steuert
        // welche Saldi-Felder als "relevant" hervorgehoben werden.
        var emps = await empQuery
            .OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .Select(e => new
            {
                e.Id, e.FirstName, e.LastName, e.EmployeeNumber,
                PrimaryCompanyProfileId = e.Employments
                    .Where(emp => emp.IsActive && emp.CompanyProfileId.HasValue)
                    .OrderByDescending(emp => emp.ContractStartDate)
                    .Select(emp => emp.CompanyProfileId)
                    .FirstOrDefault(),
                EmploymentModel = e.Employments
                    .Where(emp => emp.IsActive)
                    .OrderByDescending(emp => emp.ContractStartDate)
                    .Select(emp => emp.EmploymentModel)
                    .FirstOrDefault()
            })
            .ToListAsync();

        // Vortrag-Status pro MA in einem Schwung holen
        var vortragMap = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .Where(z => z.Lohnposition!.Kategorie == "Saldo-Vortrag")
            .GroupBy(z => z.EmployeeId)
            .Select(g => new {
                EmployeeId = g.Key,
                ErfasstAm  = g.Min(z => z.CreatedAt),
                Periode    = g.First().Periode
            })
            .ToDictionaryAsync(x => x.EmployeeId, x => x);

        var result = emps.Select(e =>
        {
            vortragMap.TryGetValue(e.Id, out var v);
            return new
            {
                e.Id,
                e.FirstName,
                e.LastName,
                e.EmployeeNumber,
                e.PrimaryCompanyProfileId,
                e.EmploymentModel,
                HatVortrag = v != null,
                ErfasstAm  = v?.ErfasstAm,
                Periode    = v?.Periode
            };
        });

        return Ok(result);
    }
}

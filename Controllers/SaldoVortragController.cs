using System.Security.Claims;
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
    private readonly AppDbContext            _db;
    private readonly LohnEditLockService     _editLock;
    private readonly SnapshotRecomputeService _recompute;
    public SaldoVortragController(AppDbContext db, LohnEditLockService editLock,
                                  SnapshotRecomputeService recompute)
    {
        _db = db; _editLock = editLock; _recompute = recompute;
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
    ///   • FLEX  → Ferien-Tage, Ferien-Geld, Nacht-Saldo (Zeitzuschlag), 13. ML
    ///             (906 vor allem Probezeit). Kein Zeitsaldo / Feiertag-Tage.
    ///   • MTP   → Stunden-, Ferien-, Nacht-, Ferien-Geld- und 13.-Saldo.
    ///   • FIX   → Stunden, Feiertag-Tage, Ferien-Tage, Nacht, 13. ML.
    ///             Ferien-Geld ist im Festlohn enthalten → kein Saldo.
    ///   • FIX-M → identisch zu FIX.
    /// Unbekanntes Modell → alle Saldi zulassen (defensiv).
    /// </summary>
    private static bool IsRelevantForModel(string saldoCode, string model) => model switch
    {
        // FLEX: Nacht-Saldo (904) mitführen — analog Lohnzettel (Walter 02.08.2026)
        "FLEX"   => saldoCode is CodeFerienTage or CodeFerienGeld or CodeNacht or CodeDreizehnter,
        "MTP"   => saldoCode is CodeZeit or CodeFerienTage or CodeNacht or CodeFerienGeld or CodeDreizehnter,
        "FIX"   => saldoCode is CodeZeit or CodeFeiertag or CodeFerienTage or CodeNacht or CodeDreizehnter,
        "FIX-M" => saldoCode is CodeZeit or CodeFeiertag or CodeFerienTage or CodeNacht or CodeDreizehnter,
        _       => true
    };

    /// <summary>Öffentlich für Unit-Tests (Relevanz-Matrix).</summary>
    public static bool IsVortragRelevantForModel(string saldoCode, string model) =>
        IsRelevantForModel(saldoCode, model == "UTP" ? "FLEX" : model);

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
                ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value, includeAkonto: false)
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
                    CreatedAt       = DateTime.Now,
                    UpdatedAt       = DateTime.Now
                });
            }
            else
            {
                entry.Periode   = dto.Periode;
                entry.Betrag    = betrag;
                entry.UpdatedAt = DateTime.Now;
            }
        }

        await _db.SaveChangesAsync();
        return await Get(employeeId);
    }

    public record SaldoKorrekturDto(
        int EmployeeId,
        string Periode,        // "YYYY-MM" — Periode, in der die Korrektur wirkt
        string Code,           // "901"…"906" — Saldo-Vortrag-Lohnposition
        decimal Betrag,        // DELTA (signed) — wird zum bestehenden Eintrag addiert
        string? Begruendung    // PFLICHT — Audit-Trail («Abgleich mit Mirus» etc.)
    );

    /// <summary>
    /// Saldo-Korrektur beim HR-Bestätigen (Walter-Vorgabe 04.08.2026).
    ///
    /// HR kann im Definitivlauf eine nachvollziehbare Saldo-Korrektur erfassen
    /// (z.B. «Abgleich mit Mirus», wenn ein Vortrag fehlt) — auch wenn die
    /// Periode bereits provisorisch abgeschlossen ist. Der Betrag ist ein
    /// DELTA: existiert in der Periode schon ein Vortrag-Eintrag desselben
    /// Codes, wird addiert, sonst ein neuer Eintrag angelegt. Danach wird die
    /// Periode via SnapshotRecomputeService frisch gerechnet (Workflow-Status
    /// bleibt erhalten).
    ///
    /// BEWUSST OHNE LohnEditLockService: die normale Edit-Sperre blockt genau
    /// den Anwendungsfall dieses Endpoints (Korrektur während die Periode bei
    /// HR liegt = provisorisch_abgeschlossen). Der Schutz ist stattdessen:
    ///   • [Authorize admin,superuser] — nur HR,
    ///   • harter 409-Riegel bei DEFINITIV «abgeschlossen»,
    ///   • Pflicht-Begründung + Klarname/Datum in der Bemerkung (Audit-Trail),
    ///   • Snapshot-Neuberechnung, damit nichts still auseinanderläuft.
    /// </summary>
    [HttpPost("korrektur")]
    public async Task<IActionResult> Korrektur([FromBody] SaldoKorrekturDto dto)
    {
        // ── Validierung ──
        if (string.IsNullOrWhiteSpace(dto.Begruendung))
            return BadRequest(new { error = "BEGRUENDUNG_FEHLT",
                message = "Begründung ist Pflicht — die Saldo-Korrektur muss nachvollziehbar sein." });

        if (dto.Periode is null || dto.Periode.Length != 7 || dto.Periode[4] != '-'
            || !int.TryParse(dto.Periode[..4], out var yr)
            || !int.TryParse(dto.Periode[5..], out var mn)
            || mn < 1 || mn > 12)
            return BadRequest(new { error = "PERIODE_UNGUELTIG",
                message = "Periode muss im Format YYYY-MM sein." });

        if (!AllCodes.Contains(dto.Code))
            return BadRequest(new { error = "CODE_UNGUELTIG",
                message = $"Saldo-Code «{dto.Code}» ist unbekannt — erlaubt sind 901–906." });

        if (dto.Betrag == 0)
            return BadRequest(new { error = "BETRAG_NULL",
                message = "Betrag 0 ist keine Korrektur — bitte ein Delta (+/−) angeben." });

        // ── MA + Vertragsmodell (Relevanz-Prüfung wie beim Vortrag) ──
        var emp = await _db.Employees
            .Include(e => e.Employments)
            .FirstOrDefaultAsync(e => e.Id == dto.EmployeeId);
        if (emp == null) return NotFound("Mitarbeiter nicht gefunden.");

        var activeEmployment = emp.Employments
            .Where(e => e.IsActive)
            .OrderByDescending(e => e.ContractStartDate)
            .FirstOrDefault();
        var employmentModel = (activeEmployment?.EmploymentModel ?? "").ToUpperInvariant();

        if (!IsVortragRelevantForModel(dto.Code, employmentModel))
            return BadRequest(new { error = "CODE_NICHT_RELEVANT",
                message = $"Saldo-Code «{dto.Code}» ist für das Vertragsmodell «{employmentModel}» nicht relevant — dieser Saldo wird bei diesem Modell nicht geführt." });

        // ── Periode-Status der MA-Filiale prüfen: nur der ENDGÜLTIGE Abschluss blockt ──
        var branchId = await GetEmployeeBranchAsync(dto.EmployeeId);
        if (branchId.HasValue)
        {
            var abgeschlossen = await _db.PayrollPerioden.AnyAsync(p =>
                p.CompanyProfileId == branchId.Value &&
                p.Year   == yr &&
                p.Month  == mn &&
                p.Status == "abgeschlossen");
            if (abgeschlossen)
            {
                return Conflict(new {
                    error   = "PERIODE_ABGESCHLOSSEN",
                    message = "Periode ist definitiv abgeschlossen — keine Saldo-Korrektur mehr möglich."
                });
            }
        }

        // ── Lohnposition per Code ──
        var lp = await _db.Lohnpositionen
            .FirstOrDefaultAsync(l => l.Code == dto.Code && l.Kategorie == "Saldo-Vortrag");
        if (lp == null)
            return Problem($"Vortrag-Lohnposition «{dto.Code}» fehlt in der DB. " +
                           "Bitte add_saldo_vortrag.sql ausführen.", statusCode: 500);

        // ── Audit-Text: Klarname IMMER aus dem JWT, nie aus dem Body ──
        var actorName = await GetActorNameAsync() ?? "unbekannt";
        var auditNote = $"Saldo-Korrektur: {dto.Begruendung.Trim()} — {actorName}, {DateTime.Now:dd.MM.yyyy}";

        // ── DELTA anwenden: bestehenden Eintrag desselben Codes in der Periode
        //    aufaddieren, sonst neuen Eintrag anlegen ──
        var betragDelta = Math.Round(dto.Betrag, 2);
        var entry = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .Where(z => z.EmployeeId == dto.EmployeeId
                     && z.Periode == dto.Periode
                     && z.LohnpositionId == lp.Id)
            .FirstOrDefaultAsync();

        decimal neuerBetrag;
        if (entry == null)
        {
            neuerBetrag = betragDelta;
            _db.LohnZulagen.Add(new LohnZulage
            {
                EmployeeId     = dto.EmployeeId,
                Periode        = dto.Periode,
                LohnpositionId = lp.Id,
                Betrag         = betragDelta,
                Bemerkung      = auditNote,
                CreatedAt      = DateTime.Now,
                UpdatedAt      = DateTime.Now
            });
        }
        else
        {
            neuerBetrag     = Math.Round(entry.Betrag + betragDelta, 2);
            entry.Betrag    = neuerBetrag;
            entry.UpdatedAt = DateTime.Now;
            // Bestehende Bemerkung (z.B. «Migrations-Vortrag aus Vorsystem»)
            // bleibt erhalten — die Korrektur wird angehängt (Audit-Trail).
            entry.Bemerkung = string.IsNullOrWhiteSpace(entry.Bemerkung)
                ? auditNote
                : $"{entry.Bemerkung} | {auditNote} ({(betragDelta >= 0 ? "+" : "")}{betragDelta:0.00})";
        }

        await _db.SaveChangesAsync();

        // ── Snapshot(s) der Periode frisch rechnen — der Service kann nur ganze
        //    Perioden (RecomputeAsync(cpId, year, month)), das ist akzeptabel.
        //    Workflow-Status bleibt dabei erhalten (macht der Service so). ──
        var recomputed = false;
        if (branchId.HasValue)
        {
            var updated = await _recompute.RecomputeAsync(branchId.Value, yr, mn);
            recomputed = updated > 0;
        }

        return Ok(new { ok = true, neuerBetrag, recomputed });
    }

    /// <summary>Klarname des eingeloggten Users (Audit) — aus dem JWT, nie aus dem Body.</summary>
    private async Task<string?> GetActorNameAsync()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(idStr, out var uid))
        {
            var u = await _db.AppUsers.Where(x => x.Id == uid)
                .Select(x => new { x.FirstName, x.LastName, x.Username })
                .FirstOrDefaultAsync();
            if (u != null)
            {
                var full = $"{u.FirstName} {u.LastName}".Trim();
                return string.IsNullOrWhiteSpace(full) ? u.Username : full;
            }
        }
        return User.Identity?.Name;
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

        // Walter-Vorgabe 24.05.2026: Vortrag-Löschen NUR blockieren, wenn die
        // betroffene Periode im DEFINITIV-Lauf bereits ENDGÜLTIG "abgeschlossen"
        // ist. provisorisch_abgeschlossen ODER ein laufender/abgeschlossener
        // Akonto blockieren das Löschen NICHT — die Eröffnungssaldi sind der
        // Input für den Definitivlohn und dürfen korrigiert/gelöscht werden,
        // solange der Definitivlauf dieser Periode nicht final abgeschlossen ist.
        // (Früher sperrte hier GetFirstAllowedDateAsync schon ab
        // provisorisch_abgeschlossen — das war zu streng.)
        var branchIdD = await GetEmployeeBranchAsync(employeeId);
        if (branchIdD.HasValue)
        {
            foreach (var per in entries.Select(e => e.Periode).Distinct())
            {
                if (string.IsNullOrEmpty(per) || per.Length != 7) continue;
                if (!int.TryParse(per[..4], out var yr2) || !int.TryParse(per[5..], out var mn2)) continue;

                var abgeschlossen = await _db.PayrollPerioden.AnyAsync(p =>
                    p.CompanyProfileId == branchIdD.Value &&
                    p.Year  == yr2 &&
                    p.Month == mn2 &&
                    p.Status == "abgeschlossen");

                if (abgeschlossen)
                {
                    return Conflict(new {
                        error            = "LOHN_EDIT_LOCKED",
                        message          = $"Saldo-Vortrag in Periode {per} kann nicht gelöscht werden — der Definitiv-Lohnlauf dieser Periode ist bereits abgeschlossen. Bitte zuerst den Lohnlauf wieder öffnen.",
                        firstAllowedDate = (string?)null
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

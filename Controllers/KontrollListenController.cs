using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Kontroll-Listen für HR — proaktiv Lücken erkennen, die im laufenden
/// Betrieb relevant sind (z.B. fehlende Dokumente, abgelaufene Pflichten).
///
/// Walter-Vorgabe 07.06.2026: erste Liste = MA, bei denen die QST-Befreiung
/// vom Ehegatten abhängt, aber das Beleg-Dokument (Ausweis Ehegatte)
/// noch nicht in der Personalakte ist.
/// </summary>
[Authorize(Roles = "admin,superuser,user")]
[ApiController]
[Route("api/kontrolle")]
public class KontrollListenController : ControllerBase
{
    private readonly AppDbContext _db;
    public KontrollListenController(AppDbContext db) => _db = db;

    /// <summary>
    /// GF-Zugang (Walter 16.07.2026): Rolle «user» sieht die Kontroll-Listen
    /// NUR fuer eine ihm zugeteilte Filiale (user_branch_access) — vorher 403
    /// fuer GF, obwohl die Card im HR-Hub sichtbar war. admin/superuser
    /// (inkl. buchhaltung via Doppel-Claim) bleiben unbeschraenkt.
    /// Liefert null wenn erlaubt, sonst das 403-Result.
    /// </summary>
    private async Task<IActionResult?> GuardBranchAsync(int? companyProfileId)
    {
        if (User.IsInRole("admin") || User.IsInRole("superuser")) return null;
        if (!companyProfileId.HasValue)
            return StatusCode(403, new { error = "BRANCH_REQUIRED",
                message = "Kontroll-Listen sind fuer GF nur pro Filiale verfuegbar — bitte Filiale im Selektor waehlen." });
        var idStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(idStr, out var uid))
            return StatusCode(403, new { error = "NO_USER" });
        var ok = await _db.UserBranchAccesses
            .AnyAsync(a => a.UserId == uid && a.CompanyProfileId == companyProfileId.Value);
        if (!ok)
            return StatusCode(403, new { error = "BRANCH_FORBIDDEN",
                message = "Kein Zugriff auf diese Filiale." });
        return null;
    }

    /// <summary>
    /// MA, die QST-pflichtig wären, aber durch den Ehegatten befreit
    /// werden könnten — und für die noch KEIN Ehegatten-Ausweis hinterlegt
    /// ist (Dokument-Typ mit linked_field_code='spouse').
    ///
    /// Filter:
    ///   1) IsActive = true
    ///   2) MA-Nationalität != CH
    ///   3) MA hat KEINE C-Bewilligung (jüngster History-Eintrag)
    ///   4) MA hat einen Ehepartner-Eintrag
    ///   5) Ehepartner ist CH-Bürger ODER hat C-Bewilligung
    ///   6) Beim MA existiert KEIN Dokument vom Typ linked_field_code='spouse'
    /// </summary>
    [HttpGet("spouse-doku-fehlt")]
    public async Task<IActionResult> SpouseDokuFehlt([FromQuery] int? companyProfileId = null)
    {
        var deny = await GuardBranchAsync(companyProfileId);
        if (deny != null) return deny;

        // 1) Aktive MA (optional auf Filiale beschränkt) + neueste C-Permit-Info
        // Walter-Vorgabe 13.06.2026: Phantom-MA (IsPayrollExcluded=true) UND
        // soft-deleted MA (IsHidden=true) sind für die HR-Kontrollen irrelevant.
        var empQuery = _db.Employees
            .Include(e => e.NationalityRef)
            .Where(e => e.IsActive
                     && !e.IsPayrollExcluded
                     && !e.IsHidden
                     && !e.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
        {
            var cpid = companyProfileId.Value;
            // Filial-Zuordnung (Walter-Bug 15.07.2026): aktive Verträge zählen;
            // ohne aktiven Vertrag der jüngste — beendete Fremd-Filial-Verträge nicht.
            empQuery = empQuery.Where(e =>
                e.Employments.Any(em => em.IsActive && em.CompanyProfileId == cpid)
                || (!e.Employments.Any(em => em.IsActive)
                    && e.Employments.OrderByDescending(em => em.ContractStartDate)
                        .Select(em => em.CompanyProfileId).FirstOrDefault() == cpid));
        }
        var emps = await empQuery
            .Select(e => new {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
                NatCode = e.NationalityRef != null ? e.NationalityRef.Code : null
            })
            .ToListAsync();
        var empIds = emps.Select(e => e.Id).ToList();
        if (empIds.Count == 0) return Ok(Array.Empty<object>());

        // 2) Neueste Bewilligung pro MA — gleiche „neueste"-Logik wie überall:
        //    max(ValidTo) → bei Gleichheit min(ValidFrom).
        var maxDate = new DateOnly(9999, 12, 31);
        var histAll = await _db.EmployeePermitHistories
            .Include(h => h.PermitType)
            .Where(h => empIds.Contains(h.EmployeeId) && h.PermitTypeId != null)
            .ToListAsync();
        var newestPermitByEmp = histAll
            .GroupBy(h => h.EmployeeId)
            .ToDictionary(g => g.Key, g => g
                .OrderByDescending(x => x.ValidTo ?? maxDate)
                .ThenBy(x => x.ValidFrom)
                .ThenBy(x => x.Id)
                .First());

        // 3) Ehepartner-Einträge pro MA (es kann theoretisch mehrere geben —
        //    wir nehmen den ersten als „aktuellen" Ehepartner).
        var spouses = await _db.EmployeeFamilyMembers
            .Include(f => f.NationalityRef)
            .Include(f => f.PermitType)
            .Where(f => f.MemberType == "Ehepartner" && empIds.Contains(f.EmployeeId))
            .ToListAsync();
        var spouseByEmp = spouses
            .GroupBy(f => f.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        // 4) Spouse-Doku-Check (Walter-Vorgabe 13.06.2026): vereinheitlicht
        // mit QstPflichtCheckService — explizite Verknüpfung über
        // employee_family_member.DokumentId statt linked_field_code-Scan.
        // Damit zeigen Kontroll-Liste, QST-Banner und Dashboard EXAKT das
        // gleiche Ergebnis. Plus: die Doku-ID muss tatsächlich noch
        // existieren (wurde nicht zwischenzeitlich gelöscht).
        var existingDokIds = await _db.EmployeeDokumente
            .Where(d => empIds.Contains(d.EmployeeId))
            .Select(d => d.Id)
            .ToListAsync();
        var dokIdSet = new HashSet<int>(existingDokIds);
        bool SpouseDokVerknuepft(EmployeeFamilyMember sp) =>
            sp.DokumentId.HasValue && dokIdSet.Contains(sp.DokumentId.Value);

        // 5) Filter zusammensetzen
        var result = emps
            .Where(e =>
                // MA nicht CH
                !string.Equals(e.NatCode, "CH", StringComparison.OrdinalIgnoreCase)
                // MA hat KEINE C-Bewilligung
                && !(newestPermitByEmp.TryGetValue(e.Id, out var p)
                     && string.Equals(p.PermitType?.Code, "C", StringComparison.OrdinalIgnoreCase))
                // Ehepartner vorhanden
                && spouseByEmp.TryGetValue(e.Id, out var sp)
                // Ehepartner CH-Bürger ODER mit C-Bewilligung
                && (
                    string.Equals(sp.NationalityRef?.Code, "CH", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(sp.PermitType?.Code, "C", StringComparison.OrdinalIgnoreCase)
                )
                // Keine Spouse-Doku verknüpft (oder Ziel-Doku gelöscht)
                && !SpouseDokVerknuepft(sp)
            )
            .Select(e =>
            {
                var maPermit = newestPermitByEmp.TryGetValue(e.Id, out var p2) ? p2 : null;
                var sp       = spouseByEmp[e.Id];
                return new {
                    employeeId          = e.Id,
                    employeeNumber      = e.EmployeeNumber,
                    employeeName        = ($"{e.FirstName} {e.LastName}").Trim(),
                    employeeNationality = e.NatCode,
                    employeePermitCode  = maPermit?.PermitType?.Code,
                    spouseName          = ($"{sp.FirstName} {sp.LastName}").Trim(),
                    spouseNationality   = sp.NationalityRef?.Code,
                    spousePermitCode    = sp.PermitType?.Code,
                    reason = string.Equals(sp.NationalityRef?.Code, "CH", StringComparison.OrdinalIgnoreCase)
                                ? "Ehepartner ist CH-Bürger"
                                : "Ehepartner hat C-Bewilligung"
                };
            })
            .OrderBy(r => r.employeeName)
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Walter-Vorgabe 13.06.2026: MA, deren QST-Befreiung an einem eigenen
    /// Ausweis hängt, aber das Beleg-Dokument noch nicht verknüpft ist.
    ///
    /// Zwei Varianten:
    ///   • CH-Bürger (NationalityRef.Code = "CH") ohne `id_pass_dokument_id`
    ///   • C-Ausweis-Inhaber ohne Beleg-Doku am C-Eintrag
    ///     (`PermitHistory.DokumentId`, Fallback `c_ausweis_dokument_id`)
    ///
    /// Walter-Bug 22.07.2026: die Liste prüfte nur noch das alte Feld
    /// `employee.c_ausweis_dokument_id`. Seit 14.06.2026 hängt das Beleg-Doku
    /// an der Permit-History — MA mit grünem «👁 Doku» landeten fälschlich
    /// in der Liste. Logik jetzt identisch zu QstPflichtCheckService.
    ///
    /// Skip:
    ///   - IsActive=false, IsHidden=true, IsPayrollExcluded=true (Phantom)
    ///   - `+alt`-Suffix-MA (Pre-Mirus-Archiv)
    ///   - MA mit Behörden-Befreiung (`QstBefreitDurchBehoerde=true`) — die
    ///     stützen die Befreiung auf das Behördenschreiben, nicht den Ausweis.
    /// </summary>
    [HttpGet("employee-ausweis-fehlt")]
    public async Task<IActionResult> EmployeeAusweisFehlt([FromQuery] int? companyProfileId = null)
    {
        var deny = await GuardBranchAsync(companyProfileId);
        if (deny != null) return deny;

        var empQuery = _db.Employees
            .Include(e => e.NationalityRef)
            .Where(e => e.IsActive
                     && !e.IsHidden
                     && !e.IsPayrollExcluded
                     && !e.QstBefreitDurchBehoerde
                     && !e.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
        {
            var cpid = companyProfileId.Value;
            // Filial-Zuordnung (Walter-Bug 15.07.2026): aktive Verträge zählen;
            // ohne aktiven Vertrag der jüngste — beendete Fremd-Filial-Verträge nicht.
            empQuery = empQuery.Where(e =>
                e.Employments.Any(em => em.IsActive && em.CompanyProfileId == cpid)
                || (!e.Employments.Any(em => em.IsActive)
                    && e.Employments.OrderByDescending(em => em.ContractStartDate)
                        .Select(em => em.CompanyProfileId).FirstOrDefault() == cpid));
        }

        var emps = await empQuery
            .Select(e => new {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
                NatCode               = e.NationalityRef != null ? e.NationalityRef.Code : null,
                IdPassDokumentId      = e.IdPassDokumentId,
                CAusweisDokumentId    = e.CAusweisDokumentId
            })
            .ToListAsync();

        var empIds = emps.Select(e => e.Id).ToList();
        if (empIds.Count == 0) return Ok(Array.Empty<object>());

        // C-Einträge pro MA — gleiche Regel wie QstPflichtCheckService
        // («einmal C immer C»): irgend ein C-Eintrag reicht; Beleg-Doku am
        // jüngsten C-Eintrag (ValidFrom desc), Fallback altes Employee-Feld.
        var cHistAll = await _db.EmployeePermitHistories
            .AsNoTracking()
            .Include(h => h.PermitType)
            .Where(h => empIds.Contains(h.EmployeeId)
                     && h.PermitType != null
                     && h.PermitType.Code == "C")
            .ToListAsync();
        var cEintragByEmp = cHistAll
            .GroupBy(h => h.EmployeeId)
            .ToDictionary(g => g.Key, g => g
                .OrderByDescending(x => x.ValidFrom)
                .ThenByDescending(x => x.Id)
                .First());

        var result = new List<object>();
        foreach (var e in emps)
        {
            bool isCh = string.Equals(e.NatCode, "CH", StringComparison.OrdinalIgnoreCase);

            if (isCh)
            {
                // CH-Bürger → braucht id_pass_dokument_id
                if (e.IdPassDokumentId.HasValue) continue;
                result.Add(new {
                    employeeId      = e.Id,
                    employeeNumber  = e.EmployeeNumber,
                    employeeName    = ($"{e.FirstName} {e.LastName}").Trim(),
                    kind            = "CH-Buerger",
                    reason          = "CH-Bürger — ID oder Pass fehlt",
                    permitCode      = "CH"
                });
                continue;
            }

            // Nicht-CH → C-Ausweis-Inhaber ohne verknüpftes Beleg-Dokument
            if (!cEintragByEmp.TryGetValue(e.Id, out var cEintrag)) continue;
            int? belegDokId = cEintrag.DokumentId ?? e.CAusweisDokumentId;
            if (belegDokId.HasValue) continue;

            result.Add(new {
                employeeId      = e.Id,
                employeeNumber  = e.EmployeeNumber,
                employeeName    = ($"{e.FirstName} {e.LastName}").Trim(),
                kind            = "C-Ausweis",
                reason          = "C-Ausweis — Bewilligungs-Dokument fehlt",
                permitCode      = "C"
            });
        }

        return Ok(result.OrderBy(r => ((dynamic)r).employeeName).ToList());
    }

    /// <summary>
    /// Walter-Vorgabe 13.06.2026: abgelaufene + bald ablaufende Bewilligungen
    /// — analog der Dashboard-Card „Bewilligungen laufen ab". Pro MA der
    /// JÜNGSTE Permit-History-Eintrag (ValidFrom desc), wenn dessen ValidTo
    /// &lt;= heute+60 (Walter 14.06.2026 — Cutoff von 90 auf 60 Tage gesenkt;
    /// die alte blaue „info"-Stufe 60–90 Tage Vorlauf braucht keine Warnung).
    ///
    /// Filter:
    ///   • IsActive, !IsHidden, kein `+alt`-Suffix, optional Filiale
    ///   • Skip wenn der MA vor Ablauf austritt (ExitDate &lt;= ValidTo) —
    ///     Erneuerung wäre unnötig (gleiche Logik wie im DashboardService).
    /// </summary>
    [HttpGet("permit-expiring")]
    public async Task<IActionResult> PermitExpiring([FromQuery] int? companyProfileId = null)
    {
        var deny = await GuardBranchAsync(companyProfileId);
        if (deny != null) return deny;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var limit = DateOnly.FromDateTime(DateTime.Today.AddDays(60));

        var empQuery = _db.Employees
            .Where(e => e.IsActive
                     && !e.IsHidden
                     && !e.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
        {
            var cpid = companyProfileId.Value;
            // Filial-Zuordnung (Walter-Bug 15.07.2026): aktive Verträge zählen;
            // ohne aktiven Vertrag der jüngste — beendete Fremd-Filial-Verträge nicht.
            empQuery = empQuery.Where(e =>
                e.Employments.Any(em => em.IsActive && em.CompanyProfileId == cpid)
                || (!e.Employments.Any(em => em.IsActive)
                    && e.Employments.OrderByDescending(em => em.ContractStartDate)
                        .Select(em => em.CompanyProfileId).FirstOrDefault() == cpid));
        }
        var emps = await empQuery
            .Select(e => new
            {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.ExitDate,
                e.IsPayrollExcluded,
                NationalityCode   = e.NationalityRef != null ? e.NationalityRef.Code : null,
                NationalityLegacy = e.Nationality
            })
            .ToListAsync();
        var empIds = emps.Select(e => e.Id).ToList();
        if (empIds.Count == 0) return Ok(Array.Empty<object>());

        var histAll = await _db.EmployeePermitHistories
            .Include(h => h.PermitType)
            .Where(h => empIds.Contains(h.EmployeeId) && h.PermitTypeId != null)
            .ToListAsync();

        // Bewilligung fehlt KOMPLETT (Walter-Vorgabe 12.07.2026, Mariangela):
        // aktiver Ausländer ohne jeglichen Historie-Eintrag (auch kein CH-/
        // Einbürgerungs-Eintrag mit PermitTypeId NULL). Gleiche Logik wie die
        // Dashboard-Karte permit_missing — sonst meldet die Liste fälschlich
        // «keine offenen Lücken». Phantom-MA + unbekannte Nationalität: kein Alarm.
        var idsMitIrgendeinemEintrag = (await _db.EmployeePermitHistories
            .Where(h => empIds.Contains(h.EmployeeId))
            .Select(h => h.EmployeeId)
            .Distinct()
            .ToListAsync()).ToHashSet();
        var fehltRows = emps
            .Where(e => !e.IsPayrollExcluded && !idsMitIrgendeinemEintrag.Contains(e.Id))
            .Where(e =>
            {
                var nat = (e.NationalityCode ?? e.NationalityLegacy ?? "").Trim().ToUpperInvariant();
                return nat.Length > 0
                    && nat is not ("CH" or "SCHWEIZ" or "SWITZERLAND" or "SUISSE" or "SVIZZERA");
            })
            .Select(e => new {
                employeeId      = e.Id,
                employeeNumber  = e.EmployeeNumber,
                employeeName    = ($"{e.FirstName} {e.LastName}").Trim(),
                permitCode      = "—",
                validTo         = (string?)null,
                daysUntil       = int.MinValue,   // sortiert VOR allen abgelaufenen
                severity        = "expired",
                reason          = "Keine Aufenthaltsbewilligung erfasst — Beschäftigung ohne Bewilligung unzulässig"
            })
            .ToList();

        var empById = emps.ToDictionary(e => e.Id);
        // Massgebender Eintrag (Walter-Bug 15.07.2026, «Berin» — gleiche Regel
        // wie DashboardService): unplausible Zeilen (Ende < Beginn) ignorieren,
        // heute gueltige Bewilligung gewinnt, sonst spaetestes Ende.
        var kontrolleHeute = DateOnly.FromDateTime(DateTime.Today);
        var youngestPerEmp = histAll
            .GroupBy(h => h.EmployeeId)
            .Select(g =>
            {
                var pool = g.Where(x => !x.ValidTo.HasValue || x.ValidTo.Value >= x.ValidFrom).ToList();
                if (pool.Count == 0) pool = g.ToList();
                return pool
                    .OrderByDescending(x => (x.ValidFrom <= kontrolleHeute
                        && (!x.ValidTo.HasValue || x.ValidTo.Value >= kontrolleHeute)) ? 1 : 0)
                    .ThenByDescending(x => x.ValidTo ?? DateOnly.MaxValue)
                    .ThenByDescending(x => x.Id)
                    .First();
            })
            .Where(h => h.ValidTo.HasValue && h.ValidTo.Value <= limit)
            .Where(h =>
            {
                if (!empById.TryGetValue(h.EmployeeId, out var e)) return false;
                if (e.ExitDate.HasValue)
                {
                    var exitDateOnly = DateOnly.FromDateTime(e.ExitDate.Value);
                    if (h.ValidTo!.Value >= exitDateOnly) return false;
                }
                return true;
            })
            .ToList();

        var result = youngestPerEmp.Select(h =>
        {
            var e        = empById[h.EmployeeId];
            var validTo  = h.ValidTo!.Value;
            var days     = validTo.DayNumber - today.DayNumber;
            var permitCd = h.PermitType?.Code ?? "?";
            string reason;
            string severity;
            if (days < 0)
            {
                reason   = $"Bewilligung {permitCd} ist seit {-days} Tag(en) abgelaufen";
                severity = "expired";
            }
            else if (days == 0)
            {
                reason   = $"Bewilligung {permitCd} läuft heute ab";
                severity = "critical";
            }
            else if (days <= 30)
            {
                reason   = $"Bewilligung {permitCd} läuft in {days} Tagen ab";
                severity = "critical";
            }
            else
            {
                // 31–60 Tage Vorlauf — höher liegende Werte werden bereits
                // durch den 60-Tage-Cutoff im Query rausgefiltert (Walter
                // 14.06.2026: blaue „info"-Stufe entfernt).
                reason   = $"Bewilligung {permitCd} läuft in {days} Tagen ab";
                severity = "warning";
            }
            return new {
                employeeId      = e.Id,
                employeeNumber  = e.EmployeeNumber,
                employeeName    = ($"{e.FirstName} {e.LastName}").Trim(),
                permitCode      = permitCd,
                validTo         = (string?)validTo.ToString("yyyy-MM-dd"),
                daysUntil       = days,
                severity,
                reason
            };
        })
        .Concat(fehltRows)                    // «fehlt komplett» (daysUntil=MinValue → ganz oben)
        .OrderBy(r => r.daysUntil)            // abgelaufene (neg.) zuerst, dann nächste Termine
        .ThenBy(r => r.employeeName)
        .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Walter-Vorgabe 20.06.2026 (ArG): MA mit ≥ 25 gearbeiteten Nächten/Jahr
    /// (rollende 12 Monate, bei &lt; 12 Datenmonaten hochgerechnet) OHNE gültige
    /// Nachtarbeit-Untersuchung — Arztzeugnis/Verzicht fehlt, hat kein
    /// Ausstellungsdatum oder ist abgelaufen. IDENTISCHE Zähl-Logik wie der
    /// Dashboard-Block „night_work_exam_fehlt" und die Ferien/Nacht-Liste.
    ///
    /// Filter: IsActive, !IsHidden, !IsPayrollExcluded (Phantom), kein `+alt`,
    /// optional Filiale. Stichtag = heute (Exam-Status kommt live vom MA →
    /// Erfassen entfernt die Lücke sofort).
    /// </summary>
    [HttpGet("nacht-untersuchung-fehlt")]
    public async Task<IActionResult> NachtUntersuchungFehlt([FromQuery] int? companyProfileId = null)
    {
        var deny = await GuardBranchAsync(companyProfileId);
        if (deny != null) return deny;

        var today     = DateOnly.FromDateTime(DateTime.Today);
        var rollStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-11);

        var empQuery = _db.Employees
            .Where(e => e.IsActive
                     && !e.IsHidden
                     && !e.IsPayrollExcluded
                     && !e.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
        {
            var cpid = companyProfileId.Value;
            // Filial-Zuordnung (Walter-Bug 15.07.2026): aktive Verträge zählen;
            // ohne aktiven Vertrag der jüngste — beendete Fremd-Filial-Verträge nicht.
            empQuery = empQuery.Where(e =>
                e.Employments.Any(em => em.IsActive && em.CompanyProfileId == cpid)
                || (!e.Employments.Any(em => em.IsActive)
                    && e.Employments.OrderByDescending(em => em.ContractStartDate)
                        .Select(em => em.CompanyProfileId).FirstOrDefault() == cpid));
        }
        var emps = await empQuery
            .Select(e => new {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
                e.NightWorkExamDokumentId, e.NightWorkAusnahmeDokumentId,
                e.ExitDate, e.KuendigungPer
            })
            .ToListAsync();
        // Gekündigte / austretende MA nicht listen (Walter 06.09.2026).
        emps = emps.Where(e => !NightWorkComplianceService.Ausgenommen(e.ExitDate, e.KuendigungPer)).ToList();
        var empIds = emps.Select(e => e.Id).ToList();
        if (empIds.Count == 0) return Ok(Array.Empty<object>());

        // Nacht-Tage (nur Tage mit Nachtstunden) — distinct pro MA.
        var nightDays = await _db.EmployeeTimeEntries.AsNoTracking()
            .Where(t => empIds.Contains(t.EmployeeId) && t.EntryDate >= rollStart
                     && t.EntryDate <= today && (t.NightHours ?? 0m) > 0m)
            .Select(t => new { t.EmployeeId, t.EntryDate })
            .Distinct().ToListAsync();
        var nightDatesByEmp = nightDays.GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.EntryDate).ToList());

        var result = new List<object>();
        foreach (var e in emps)
        {
            if (!nightDatesByEmp.TryGetValue(e.Id, out var dates) || dates.Count == 0) continue;

            // NEUE Regel: > 18 Nächte in einem rollierenden 6-Wochen-Fenster.
            var nw = NightWorkComplianceService.Evaluate(dates, today);
            if (!nw.RequiresDocuments) continue;                 // max ≤ 18 → nicht listen

            // Nachweise vollständig = Arztzeugnis/Verzicht UND Checkliste/Ausnahmeregelung.
            bool hasExam     = e.NightWorkExamDokumentId.HasValue;
            bool hasChecklist = e.NightWorkAusnahmeDokumentId.HasValue;
            if (hasExam && hasChecklist) continue;               // vollständig → nicht listen

            string grund =
                  (!hasExam && !hasChecklist) ? "Arztzeugnis/Verzicht und Ausnahmeregelung fehlen"
                : (!hasExam)                  ? "Arztzeugnis/Verzicht fehlt"
                :                               "Ausnahmeregelung (Checkliste) fehlt";

            result.Add(new {
                employeeId       = e.Id,
                employeeNumber   = e.EmployeeNumber,
                employeeName     = ($"{e.FirstName} {e.LastName}").Trim(),
                maxNaechte6Wochen = nw.MaxNightsInSixWeeks,
                windowFrom       = nw.WindowFrom?.ToString("yyyy-MM-dd"),
                windowTo         = nw.WindowTo?.ToString("yyyy-MM-dd"),
                reason           = grund
            });
        }

        return Ok(result.OrderByDescending(r => ((dynamic)r).maxNaechte6Wochen).ToList());
    }
}

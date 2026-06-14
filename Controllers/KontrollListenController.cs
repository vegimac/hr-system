using HrSystem.Data;
using HrSystem.Models;
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
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/kontrolle")]
public class KontrollListenController : ControllerBase
{
    private readonly AppDbContext _db;
    public KontrollListenController(AppDbContext db) => _db = db;

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
            empQuery = empQuery.Where(e => e.Employments.Any(em => em.CompanyProfileId == cpid));
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
    /// Ausweis hängt, aber das Beleg-Dokument noch nicht am MA verknüpft ist.
    ///
    /// Zwei Varianten:
    ///   • CH-Bürger (NationalityRef.Code = "CH") ohne `id_pass_dokument_id`
    ///   • C-Ausweis-Inhaber (jüngster PermitHistory-Eintrag = "C") ohne
    ///     `c_ausweis_dokument_id`
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
            empQuery = empQuery.Where(e => e.Employments.Any(em => em.CompanyProfileId == cpid));
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

        // Neueste Bewilligung pro MA — gleiche „neueste"-Logik wie überall:
        // max(ValidTo) → bei Gleichheit min(ValidFrom).
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

            // Nicht-CH → prüfen ob aktiver C-Ausweis vorliegt
            if (!newestPermitByEmp.TryGetValue(e.Id, out var p)) continue;
            bool isC = string.Equals(p.PermitType?.Code, "C", StringComparison.OrdinalIgnoreCase);
            if (!isC) continue;

            if (e.CAusweisDokumentId.HasValue) continue;
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
        var today = DateOnly.FromDateTime(DateTime.Today);
        var limit = DateOnly.FromDateTime(DateTime.Today.AddDays(60));

        var empQuery = _db.Employees
            .Where(e => e.IsActive
                     && !e.IsHidden
                     && !e.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
        {
            var cpid = companyProfileId.Value;
            empQuery = empQuery.Where(e => e.Employments.Any(em => em.CompanyProfileId == cpid));
        }
        var emps = await empQuery
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.ExitDate })
            .ToListAsync();
        var empIds = emps.Select(e => e.Id).ToList();
        if (empIds.Count == 0) return Ok(Array.Empty<object>());

        var histAll = await _db.EmployeePermitHistories
            .Include(h => h.PermitType)
            .Where(h => empIds.Contains(h.EmployeeId) && h.PermitTypeId != null)
            .ToListAsync();

        var empById = emps.ToDictionary(e => e.Id);
        var youngestPerEmp = histAll
            .GroupBy(h => h.EmployeeId)
            .Select(g => g.OrderByDescending(x => x.ValidFrom).ThenByDescending(x => x.Id).First())
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
                validTo         = validTo.ToString("yyyy-MM-dd"),
                daysUntil       = days,
                severity,
                reason
            };
        })
        .OrderBy(r => r.daysUntil)            // abgelaufene (neg.) zuerst, dann nächste Termine
        .ThenBy(r => r.employeeName)
        .ToList();

        return Ok(result);
    }
}

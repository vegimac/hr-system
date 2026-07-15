using HrSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Walter-Vorgabe 27.05.2026: globale Suche ueber Mitarbeiter, Vertraege,
/// Dokumente und Posteingang. Aufrufer ist die Cmd-K-Suche im Frontend.
///
/// Limits: pro Quelle max. 10 Treffer (Performance + Lesbarkeit). Suche
/// ist case-insensitive (ILIKE %q%).
/// </summary>
[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly AppDbContext _db;
    public SearchController(AppDbContext db) { _db = db; }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(new { employees = Array.Empty<object>(), contracts = Array.Empty<object>(), documents = Array.Empty<object>(), mailbox = Array.Empty<object>() });

        // Walter-Vorgabe 27.05.2026: Multi-Token-Suche. „Senada & Ausweis"
        // wird zu zwei Tokens; JEDER muss irgendwo matchen (AND), aber
        // jeder darf in einem ANDEREN Feld treffen (z.B. „Senada" im Namen,
        // „Ausweis" im Permit-Typ oder im Dokument-Filename). Damit verhaelt
        // sich die Suche „semantisch" ohne KI-API.
        var tokens = q.Trim().Split(new[] { ' ', '\t', '&', '+', ',' }, StringSplitOptions.RemoveEmptyEntries)
                              .Where(t => t.Length >= 2)
                              .Select(t => t.Trim())
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .Take(5)  // mehr als 5 Tokens machen die Query langsam und sind unrealistisch
                              .ToList();
        if (tokens.Count == 0)
            return Ok(new { employees = Array.Empty<object>(), contracts = Array.Empty<object>(), documents = Array.Empty<object>(), mailbox = Array.Empty<object>() });

        var s = q.Trim();

        // ── Mitarbeiter ──────────────────────────────────────────────
        // Match jedes Tokens auf: Vor-/Nachname, MA-Nr, AHV-Nr, Permit-Code
        // (z.B. „B"/„C"/„L") oder Permit-Beschreibung („Aufenthaltsbewilligung").
        // AHV-Ziffern-Match nur fuer ZIFFERN-Tokens (>= 4 Stellen).
        IQueryable<HrSystem.Models.Employee> eq = _db.Employees;
        foreach (var t in tokens)
        {
            var like = "%" + t + "%";
            var digits = new string(t.Where(char.IsDigit).ToArray());
            // Lokale Kopien sind in EF noetig fuer korrekte Parameter-Bindung.
            var likeLoc = like;
            var digitsLoc = digits;
            eq = eq.Where(e =>
                EF.Functions.ILike(e.FirstName ?? "", likeLoc)
             || EF.Functions.ILike(e.LastName ?? "", likeLoc)
             || EF.Functions.ILike(((e.FirstName ?? "") + " " + (e.LastName ?? "")), likeLoc)
             || EF.Functions.ILike(e.EmployeeNumber ?? "", likeLoc)
             || (e.SocialSecurityNumber != null && EF.Functions.ILike(e.SocialSecurityNumber, likeLoc))
             || (digitsLoc.Length >= 4 && e.SocialSecurityNumber != null
                  && EF.Functions.ILike(e.SocialSecurityNumber.Replace(".", "").Replace(" ", ""), "%" + digitsLoc + "%"))
             || (e.PermitType != null && (
                    EF.Functions.ILike(e.PermitType.Code        ?? "", likeLoc)
                 || EF.Functions.ILike(e.PermitType.Description ?? "", likeLoc)))
            );
        }
        var employees = await eq
            .OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .Take(10)
            .Select(e => new {
                id              = e.Id,
                firstName       = e.FirstName,
                lastName        = e.LastName,
                employeeNumber  = e.EmployeeNumber,
                isActive        = e.IsActive,
                ssn             = e.SocialSecurityNumber,
                permitCode      = e.PermitType != null ? e.PermitType.Code : null,
                permitDesc      = e.PermitType != null ? e.PermitType.Description : null,
                // Filiale des jüngsten Vertrags (Walter-Vorgabe 10.07.2026) —
                // z.B. «104 Langenthal»; auch bei inaktiven MA hilfreich, um zu
                // sehen, wo die Person zugeordnet war.
                branch          = _db.Employments
                    .Where(c => c.EmployeeId == e.Id && c.CompanyProfileId != null)
                    .OrderByDescending(c => c.ContractStartDate)
                    .Select(c => ((c.CompanyProfile!.RestaurantCode ?? "") + " "
                                + (c.CompanyProfile.City ?? c.CompanyProfile.BranchName ?? c.CompanyProfile.CompanyName)).Trim())
                    .FirstOrDefault(),
            })
            .ToListAsync();

        // ── Vertraege ────────────────────────────────────────────────
        IQueryable<HrSystem.Models.Employment> cq = _db.Employments.Include(c => c.Employee);
        foreach (var t in tokens)
        {
            var likeLoc = "%" + t + "%";
            cq = cq.Where(c =>
                EF.Functions.ILike(c.JobTitle ?? "", likeLoc)
             || EF.Functions.ILike(c.EmploymentModel ?? "", likeLoc)
             || (c.Employee != null && (
                    EF.Functions.ILike(c.Employee.FirstName ?? "", likeLoc)
                 || EF.Functions.ILike(c.Employee.LastName ?? "", likeLoc)
                 || EF.Functions.ILike(c.Employee.EmployeeNumber ?? "", likeLoc))));
        }
        var contracts = await cq
            .OrderByDescending(c => c.ContractStartDate)
            .Take(10)
            .Select(c => new {
                id              = c.Id,
                employeeId      = c.EmployeeId,
                employeeName    = (c.Employee != null ? (c.Employee.FirstName + " " + c.Employee.LastName) : null),
                jobTitle        = c.JobTitle,
                model           = c.EmploymentModel,
                startDate       = c.ContractStartDate,
                endDate         = c.ContractEndDate,
                isActive        = c.IsActive,
                hourlyRate      = c.HourlyRate,
                monthlySalary   = c.MonthlySalary,
            })
            .ToListAsync();

        // ── Dokumente (MA-Dokumente) ─────────────────────────────────
        // Token-by-token Filter — jeder Token muss in filename, bemerkung
        // ODER MA-Name (via SubQuery) matchen.
        IQueryable<HrSystem.Models.EmployeeDokument> dq = _db.EmployeeDokumente;
        foreach (var t in tokens)
        {
            var likeLoc = "%" + t + "%";
            dq = dq.Where(d =>
                EF.Functions.ILike(d.FilenameOriginal ?? "", likeLoc)
             || (d.Bemerkung != null && EF.Functions.ILike(d.Bemerkung, likeLoc))
             || _db.Employees.Any(e => e.Id == d.EmployeeId && (
                    EF.Functions.ILike(e.FirstName ?? "", likeLoc)
                 || EF.Functions.ILike(e.LastName ?? "", likeLoc)
                 || EF.Functions.ILike(((e.FirstName ?? "") + " " + (e.LastName ?? "")), likeLoc)
                 || EF.Functions.ILike(e.EmployeeNumber ?? "", likeLoc))));
        }
        var documentsRaw = await dq
            .OrderByDescending(d => d.HochgeladenAm)
            .Take(10)
            .Select(d => new {
                id            = d.Id,
                filename      = d.FilenameOriginal,
                bemerkung     = d.Bemerkung,
                employeeId    = d.EmployeeId,
                uploadedAt    = d.HochgeladenAm,
            })
            .ToListAsync();
        var docEmpIds = documentsRaw.Select(d => d.employeeId).Distinct().ToList();
        var docEmps = await _db.Employees
            .Where(e => docEmpIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber })
            .ToDictionaryAsync(x => x.Id, x => x);
        var documents = documentsRaw.Select(d => new {
            d.id, d.filename, d.bemerkung, d.employeeId, d.uploadedAt,
            employeeName   = docEmps.TryGetValue(d.employeeId, out var n) ? (n.FirstName + " " + n.LastName).Trim() : null,
            // Suche geht ueber ALLE Filialen → MA-Nummer zeigt die Filiale (Walter 15.07.2026).
            employeeNumber = docEmps.TryGetValue(d.employeeId, out var n2) ? n2.EmployeeNumber : null,
        }).ToList();

        // ── Posteingang (Mailbox-Dokumente) ─────────────────────────
        IQueryable<HrSystem.Models.MailboxDocument> mq = _db.MailboxDocuments;
        foreach (var t in tokens)
        {
            var likeLoc = "%" + t + "%";
            mq = mq.Where(m =>
                EF.Functions.ILike(m.OriginalFilename ?? "", likeLoc)
             || (m.Bemerkung   != null && EF.Functions.ILike(m.Bemerkung,   likeLoc))
             || (m.MessageBody != null && EF.Functions.ILike(m.MessageBody, likeLoc)));
        }
        var mailbox = await mq
            .OrderByDescending(m => m.UploadedAt)
            .Take(10)
            .Select(m => new {
                id                = m.Id,
                filename          = m.OriginalFilename,
                description       = m.Bemerkung,
                targetType        = m.TargetType,
                uploadedAt        = m.UploadedAt,
                companyProfileId  = m.CompanyProfileId,
                employeeId        = m.EmployeeId,
            })
            .ToListAsync();

        return Ok(new { employees, contracts, documents, mailbox });
    }
}

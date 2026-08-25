using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Notfallkontakte-Liste pro Filiale (Walter-Vorgabe 25.08.2026) —
/// GET-only PDF-Generator für den Aushang im Restaurant. Kein Lohn-Edit,
/// im EditLock-Audit unkritisch (analog BewerbungsbogenController).
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superuser,user")]
[Route("api/notfall-liste")]
public class NotfallListeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly NotfallListePdfService _pdf;

    public NotfallListeController(AppDbContext db, NotfallListePdfService pdf)
    {
        _db = db;
        _pdf = pdf;
    }

    /// <summary>GET /api/notfall-liste/pdf?companyProfileId=…</summary>
    [HttpGet("pdf")]
    public async Task<IActionResult> GetPdf([FromQuery] int companyProfileId)
    {
        if (companyProfileId <= 0)
            return BadRequest(new { error = "FILIALE_FEHLT", message = "Bitte eine Filiale wählen." });

        var cp = await _db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == companyProfileId);
        if (cp is null)
            return NotFound(new { error = "FILIALE_NICHT_GEFUNDEN", message = "Filiale nicht gefunden." });

        // Aktive MA mit heute laufendem Vertrag in dieser Filiale (gleiche
        // Semantik wie der Alters-Report); Phantom-MA ohne Lohn bleiben weg.
        var heute = DateTime.Today;
        var empIds = await _db.Employments.AsNoTracking()
            .Where(em => em.CompanyProfileId == companyProfileId
                      && em.IsActive
                      && em.ContractStartDate <= heute
                      && (em.ContractEndDate == null || em.ContractEndDate >= heute))
            .Select(em => em.EmployeeId)
            .Distinct()
            .ToListAsync();

        // Walter 25.08.2026 (final): Austretende MA kommen NICHT auf den
        // Aushang — MIT einer Ausnahme für Befristungen. Regeln:
        //   1) Erfasste Kündigung/Aufhebung (Kündigungs-Felder/Austrittsgrund) → raus.
        //   2) Austrittsdatum gesetzt → raus, AUSSER es liegt «ziemlich genau»
        //      6 Monate nach dem Eintritt (±30 Tage) = typische Befristung,
        //      die i.d.R. verlängert wird → bleibt drauf.
        //   3) Zweifelsfall (z.B. Eintritt unbekannt) → auf die Liste.
        var empsRaw = await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id) && e.IsActive && !e.IsPayrollExcluded
                     && e.KuendigungPer == null
                     && e.KuendigungAusgesprochenAm == null
                     && (e.Austrittsgrund == null || e.Austrittsgrund == ""))
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
                               e.EntryDate, e.ExitDate,
                               e.NotfallFamilyMemberId, e.NotfallName,
                               e.NotfallBeziehung, e.NotfallTelefon })
            .ToListAsync();
        var emps = empsRaw.Where(e =>
        {
            if (e.ExitDate == null) return true;                 // kein Austritt
            if (e.EntryDate == null) return true;                // Zweifelsfall → Liste
            var sechsMonate = e.EntryDate.Value.AddMonths(6);
            return Math.Abs((e.ExitDate.Value - sechsMonate).TotalDays) <= 30; // Befristung
        }).ToList();

        // FK-Verknüpfungen auf Familienmitglieder LIVE auflösen.
        var fmIds = emps.Where(e => e.NotfallFamilyMemberId != null)
                        .Select(e => e.NotfallFamilyMemberId!.Value)
                        .Distinct().ToList();
        var fms = fmIds.Count == 0
            ? new Dictionary<int, (string Name, string? Typ, string? Tel)>()
            : (await _db.EmployeeFamilyMembers.AsNoTracking()
                .Where(f => fmIds.Contains(f.Id))
                .Select(f => new { f.Id, f.FirstName, f.LastName, f.MemberType, f.Phone })
                .ToListAsync())
                .ToDictionary(f => f.Id,
                              f => (Name: ($"{f.FirstName} {f.LastName}").Trim(),
                                    Typ: (string?)f.MemberType,
                                    Tel: f.Phone));

        // Walter-Konvention: nach Vorname sortieren, Tie-Break Nachname.
        var zeilen = emps
            .OrderBy(e => e.FirstName ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.LastName ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(e =>
            {
                string? nfName = null, nfBez = null, nfTel = null;
                if (e.NotfallFamilyMemberId != null
                    && fms.TryGetValue(e.NotfallFamilyMemberId.Value, out var fm))
                {
                    nfName = fm.Name;
                    nfBez  = fm.Typ;
                    nfTel  = fm.Tel;
                }
                else if (!string.IsNullOrWhiteSpace(e.NotfallName))
                {
                    nfName = e.NotfallName;
                    nfBez  = e.NotfallBeziehung;
                    nfTel  = e.NotfallTelefon;
                }
                return new NotfallListePdfService.NotfallListeZeile(
                    e.EmployeeNumber, e.FirstName, e.LastName, nfName, nfBez, nfTel);
            })
            .ToList();

        var titel = string.Join(" ", new[] { cp.RestaurantCode, cp.City }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (string.IsNullOrWhiteSpace(titel)) titel = cp.BranchName ?? "";

        byte[] bytes;
        try
        {
            bytes = _pdf.Generate(new NotfallListePdfService.NotfallListeInput(titel, zeilen));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "NOTFALL_LISTE_PDF_FEHLER",
                message = "Liste konnte nicht erzeugt werden.",
                detail = ex.GetBaseException().Message
            });
        }

        return File(bytes, "application/pdf", "Notfallkontakte.pdf");
    }
}

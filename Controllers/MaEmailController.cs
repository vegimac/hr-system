using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Gruppen-E-Mail an Mitarbeitende (Walter-Vorgabe 14.08.2026) — erste
/// geöffnete Funktion der «Mitarbeiter-Korrespondenz». Selektion:
/// Filiale (eine oder alle) × Vertragsmodell (FLEX/MTP/FIX/FIX-M, mehrere
/// wählbar) → Empfänger-Vorschau mit Abwahl → Versand als EINZELMAILS an
/// employee.email (kein CC/BCC — Adressen bleiben privat). Versand über
/// den bestehenden EmailService (SMTP-Konfig, Test-Redirect greift).
/// Anwendungsfall #1: Dienstplan-Handy-Link ans Management-Team (FIX-M).
/// Der Versand von Lohnbelegen bleibt bewusst GESCHLOSSEN.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/ma-email")]
public class MaEmailController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;

    public MaEmailController(AppDbContext db, EmailService email)
    {
        _db = db;
        _email = email;
    }

    // ── GET /api/ma-email/empfaenger?companyProfileId=&modelle=FIX-M,MTP ─
    /// <summary>
    /// Empfänger-Vorschau: aktive MA mit heute laufendem Vertrag der
    /// gewählten Modelle in der gewählten Filiale (leer = alle Filialen).
    /// MA ohne E-Mail-Adresse werden mitgeliefert (Anzeige «keine E-Mail»),
    /// sind aber nicht versandfähig.
    /// </summary>
    [HttpGet("empfaenger")]
    public async Task<IActionResult> Empfaenger(
        [FromQuery] int? companyProfileId, [FromQuery] string? modelle,
        [FromQuery] string? funktionen)
    {
        var wanted = (modelle ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(m => m.ToUpperInvariant())
            .ToHashSet();
        // Funktions-Filter (Walter 15.08.2026): JobGroup-Codes, leer = alle.
        var wantedFunk = (funktionen ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.ToUpperInvariant())
            .ToHashSet();

        var heute = DateTime.Today;
        var rows = await _db.Employments.AsNoTracking()
            .Where(em => em.IsActive
                      && em.ContractStartDate <= heute
                      && (em.ContractEndDate == null || em.ContractEndDate >= heute)
                      && em.Employee!.IsActive
                      && !em.Employee!.IsHidden
                      && !em.Employee!.IsPayrollExcluded
                      && (companyProfileId == null || em.CompanyProfileId == companyProfileId))
            .Select(em => new
            {
                em.EmployeeId, em.CompanyProfileId, em.ContractStartDate, em.EmploymentModel,
                em.JobGroupId, em.JobTitle,
                em.Employee!.FirstName, em.Employee!.LastName, em.Employee!.EmployeeNumber,
                em.Employee!.Email,
            })
            .ToListAsync();

        // JobGroup-Code + deutscher Anzeigename (app_text JOB_GROUP).
        var jobGroups = await _db.JobGroups.AsNoTracking()
            .Select(j => new { j.Id, j.Code })
            .ToDictionaryAsync(j => j.Id, j => j.Code);
        var jgNames = await _db.AppTexts.AsNoTracking()
            .Where(t => t.IsActive && t.Module == "JOB_GROUP" && t.LanguageCode == "de")
            .ToDictionaryAsync(t => t.TextKey, t => t.Content);

        // Pro MA der jüngste laufende Vertrag bestimmt Modell + Filiale.
        string? FunkCode(int? jobGroupId, string? jobTitle) =>
            jobGroupId.HasValue && jobGroups.TryGetValue(jobGroupId.Value, out var jc) ? jc : jobTitle;

        var proMa = rows
            .GroupBy(x => x.EmployeeId)
            .Select(g => g.OrderByDescending(x => x.ContractStartDate).First())
            .Where(x => wanted.Count == 0 || wanted.Contains((x.EmploymentModel ?? "").ToUpperInvariant()))
            .Where(x => wantedFunk.Count == 0
                     || wantedFunk.Contains((FunkCode(x.JobGroupId, x.JobTitle) ?? "").ToUpperInvariant()))
            .ToList();

        var branches = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.City, c.BranchName, c.WorkLocation })
            .ToDictionaryAsync(c => c.Id);

        var result = proMa
            .OrderBy(x => x.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LastName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new
            {
                employeeId = x.EmployeeId,
                name = $"{x.FirstName} {x.LastName}".Trim(),
                employeeNumber = x.EmployeeNumber,
                modell = x.EmploymentModel,
                filiale = x.CompanyProfileId.HasValue && branches.TryGetValue(x.CompanyProfileId.Value, out var b)
                    ? (!string.IsNullOrWhiteSpace(b.WorkLocation) ? b.WorkLocation : (b.City ?? b.BranchName))
                    : null,
                email = string.IsNullOrWhiteSpace(x.Email) ? null : x.Email.Trim(),
                funktion = FunkCode(x.JobGroupId, x.JobTitle) is string fc && fc.Length > 0
                    ? (jgNames.TryGetValue(fc + ".NAME", out var fn) ? fn : fc)
                    : null,
            });

        return Ok(result);
    }

    public class SendDto
    {
        public string Betreff { get; set; } = "";
        public string Text { get; set; } = "";
        public List<int> EmployeeIds { get; set; } = new();
    }

    // ── POST /api/ma-email/senden ────────────────────────────────────────
    /// <summary>
    /// Versand als Einzelmails. Text 1:1 (Zeilenumbrüche → &lt;br&gt;).
    /// Liefert pro MA das Resultat — Mail-Fehler brechen den Lauf nicht ab.
    /// </summary>
    [HttpPost("senden")]
    public async Task<IActionResult> Senden([FromBody] SendDto dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Betreff))
            return BadRequest(new { error = "BETREFF_FEHLT", message = "Bitte einen Betreff eingeben." });
        if (string.IsNullOrWhiteSpace(dto.Text))
            return BadRequest(new { error = "TEXT_FEHLT", message = "Bitte einen Nachrichtentext eingeben." });
        if (dto.EmployeeIds.Count == 0)
            return BadRequest(new { error = "KEINE_EMPFAENGER", message = "Bitte mindestens einen Empfänger wählen." });

        var emps = await _db.Employees.AsNoTracking()
            .Where(e => dto.EmployeeIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.Email })
            .ToListAsync();

        var text = dto.Text.Trim();
        var html = System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br>");

        var gesendet = new List<object>();
        var fehlgeschlagen = new List<object>();
        var ohneEmail = new List<object>();

        foreach (var e in emps)
        {
            var name = $"{e.FirstName} {e.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(e.Email))
            {
                ohneEmail.Add(new { e.Id, name });
                continue;
            }
            // bypassTestRedirect (Walter 14.08.2026): die Gruppen-E-Mail geht
            // IMMER an die echten MA-Adressen — die globale Test-Umleitung
            // gilt hier nicht (der GF wählt die Empfänger ja bewusst und
            // bestätigt den Versand mit Rückfrage).
            var ok = await _email.SendAsync(e.Email.Trim(), name, dto.Betreff.Trim(), html, text,
                bypassTestRedirect: true);
            if (ok) gesendet.Add(new { e.Id, name, email = e.Email.Trim() });
            else fehlgeschlagen.Add(new { e.Id, name, email = e.Email.Trim() });
        }

        return Ok(new
        {
            gesendet = gesendet.Count,
            fehlgeschlagen,
            ohneEmail,
            details = gesendet,
        });
    }
}

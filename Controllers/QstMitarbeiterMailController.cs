using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// E-Mails an den MA zur Quellensteuer (Walter 03.09.2026, «die gleiche Mail
/// wie bei der Bewilligung»):
///   • partner-ausweis  — Befreiung über den Ehepartner (CH/C), aber dessen
///                        Ausweis liegt nicht als Dokument vor.
///   • partner-angaben  — MA QST-pflichtig und verheiratet, aber die
///                        Ehepartner-Angaben sind unvollständig; die Mail
///                        zählt auf, WAS fehlt.
/// Beide: Briefanrede, OneCrew-Rahmen, wählbare Kopie an OneCrew-Benutzer
/// (Vorschlag s.ittig + GF), Ablage als PDF in den MA-Dokumenten.
///
///   GET  /api/employees/{employeeId}/qst-mail/{art}/preview
///   POST /api/employees/{employeeId}/qst-mail/{art}/send   { kopieUserIds }
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/qst-mail")]
[Authorize(Roles = "admin,superuser,user,buchhaltung")]
public class QstMitarbeiterMailController : HrControllerBase
{
    private readonly MitarbeiterMailService _mails;
    private readonly QstPflichtCheckService _qst;

    public QstMitarbeiterMailController(AppDbContext db, MitarbeiterMailService mails, QstPflichtCheckService qst) : base(db)
    {
        _mails = mails; _qst = qst;
    }

    private sealed class Build
    {
        public IActionResult? Error { get; init; }
        public MitarbeiterMailService.Mail? Mail { get; init; }
        public string Titel { get; init; } = "";
        public string DateiPrefix { get; init; } = "";
    }

    private async Task<Build> BuildAsync(int employeeId, string art)
    {
        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return new Build { Error = NotFound(new { error = "Mitarbeiter nicht gefunden." }) };
        var to = (emp.Email ?? "").Trim();
        if (to.Length == 0)
            return new Build { Error = BadRequest(new { error = "Für diesen Mitarbeitenden ist keine E-Mail-Adresse hinterlegt." }) };

        var chk = await _qst.CheckAsync(employeeId, DateOnly.FromDateTime(DateTime.Now));
        var spouse = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(f => f.EmployeeId == employeeId && f.MemberType == "Ehepartner" && f.DateOfDeath == null)
            .OrderByDescending(f => f.Id).FirstOrDefaultAsync();

        // «deiner Ehefrau Anna Muster» / «deinem Ehemann …» / neutral
        var spouseName = spouse == null ? "" : $"{spouse.FirstName} {spouse.LastName}".Trim();
        var g = (spouse?.Gender ?? "").Trim().ToLowerInvariant();
        string dativ = g is "female" or "w" or "weiblich" ? "deiner Ehefrau"
                     : g is "male" or "m" or "männlich" ? "deinem Ehemann"
                     : "deinem Ehepartner / deiner Ehepartnerin";
        string dativName = spouseName.Length > 0 ? $"{dativ} {spouseName}" : dativ;
        string pron = g is "female" or "w" or "weiblich" ? "sie" : g is "male" or "m" or "männlich" ? "er" : "er/sie";
        string kurz = g is "female" or "w" or "weiblich" ? "deiner Ehefrau" : g is "male" or "m" or "männlich" ? "deinem Ehemann" : "deinem Ehepartner";

        string betreff, text, titel, prefix;
        switch (art)
        {
            case "partner-ausweis":
            {
                if (chk.BefreiungsGrund is not ("Ehepartner-CH" or "Ehepartner-C"))
                    return new Build { Error = Conflict(new { error = "KEINE_PARTNER_BEFREIUNG", message = "Die QST-Befreiung läuft nicht über den Ehepartner — diese E-Mail passt hier nicht." }) };
                if (!chk.SpouseDokumentFehlt)
                    return new Build { Error = Conflict(new { error = "AUSWEIS_VORHANDEN", message = "Der Ausweis des Ehepartners ist bereits hinterlegt." }) };
                var dok = chk.BefreiungsGrund == "Ehepartner-CH"
                    ? "eine Kopie der Identitätskarte oder des Passes"
                    : "eine Kopie des Ausweises C";
                betreff = $"Ausweis von {kurz} — bitte nachreichen";
                text = $"{MitarbeiterMailService.Briefanrede(emp)}\n\n"
                     + $"Für deine Befreiung von der Quellensteuer brauchen wir {dok} von {dativName}. "
                     + "Kannst du uns das Dokument bitte so rasch wie möglich mitbringen oder uns ein gutes Foto (Vorder- und Rückseite) zukommen lassen?\n\n"
                     + "Herzlichen Dank\n{SenderName}";
                titel = "E-Mail — Ausweis Ehepartner fehlt (Quellensteuer)";
                prefix = "E-Mail_Ausweis_Ehepartner";
                break;
            }
            case "partner-angaben":
            {
                if (!chk.PartnerDatenFehlen || chk.PartnerDatenMaengel == null || chk.PartnerDatenMaengel.Count == 0)
                    return new Build { Error = Conflict(new { error = "ANGABEN_VOLLSTAENDIG", message = "Die Ehepartner-Angaben sind vollständig — nichts nachzufragen." }) };
                // Mängel-Liste (HR-Sprache) → verständliche Punkte für den MA
                var punkte = new List<string>();
                foreach (var m in chk.PartnerDatenMaengel)
                {
                    if (m.Contains("Eintrag fehlt", StringComparison.OrdinalIgnoreCase))
                    {
                        punkte.Add("Name, Vorname und Geburtsdatum");
                        punkte.Add("Nationalität");
                        punkte.Add("Aufenthaltsbewilligung (Kopie des Ausweises, falls keine Schweizer Staatsangehörigkeit)");
                        punkte.Add($"ob {pron} arbeitet — und wenn ja, Name und Adresse des Arbeitgebers");
                    }
                    else if (m.Contains("Nationalität", StringComparison.OrdinalIgnoreCase)) punkte.Add("Nationalität");
                    else if (m.Contains("Bewilligung", StringComparison.OrdinalIgnoreCase)) punkte.Add("Aufenthaltsbewilligung (Kopie des Ausweises, Vorder- und Rückseite)");
                    else if (m.Contains("Erwerbstätig", StringComparison.OrdinalIgnoreCase)) punkte.Add($"ob {pron} arbeitet — und wenn ja, Name und Adresse des Arbeitgebers");
                    else if (m.Contains("Arbeitgeber", StringComparison.OrdinalIgnoreCase)) punkte.Add("Name und Adresse des Arbeitgebers");
                    else punkte.Add(m);
                }
                punkte = punkte.Distinct().ToList();
                betreff = $"Angaben zu {kurz} — bitte ergänzen";
                text = $"{MitarbeiterMailService.Briefanrede(emp)}\n\n"
                     + $"Für die Quellensteuer brauchen wir noch folgende Angaben zu {dativName}:\n\n"
                     + string.Join("\n", punkte.Select(p => "• " + p))
                     + "\n\nKannst du uns diese Angaben bitte so rasch wie möglich zukommen lassen? Eine Antwort auf diese E-Mail genügt.\n\n"
                     + "Herzlichen Dank\n{SenderName}";
                titel = "E-Mail — Ehepartner-Angaben fehlen (Quellensteuer)";
                prefix = "E-Mail_Angaben_Ehepartner";
                break;
            }
            default:
                return new Build { Error = NotFound(new { error = "Unbekannte Mail-Art." }) };
        }

        text = text.Replace("{SenderName}", await _mails.SenderNameAsync(GetCurrentUserId())).Trim();
        var (cpId, code, name) = await _mails.FilialeAsync(employeeId);
        return new Build
        {
            Mail = new MitarbeiterMailService.Mail
            {
                To = to,
                Name = $"{emp.FirstName} {emp.LastName}".Trim(),
                Betreff = betreff,
                Text = text,
                Html = MitarbeiterMailService.HtmlAusText(betreff, text),
                CompanyProfileId = cpId,
                BranchCode = code,
                BranchName = name,
            },
            Titel = titel,
            DateiPrefix = prefix,
        };
    }

    private async Task<IActionResult?> GuardAsync(int employeeId)
    {
        var (cpId, _, _) = await _mails.FilialeAsync(employeeId);
        if (cpId == null)
            return User.IsInRole("admin") || (User.IsInRole("superuser") && !User.IsInRole("buchhaltung"))
                ? null
                : StatusCode(403, new { error = "BRANCH_REQUIRED", message = "Dieser Mitarbeiter hat keine Filial-Zuordnung — Versand nur für Admin/HR." });
        return await CanAccessBranchAsync(cpId.Value) ? null
            : StatusCode(403, new { error = "BRANCH_FORBIDDEN", message = "Kein Zugriff auf die Filiale dieses Mitarbeiters." });
    }

    [HttpGet("{art}/preview")]
    public async Task<IActionResult> Preview(int employeeId, string art)
    {
        var guard = await GuardAsync(employeeId);
        if (guard != null) return guard;
        var b = await BuildAsync(employeeId, art);
        if (b.Error != null) return b.Error;
        var kat = VersandKategorien.Code(VersandKategorie.Bewilligung);
        var last = await _db.MailLogs.AsNoTracking()
            .Where(l => l.EmployeeId == employeeId && l.Kategorie == kat && l.Ok && l.Subject == b.Mail!.Betreff)
            .OrderByDescending(l => l.CreatedAt).Select(l => (DateTime?)l.CreatedAt).FirstOrDefaultAsync();
        var benutzer = await _mails.BenutzerAsync(b.Mail!.CompanyProfileId);
        return Ok(new
        {
            ok = true,
            to = b.Mail.To,
            betreff = b.Mail.Betreff,
            text = b.Mail.Text,
            lastMailSentAt = last,
            benutzer = benutzer.Select(u => new { u.Id, email = u.Email, name = u.Name, rolle = u.Rolle, vorgeschlagen = u.Vorgeschlagen }),
        });
    }

    public sealed class SendDto
    {
        public List<int>? KopieUserIds { get; set; }
    }

    [HttpPost("{art}/send")]
    public async Task<IActionResult> Send(int employeeId, string art, [FromBody] SendDto? dto = null)
    {
        var guard = await GuardAsync(employeeId);
        if (guard != null) return guard;
        var b = await BuildAsync(employeeId, art);
        if (b.Error != null) return b.Error;
        var kopien = await _mails.KopienAsync(b.Mail!.CompanyProfileId, dto?.KopieUserIds);
        var res = await _mails.SendenAsync(b.Mail, kopien, VersandKategorie.Bewilligung, employeeId);
        if (res == null)
            return StatusCode(502, new { error = "E-Mail-Versand fehlgeschlagen — siehe Versandprotokoll." });
        bool abgelegt = false;
        try
        {
            var typ = await _mails.DokumentTypEhepartnerAsync();
            abgelegt = await _mails.AblegenAsync(employeeId, b.Mail, typ, b.Titel, b.DateiPrefix, kopien, GetCurrentUserId());
        }
        catch { abgelegt = false; }
        return Ok(new { ok = true, to = b.Mail.To, betreff = b.Mail.Betreff, kopien = res.KopieOk, kopienFehler = res.KopieFehler, abgelegt });
    }
}

using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Öffentlicher Download-Link für den Jahres-Lohnausweis an Behörden
/// (Walter 30.07.2026, Lohnabtretung). Analog ContractShare:
/// Klartext-Token nur im Link, SHA-256 in der DB; Landing zuerst, PDF erst
/// per Button — kein PDF-Anhang in der Mail (Messaging-Preview).
/// </summary>
[ApiController]
[Route("api/lohnausweis-share")]
public class LohnausweisShareController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LohnausweisPdfService _pdf;

    public LohnausweisShareController(AppDbContext db, LohnausweisPdfService pdf)
    {
        _db = db;
        _pdf = pdf;
    }

    // ── Öffentlich: neutrale Landing-Page ───────────────────────────────────
    [AllowAnonymous]
    [HttpGet("/lohnausweis/{token}")]
    public async Task<IActionResult> PublicLanding(string token)
    {
        var hash = ShareTokenUtil.HashToken(token);
        var t = await _db.LohnausweisShareTokens.FirstOrDefaultAsync(x => x.TokenHash == hash);
        string html;
        if (t == null)
            html = LandingHtml("Link nicht gefunden", "Dieser Lohnausweis-Link ist ungültig.", null);
        else if (t.RevokedAt != null)
            html = LandingHtml("Link nicht mehr gültig",
                "Dieser Lohnausweis-Link wurde ersetzt oder zurückgezogen. Bitte fordern Sie einen neuen an.", null);
        else if (t.ExpiresAt < DateTime.Now)
            html = LandingHtml("Link abgelaufen",
                "Dieser Lohnausweis-Link ist abgelaufen. Bitte fordern Sie einen neuen an.", null);
        else
        {
            if (t.OpenedAt == null)
            {
                t.OpenedAt = DateTime.Now;
                try { await _db.SaveChangesAsync(); } catch { /* best-effort */ }
            }

            var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == t.EmployeeId);
            var maName = emp != null
                ? System.Net.WebUtility.HtmlEncode($"{emp.FirstName} {emp.LastName}".Trim())
                : "Mitarbeiter/in";
            var pdfHref = $"/lohnausweis/{token}/pdf";
            var body = $"Sehr geehrte Damen und Herren,<br><br>"
                     + $"hier können Sie den Jahres-Lohnausweis {t.Year} für <strong>{maName}</strong> "
                     + $"als PDF herunterladen.";
            html = LandingHtml($"Lohnausweis {t.Year}", body, pdfHref,
                               t.ExpiresAt.ToString("dd.MM.yyyy"));
        }
        return Content(html, "text/html; charset=utf-8");
    }

    private static string LandingHtml(string title, string bodyHtml, string? pdfHref,
                                      string? gueltigBis = null)
    {
        var btn = pdfHref != null
            ? $"<a class='btn' href='{pdfHref}'>📄 Lohnausweis öffnen</a>"
            : "";
        var validNote = (pdfHref != null && !string.IsNullOrWhiteSpace(gueltigBis))
            ? $"<div class='valid'>Link gültig bis {System.Net.WebUtility.HtmlEncode(gueltigBis)}</div>"
            : "";
        return $@"<!DOCTYPE html>
<html lang='de'><head><meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<meta name='description' content='Sicherer Link zum Lohnausweis.'>
<meta property='og:title' content='Lohnausweis'>
<meta property='og:description' content='Sicherer Link zum Lohnausweis.'>
<title>Lohnausweis — OneCrew</title>
<link rel='icon' href='/favicon.svg' type='image/svg+xml'>
<style>
  body{{margin:0;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f6f3ee;color:#3f3f3f;display:flex;min-height:100vh;align-items:flex-start;justify-content:center}}
  .card{{background:#faf8f5;border:1px solid rgba(255,255,255,.62);box-shadow:0 8px 30px rgba(60,55,48,.16);border-radius:18px;padding:34px 28px;max-width:440px;width:90%;box-sizing:border-box;text-align:center;margin-top:clamp(20px,7vh,90px);margin-bottom:40px}}
  h1{{font-size:19px;margin:0 0 12px}}
  .msg{{font-size:14px;color:#3f3f3f;margin:0 0 22px;line-height:1.6;text-align:left}}
  a.btn{{display:inline-block;background:#3f3f3f;color:#fff;text-decoration:none;padding:13px 24px;border-radius:12px;font-size:15px;font-weight:600}}
  .valid{{font-size:12px;color:#8b8b8b;margin-top:12px}}
</style></head>
<body><div class='card'><h1>{title}</h1><div class='msg'>{bodyHtml}</div>{btn}{validNote}</div></body></html>";
    }

    // ── Öffentlich: PDF erst per Button-Klick ───────────────────────────────
    [AllowAnonymous]
    [HttpGet("/lohnausweis/{token}/pdf")]
    public async Task<IActionResult> PublicPdf(string token)
    {
        var hash = ShareTokenUtil.HashToken(token);
        var t = await _db.LohnausweisShareTokens.FirstOrDefaultAsync(x => x.TokenHash == hash);
        if (t == null)
            return NotFound("Dieser Lohnausweis-Link wurde nicht gefunden.");
        if (t.RevokedAt != null)
            return StatusCode(410, "Dieser Lohnausweis-Link wurde ersetzt oder zurückgezogen. Bitte einen neuen Link anfordern.");
        if (t.ExpiresAt < DateTime.Now)
            return StatusCode(410, "Dieser Lohnausweis-Link ist abgelaufen. Bitte einen neuen Link anfordern.");

        var (pdf, filename, error) = await LohnausweisBuildService.GeneratePdfAsync(
            _db, _pdf, t.EmployeeId, t.Year);
        if (pdf == null)
            return NotFound(error ?? "Der Lohnausweis konnte nicht erzeugt werden.");

        if (t.UsedAt == null)
        {
            t.UsedAt = DateTime.Now;
            try { await _db.SaveChangesAsync(); } catch { /* best-effort */ }
        }
        return File(pdf, "application/pdf", filename ?? $"Lohnausweis_{t.Year}.pdf");
    }
}

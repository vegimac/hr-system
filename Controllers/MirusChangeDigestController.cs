using System.Text;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrSystem.Controllers;

/// <summary>
/// Manueller Trigger + HTML-Vorschau für den Mirus-Änderungsdigest (Walter 23.07.2026).
/// Nur admin — zum Testen / Nachholen ohne auf 06:00 zu warten.
/// </summary>
[ApiController]
[Route("api/mirus-change-digest")]
[Authorize(Roles = "admin")]
public class MirusChangeDigestController : ControllerBase
{
    private readonly MirusChangeDigestService _svc;

    public MirusChangeDigestController(MirusChangeDigestService svc) => _svc = svc;

    /// <summary>Sofort-Lauf: letzte 24 h → Mails an alle Empfänger mit Flag.</summary>
    [HttpPost("run-now")]
    public async Task<IActionResult> RunNow(CancellationToken ct)
    {
        var result = await _svc.RunAsync(ct);
        return Ok(new
        {
            recipientCount = result.RecipientCount,
            mailsSent = result.MailsSent,
            changeCount = result.ChangeCount,
            message = result.Message
        });
    }

    /// <summary>
    /// 1:1 HTML-Vorschau der Digest-Mail (keine Zustellung).
    /// Optional: ?restaurantCode=129 (Reinach) oder ?companyProfileId=…
    /// </summary>
    [HttpGet("preview")]
    public async Task<IActionResult> Preview(
        [FromQuery] int? companyProfileId,
        [FromQuery] string? restaurantCode,
        CancellationToken ct)
    {
        try
        {
            var name = User.Identity?.Name ?? "Vorschau";
            var result = await _svc.PreviewAsync(ct, companyProfileId, restaurantCode, name);
            return Content(WrapPreviewHtml(result.Subject, result.Message, result.Html), "text/html", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            var msg = System.Net.WebUtility.HtmlEncode(ex.GetBaseException().Message);
            var html = "<!DOCTYPE html><html lang=\"de\"><head><meta charset=\"utf-8\"><title>Vorschau-Fehler</title></head>"
                     + "<body style=\"font-family:-apple-system,Segoe UI,Roboto,sans-serif;padding:28px;color:#991b1b\">"
                     + "<h2>Vorschau fehlgeschlagen</h2>"
                     + $"<p>{msg}</p></body></html>";
            return Content(html, "text/html", Encoding.UTF8);
        }
    }

    private static string WrapPreviewHtml(string subject, string message, string bodyHtml)
    {
        var doc = new StringBuilder();
        doc.Append("<!DOCTYPE html><html lang=\"de\"><head><meta charset=\"utf-8\">");
        doc.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        doc.Append($"<title>{System.Net.WebUtility.HtmlEncode(subject)}</title>");
        doc.Append("<style>body{margin:0;background:#f1f5f9;padding:24px}");
        doc.Append(".wrap{max-width:720px;margin:0 auto;background:#fff;padding:8px 28px 28px;");
        doc.Append("border:1px solid #e2e8f0;border-radius:8px}");
        doc.Append(".bar{font:13px/1.4 -apple-system,Segoe UI,Roboto,sans-serif;color:#64748b;");
        doc.Append("margin-bottom:12px;padding-bottom:10px;border-bottom:1px solid #e2e8f0}");
        doc.Append(".bar b{color:#0f172a}</style></head><body><div class=\"wrap\">");
        doc.Append("<div class=\"bar\">");
        doc.Append($"<div><b>Betreff:</b> {System.Net.WebUtility.HtmlEncode(subject)}</div>");
        doc.Append($"<div>{System.Net.WebUtility.HtmlEncode(message)} — nicht gesendet</div>");
        doc.Append("</div>");
        doc.Append(bodyHtml);
        doc.Append("</div></body></html>");
        return doc.ToString();
    }
}

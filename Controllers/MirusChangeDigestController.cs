using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrSystem.Controllers;

/// <summary>
/// Manueller Trigger für den Mirus-Änderungsdigest (Walter 23.07.2026).
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
}

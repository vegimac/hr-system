using System.Security.Claims;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrSystem.Controllers;

// ============================================================================
// Mindestlohn-Vertragsanpassung (Walter-Vorgabe 23.05.2026).
// GET  /api/wage-adjustment/pending  → betroffene Verträge zum nächsten
//      Mindestlohn-Stichtag (Filiale). admin/superuser/user (GF sieht Warnung).
// POST /api/wage-adjustment/apply    → erzeugt pro MA einen neuen Vertrag
//      (identisch ausser Lohn, ab Stichtag) + optional Postfach-Mitteilung.
//      admin/superuser.
//
// Der Apply-Pfad prüft via LohnEditLockService, dass der Stichtag nicht in einer
// bereits in Verarbeitung befindlichen Periode liegt (der neue Vertrag würde
// sonst rückwirkend greifen). Damit ist der Controller auch im EditLock-Audit
// abgedeckt (kein Whitelist-Eintrag nötig).
// ============================================================================
[ApiController]
[Route("api/wage-adjustment")]
[Authorize(Roles = "admin,superuser,user")]
public class WageAdjustmentController : ControllerBase
{
    private readonly WageAdjustmentService _svc;
    private readonly LohnEditLockService   _editLock;

    public WageAdjustmentController(WageAdjustmentService svc, LohnEditLockService editLock)
    {
        _svc      = svc;
        _editLock = editLock;
    }

    // GET /api/wage-adjustment/pending?companyProfileId=123
    [HttpGet("pending")]
    public async Task<IActionResult> Pending([FromQuery] int companyProfileId)
    {
        if (companyProfileId <= 0)
            return BadRequest(new { error = "companyProfileId fehlt." });
        var result = await _svc.GetPendingAsync(companyProfileId);
        return Ok(result);
    }

    // POST /api/wage-adjustment/apply
    [HttpPost("apply")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Apply([FromBody] WageAdjustmentApplyDto dto)
    {
        if (dto.CompanyProfileId <= 0)
            return BadRequest(new { error = "companyProfileId fehlt." });
        if (dto.Items == null || dto.Items.Count == 0)
            return BadRequest(new { error = "Keine Verträge zum Anpassen übergeben." });

        // Stichtag darf nicht in einer bereits verarbeiteten Periode liegen.
        var firstAllowed = await _editLock.GetFirstAllowedDateAsync(User, dto.CompanyProfileId);
        if (firstAllowed.HasValue && dto.EffectiveDate < firstAllowed.Value)
        {
            return Conflict(new
            {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Das Mindestlohn-Datum {dto.EffectiveDate:dd.MM.yyyy} liegt in einer bereits in Verarbeitung befindlichen Lohnperiode. Frühestes erlaubtes Datum: {firstAllowed.Value:dd.MM.yyyy}.",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });
        }

        var actor  = GetUserId();
        var result = await _svc.ApplyAsync(dto.CompanyProfileId, dto.EffectiveDate, dto.Items, dto.SendMessage, actor);
        return Ok(result);
    }

    private int? GetUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }
}

public record WageAdjustmentApplyDto(
    int CompanyProfileId,
    DateOnly EffectiveDate,
    bool SendMessage,
    List<WageAdjustmentApplyItem> Items);

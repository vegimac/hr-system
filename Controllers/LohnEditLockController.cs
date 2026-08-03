using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrSystem.Controllers;

/// <summary>
/// Liefert dem Frontend die Information „ab welchem Datum dürfen Absenzen /
/// Stempelzeiten / Vorschuss / Lohnzulagen für diese Filiale eingegeben werden?"
///
/// Frontend setzt das min-Date auf Datum-Pickern und blendet ein Banner ein,
/// wenn nicht alle Tage frei sind.
/// </summary>
[ApiController]
[Authorize]
[Route("api/lohn-edit-lock")]
public class LohnEditLockController : ControllerBase
{
    private readonly LohnEditLockService _lockSvc;

    public LohnEditLockController(LohnEditLockService lockSvc)
    {
        _lockSvc = lockSvc;
    }

    /// <summary>
    /// GET /api/lohn-edit-lock/first-allowed-date?branchId=58
    /// Optional: <c>mode=contracts</c> (= weiche Sperre: nur Definitiv
    /// «abgeschlossen», wie QST/Verträge/Familienzulagen — Walter 01.08.2026).
    /// Antwort:
    ///   { firstAllowedDate: "2026-02-01", reason: "…" }
    /// oder:
    ///   { firstAllowedDate: null, reason: null }
    /// </summary>
    [HttpGet("first-allowed-date")]
    public async Task<IActionResult> GetFirstAllowedDate(
        [FromQuery] int branchId,
        [FromQuery] string? mode = null)
    {
        if (branchId <= 0)
            return BadRequest(new { error = "branchId fehlt" });

        var soft = string.Equals(mode, "contracts", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "soft", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "definitiv", StringComparison.OrdinalIgnoreCase);

        var first = soft
            ? await _lockSvc.GetFirstAllowedDateForContractsAsync(branchId)
            : await _lockSvc.GetFirstAllowedDateAsync(User, branchId);

        string? reason = null;
        if (first.HasValue)
        {
            var prevMonth = first.Value.AddMonths(-1);
            reason = soft
                ? $"Definitiv-Lohnperioden bis und mit {prevMonth:MM/yyyy} sind abgeschlossen — Edits nur ab {first.Value:dd.MM.yyyy}."
                : $"Lohnperioden bis und mit {prevMonth:MM/yyyy} sind in Verarbeitung " +
                  $"oder abgeschlossen — Edits nur ab {first.Value:dd.MM.yyyy}.";
        }

        return Ok(new
        {
            firstAllowedDate = first.HasValue ? first.Value.ToString("yyyy-MM-dd") : null,
            reason,
            mode = soft ? "contracts" : "default"
        });
    }
}

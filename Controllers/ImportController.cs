using Microsoft.AspNetCore.Mvc;

namespace HrSystem.Controllers;

/// <summary>
/// PDF-/ZIP-Stempelzeiten-Import ENTFERNT (Walter-Vorgabe 19.06.2026).
/// Stempelzeiten werden ab sofort AUSSCHLIESSLICH über die easy@work-API
/// synchronisiert (manueller Sync + täglicher Auto-Sync, siehe
/// <c>EasyAtWorkController</c> / <c>EasyAtWorkTimepunchSyncService</c>).
///
/// Die alten Routen bleiben nur noch als Stubs erhalten und liefern
/// <c>410 Gone</c> mit klarer Meldung, falls irgendwo noch ein alter Link/Client
/// darauf zeigt. Es gibt KEINEN Schreibpfad mehr in <c>employee_time_entry</c>
/// über diesen Controller — bestehende importierte Stempelzeiten bleiben
/// unverändert in der DB.
/// </summary>
[ApiController]
[Route("api/import")]
public class ImportController : ControllerBase
{
    private const string GoneError   = "STEMPELZEITEN_PDF_IMPORT_REMOVED";
    private const string GoneMessage = "Stempelzeiten werden nur noch über easy@work API synchronisiert.";

    private IActionResult Gone() => StatusCode(410, new { error = GoneError, message = GoneMessage });

    // Frühere PDF-Import-Endpunkte → jetzt 410 Gone.
    [HttpGet("stempelzeiten/count")]              public IActionResult Count()             => Gone();
    [HttpPost("stempelzeiten/dedupe")]            public IActionResult Dedupe()            => Gone();
    [HttpPost("stempelzeiten/preview")]           public IActionResult PreviewStempel()    => Gone();
    [HttpPost("stempelzeiten")]                   public IActionResult ImportStempel()     => Gone();
    [HttpPost("stempelzeiten-monatlich/preview")] public IActionResult PreviewMonatlich()  => Gone();
    [HttpPost("stempelzeiten-monatlich")]         public IActionResult ImportMonatlich()   => Gone();
}

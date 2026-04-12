using HrSystem.Services;
using Microsoft.AspNetCore.Mvc;

namespace HrSystem.Controllers;

/// <summary>
/// Admin-Endpoints für den Import und Status der Quellensteuer-Tarifdateien.
/// </summary>
[ApiController]
[Route("api/admin/quellensteuer")]
public class QuellensteuerAdminController : ControllerBase
{
    private readonly QuellensteuerTarifService _tarifService;

    public QuellensteuerAdminController(QuellensteuerTarifService tarifService)
        => _tarifService = tarifService;

    // ── GET /api/admin/quellensteuer/status ──────────────────────────────
    /// <summary>
    /// Gibt den Status aller geladenen Quellensteuer-Tarifdateien zurück.
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var dateien = _tarifService.GetDateienStatus();
        return Ok(new
        {
            AnzahlDateien  = dateien.Count,
            Dateien        = dateien.Select(d => new
            {
                d.Jahr,
                d.Kanton,
                d.Dateiname,
                d.AnzahlKombinationen,
                d.AnzahlEintraege,
                MaxEinkommen   = d.MaxEinkommen,
                GeladenAm      = d.GeladenAm.ToString("dd.MM.yyyy HH:mm")
            })
        });
    }

    // ── POST /api/admin/quellensteuer/import ─────────────────────────────
    /// <summary>
    /// Importiert eine oder mehrere Tarifdateien (.txt oder .zip).
    /// Der Kanton und das Jahr werden automatisch aus dem Dateiinhalt erkannt.
    /// Nach dem Import wird der Cache automatisch neu geladen.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(50 * 1024 * 1024)] // max 50 MB
    public async Task<IActionResult> Import([FromForm] IFormFileCollection files)
    {
        if (files == null || files.Count == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });

        var alleErgebnisse = new List<object>();
        var fehler = new List<string>();

        foreach (var file in files)
        {
            string ext = Path.GetExtension(file.FileName).ToLower();
            if (ext != ".txt" && ext != ".zip")
            {
                fehler.Add($"{file.FileName}: Nur .txt und .zip Dateien erlaubt.");
                continue;
            }

            try
            {
                using var stream = file.OpenReadStream();
                var ergebnis = await _tarifService.ImportiereAsync(stream, file.FileName);

                if (ergebnis.Erfolg)
                {
                    foreach (var info in ergebnis.ImportierteDateien)
                    {
                        alleErgebnisse.Add(new
                        {
                            info.Kanton,
                            info.Jahr,
                            info.Dateiname,
                            Meldung = $"Kanton {info.Kanton} {info.Jahr} erfolgreich importiert."
                        });
                    }
                }
                else
                {
                    fehler.Add($"{file.FileName}: Kanton konnte nicht erkannt werden.");
                }
            }
            catch (Exception ex)
            {
                fehler.Add($"{file.FileName}: {ex.Message}");
            }
        }

        if (alleErgebnisse.Count == 0 && fehler.Count > 0)
            return BadRequest(new { error = "Import fehlgeschlagen.", fehler });

        return Ok(new
        {
            Erfolg         = alleErgebnisse.Count,
            Fehler         = fehler.Count,
            Importiert     = alleErgebnisse,
            Fehlermeldungen = fehler,
            Status         = _tarifService.GetDateienStatus().Select(d => new
            {
                d.Jahr, d.Kanton, d.AnzahlKombinationen, d.MaxEinkommen
            })
        });
    }

    // ── POST /api/admin/quellensteuer/reload ─────────────────────────────
    /// <summary>
    /// Lädt alle Tarifdateien aus dem Dateisystem neu (ohne Upload).
    /// </summary>
    [HttpPost("reload")]
    public IActionResult Reload()
    {
        _tarifService.Reload();
        var status = _tarifService.GetDateienStatus();
        return Ok(new
        {
            Meldung       = $"Cache neu geladen: {status.Count} Dateien.",
            AnzahlDateien = status.Count
        });
    }
}

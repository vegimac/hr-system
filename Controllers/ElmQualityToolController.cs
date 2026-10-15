using HrSystem.Services.Elm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrSystem.Controllers;

/// <summary>
/// Lohntest im Swissdec Quality Tool (Walter 15.10.2026): Bericht mit allen Abweichungen der gesendeten
/// Muster-AG-Meldungen, Sendungen zurücksetzen, neuer Lauf. Nur Testinstanz, nur Admin.
/// Der API-Schlüssel bleibt auf dem Server (<see cref="QualityToolClient"/>).
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/elm/qualitytool")]
public class ElmQualityToolController : ControllerBase
{
    private readonly QualityToolClient _qt;

    public ElmQualityToolController(QualityToolClient qt) => _qt = qt;

    [HttpGet("status")]
    public IActionResult Status()
        => Ok(new { verfuegbar = ElmEndpunkte.IstTestinstanz(), nichtBereit = _qt.NichtBereit(), monitoringId = _qt.MonitoringId });

    [HttpGet("bericht")]
    public Task<IActionResult> Bericht(CancellationToken ct) => Ausfuehren(() => _qt.BerichtAsync(ct));

    [HttpPost("sendungen-zuruecksetzen")]
    public Task<IActionResult> SendungenZuruecksetzen(CancellationToken ct) => Ausfuehren(() => _qt.SendungenZuruecksetzenAsync(ct));

    [HttpPost("neuer-lauf")]
    public Task<IActionResult> NeuerLauf(CancellationToken ct) => Ausfuehren(() => _qt.NeuerLaufAsync(ct));

    private async Task<IActionResult> Ausfuehren(Func<Task<QualityToolBericht.Bericht>> aufruf)
    {
        try
        {
            var b = await aufruf();
            return Ok(new
            {
                b.MonitoringId, punkte = b.Punkte, b.Meldung, anzahl = b.Anzahl,
                monate = b.Monate.Select(m => new
                {
                    m.Sequenz, m.Teilprozess, anzahl = m.Anzahl,
                    faelle = m.Faelle.Select(f => new
                    {
                        f.Id, f.Name,
                        abweichungen = f.Abweichungen.Select(a => new { a.Feld, a.Soll, a.Ist, a.Art, a.Pfad, a.Gesendet }),
                    }),
                }),
            });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = "QUALITYTOOL", message = ex.Message }); }
    }
}

using HrSystem.Services.Elm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrSystem.Controllers;

/// <summary>
/// Swissdec ELM 6.0 — Etappe E1 (Walter 27.08.2026): Verbindungstests
/// Ping + CheckInteroperability. NUR Admin (eigener Sidebar-Bereich
/// «Swissdec»). Rein manuell ausgelöste Calls (Richtlinien Kap. 4: Ping
/// nie automatisieren). Kein Lohn-Edit → EditLock-Audit unkritisch
/// (GET-frei, POSTs rufen nur externe Test-Endpunkte).
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/elm")]
public class ElmController : ControllerBase
{
    private readonly ElmTransmitterClient _client;
    private readonly ElmAnnualDeclarationBuilder _builder;
    public ElmController(ElmTransmitterClient client, ElmAnnualDeclarationBuilder builder)
    {
        _client = client;
        _builder = builder;
    }

    public record ElmUrlDto(string Url);

    private static bool UrlOk(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

    [HttpPost("ping")]
    public async Task<IActionResult> Ping([FromBody] ElmUrlDto dto, CancellationToken ct)
    {
        if (!UrlOk(dto.Url))
            return BadRequest(new { error = "URL_INVALID", message = "Bitte eine gültige Endpoint-URL angeben." });
        var r = await _client.PingAsync(dto.Url.Trim(), ct);
        return Ok(r);
    }

    [HttpPost("check-interoperability")]
    public async Task<IActionResult> CheckInteroperability([FromBody] ElmUrlDto dto, CancellationToken ct)
    {
        if (!UrlOk(dto.Url))
            return BadRequest(new { error = "URL_INVALID", message = "Bitte eine gültige Endpoint-URL angeben." });
        var r = await _client.CheckInteroperabilityAsync(dto.Url.Trim(), ct);
        return Ok(r);
    }

    /// <summary>
    /// E2: Jahresmeldung AHV als DeclareAnnualSalary-XML erzeugen + gegen
    /// die ELM-6.0-Schemas validieren. Nur mit KUNSTDATEN (test.onecrew.ch)
    /// im Refapps-Transmitter hochladen — nie Echtdaten.
    /// </summary>
    [HttpGet("annual-ahv/{year:int}")]
    public async Task<IActionResult> AnnualAhv(int year, CancellationToken ct)
    {
        if (year < 2020 || year > 2100)
            return BadRequest(new { error = "YEAR_INVALID", message = "Bitte ein gültiges Jahr angeben." });
        var r = await _builder.BuildAhvAsync(year, ct);
        return Ok(new
        {
            xml = r.Xml,
            personen = r.Personen,
            uebersprungen = r.Uebersprungen,
            totalAhv = r.TotalAhv,
            totalAlv = r.TotalAlv,
            warnungen = r.Warnungen,
            xsdFehler = r.XsdFehler,
            valid = r.XsdFehler.Count == 0 && r.Xml.Length > 0
        });
    }
}

using HrSystem.Services.Elm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrSystem.Controllers;

/// <summary>
/// Lohnausweis und Listen aus Swissdec Viewgen. Nur auf der Testinstanz: Produktiv darf Viewgen erst
/// nach der Zertifizierung laufen (Lizenzbestimmungen Ziffer 3).
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superuser")]
[Route("api/elm/viewgen")]
public class ElmViewgenController : ControllerBase
{
    private readonly ViewgenDienst _viewgen;
    private readonly ElmAnnualDeclarationBuilder _jahr;

    public ElmViewgenController(ViewgenDienst viewgen, ElmAnnualDeclarationBuilder jahr)
    {
        _viewgen = viewgen;
        _jahr = jahr;
    }

    [HttpGet("status")]
    public IActionResult Status() => Ok(new
    {
        freigegeben = ElmEndpunkte.IstTestinstanz(),
        installiert = _viewgen.Installiert,
        berichte = ViewgenDienst.Berichte.Select(b => new { plugin = b.Key, name = b.Value }),
    });

    public record BerichtBody(string Plugin, int Jahr, int? CompanyProfileId);

    [HttpPost("bericht")]
    public async Task<IActionResult> Bericht([FromBody] BerichtBody dto, CancellationToken ct)
    {
        if (!ElmEndpunkte.IstTestinstanz())
            return StatusCode(403, new { error = "VIEWGEN_GESPERRT", message = "Viewgen ist erst nach der Zertifizierung freigegeben." });
        if (!ViewgenDienst.Berichte.TryGetValue(dto.Plugin ?? "", out var name))
            return BadRequest(new { error = "VIEWGEN_BERICHT", message = "Diesen Bericht gibt es nicht." });

        var meldung = await _jahr.BuildAhvAsync(dto.Jahr, dto.CompanyProfileId, ct);
        if (string.IsNullOrEmpty(meldung.Xml) || meldung.Personen == 0)
            return BadRequest(new { error = "VIEWGEN_DATEN", message = $"Für {dto.Jahr} gibt es keine Jahresmeldung.", warnungen = meldung.Warnungen });
        if (meldung.XsdFehler.Count > 0)
            return BadRequest(new { error = "VIEWGEN_SCHEMA", message = "Die Jahresmeldung ist nicht gültig.", technisch = meldung.XsdFehler });

        var r = await _viewgen.ErzeugePdfAsync(meldung.Xml, dto.Plugin!, ct);
        if (r.Pdf == null) return StatusCode(502, new { error = "VIEWGEN_FEHLER", message = r.Fehler });
        return File(r.Pdf, "application/pdf", $"{name}-{dto.Jahr}.pdf");
    }
}

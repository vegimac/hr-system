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
    private readonly ElmMonthlyDeclarationBuilder _monat;

    public ElmViewgenController(ViewgenDienst viewgen, ElmAnnualDeclarationBuilder jahr, ElmMonthlyDeclarationBuilder monat)
    {
        _viewgen = viewgen;
        _jahr = jahr;
        _monat = monat;
    }

    [HttpGet("status")]
    public IActionResult Status() => Ok(new
    {
        freigegeben = ElmEndpunkte.IstTestinstanz(),
        installiert = _viewgen.Installiert,
        berichte = ViewgenDienst.Berichte.Select(b => new
        {
            plugin = b.Key, name = b.Value,
            ausJahr = ViewgenDienst.AusJahr(b.Key), ausMonat = ViewgenDienst.AusMonat.Contains(b.Key),
        }),
    });

    /// <param name="Monat">Gesetzt = aus der Monatsmeldung, leer = aus der Jahresmeldung.</param>
    public record BerichtBody(string Plugin, int Jahr, int? CompanyProfileId, int? Monat = null);

    [HttpPost("bericht")]
    public async Task<IActionResult> Bericht([FromBody] BerichtBody dto, CancellationToken ct)
    {
        if (!ElmEndpunkte.IstTestinstanz())
            return StatusCode(403, new { error = "VIEWGEN_GESPERRT", message = "Viewgen ist erst nach der Zertifizierung freigegeben." });
        var plugin = dto.Plugin ?? "";
        if (!ViewgenDienst.Berichte.TryGetValue(plugin, out var name))
            return BadRequest(new { error = "VIEWGEN_BERICHT", message = "Diesen Bericht gibt es nicht." });

        if (dto.Monat is int m0 ? m0 is < 1 or > 12 || !ViewgenDienst.AusMonat.Contains(plugin) : !ViewgenDienst.AusJahr(plugin))
            return BadRequest(new { error = "VIEWGEN_BERICHT", message = "Diesen Bericht gibt es für diese Meldung nicht." });
        var (xml, fehler) = await MeldungAsync(dto.Jahr, dto.Monat, dto.CompanyProfileId, ct);
        if (fehler != null) return fehler;
        var datei = dto.Monat is int m ? $"{name}-{m:00}-{dto.Jahr}.pdf" : $"{name}-{dto.Jahr}.pdf";

        var r = await _viewgen.ErzeugePdfAsync(xml, plugin, ct);
        if (r.Pdf == null) return StatusCode(502, new { error = "VIEWGEN_FEHLER", message = r.Fehler });
        return File(r.Pdf, "application/pdf", datei);
    }

    public record XmlBody(int Jahr, int? CompanyProfileId, int? Monat = null);

    /// <summary>Genau die XML, die ViewGen bekommt: frisch aus OneCrew, nicht aus einer abgelegten Datei.</summary>
    [HttpPost("xml")]
    public async Task<IActionResult> Xml([FromBody] XmlBody dto, CancellationToken ct)
    {
        if (!ElmEndpunkte.IstTestinstanz())
            return StatusCode(403, new { error = "VIEWGEN_GESPERRT", message = "Viewgen ist erst nach der Zertifizierung freigegeben." });
        if (dto.Monat is < 1 or > 12)
            return BadRequest(new { error = "VIEWGEN_DATEN", message = "Bitte einen gültigen Monat wählen." });
        var (xml, fehler) = await MeldungAsync(dto.Jahr, dto.Monat, dto.CompanyProfileId, ct);
        if (fehler != null) return fehler;
        var name = dto.Monat is int m ? $"Monatsmeldung-{m:00}-{dto.Jahr}.xml" : $"Jahresmeldung-{dto.Jahr}.xml";
        return File(System.Text.Encoding.UTF8.GetBytes(xml), "application/xml", name);
    }

    private async Task<(string Xml, IActionResult? Fehler)> MeldungAsync(int jahr, int? monat, int? filiale, CancellationToken ct)
    {
        string xml;
        List<string> xsd, warnungen;
        if (monat is int mo)
        {
            var m = await _monat.BuildAsync(jahr, mo, filiale, ct);
            (xml, xsd, warnungen) = (m.Xml, m.XsdFehler, m.Warnungen);
            if (string.IsNullOrEmpty(xml))
                return ("", BadRequest(new { error = "VIEWGEN_DATEN", message = warnungen.FirstOrDefault() ?? $"Für {mo:00}.{jahr} gibt es keine Monatsmeldung.", warnungen }));
        }
        else
        {
            var j = await _jahr.BuildAhvAsync(jahr, filiale, ct);
            (xml, xsd, warnungen) = (j.Xml, j.XsdFehler, j.Warnungen);
            if (string.IsNullOrEmpty(xml) || j.Personen == 0)
                return ("", BadRequest(new { error = "VIEWGEN_DATEN", message = warnungen.FirstOrDefault() ?? $"Für {jahr} gibt es keine Jahresmeldung.", warnungen }));
        }
        if (xsd.Count > 0)
            return ("", BadRequest(new { error = "VIEWGEN_SCHEMA", message = "Die Meldung ist nicht gültig.", technisch = xsd }));
        return (xml, null);
    }
}

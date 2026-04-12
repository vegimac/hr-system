using HrSystem.Services;
using Microsoft.AspNetCore.Mvc;

namespace HrSystem.Controllers;

/// <summary>
/// Liefert Quellensteuer-Tarife basierend auf den offiziellen ESTV-Tarifdateien.
/// </summary>
[ApiController]
[Route("api/quellensteuer/tarif")]
public class QuellensteuerTarifController : ControllerBase
{
    private readonly QuellensteuerTarifService _tarifService;

    public QuellensteuerTarifController(QuellensteuerTarifService tarifService)
        => _tarifService = tarifService;

    // ── GET /api/quellensteuer/tarif/satz ────────────────────────────────
    /// <summary>
    /// Gibt den Steuersatz (%) und den Steuerbetrag (CHF) für einen Mitarbeiter zurück.
    /// </summary>
    [HttpGet("satz")]
    public IActionResult GetSatz(
        [FromQuery] string kanton,
        [FromQuery] string tarif,
        [FromQuery] int    kinder,
        [FromQuery] bool   kirchensteuer,
        [FromQuery] decimal bruttolohn)
    {
        if (string.IsNullOrWhiteSpace(kanton) || string.IsNullOrWhiteSpace(tarif))
            return BadRequest("kanton und tarif sind Pflichtfelder.");

        if (bruttolohn < 0)
            return BadRequest("bruttolohn muss >= 0 sein.");

        decimal? satz   = _tarifService.GetSteuersatzProzent(kanton, tarif, kinder, kirchensteuer, bruttolohn);
        decimal? betrag = _tarifService.GetSteuerBetrag(kanton, tarif, kinder, kirchensteuer, bruttolohn);

        if (satz is null)
            return NotFound(new { error = $"Tarif '{kanton}|{tarif}|{kinder}|{(kirchensteuer ? 'Y' : 'N')}' nicht gefunden." });

        return Ok(new QstSatzDto(
            Kanton:        kanton.ToUpper(),
            TarifCode:     tarif.ToUpper(),
            QstCode:       $"{tarif.ToUpper()}{kinder}{(kirchensteuer ? 'Y' : 'N')}",
            Kinder:        kinder,
            Kirchensteuer: kirchensteuer,
            Bruttolohn:    bruttolohn,
            SteuersatzPct: satz.Value,
            SteuerbetragCHF: betrag!.Value
        ));
    }

    // ── GET /api/quellensteuer/tarif/kantone ─────────────────────────────
    /// <summary>Gibt alle geladenen Kantone zurück.</summary>
    [HttpGet("kantone")]
    public IActionResult GetKantone()
        => Ok(_tarifService.GetVerfuegbareKantone());

    // ── GET /api/quellensteuer/tarif/kombinationen ───────────────────────
    /// <summary>Gibt alle Tarifkombinationen eines Kantons zurück.</summary>
    [HttpGet("kombinationen")]
    public IActionResult GetKombinationen([FromQuery] string kanton)
    {
        if (string.IsNullOrWhiteSpace(kanton))
            return BadRequest("kanton ist ein Pflichtfeld.");

        var result = _tarifService.GetTarifKombinationen(kanton.ToUpper());
        if (result.Count == 0)
            return NotFound(new { error = $"Kanton '{kanton}' nicht gefunden." });

        return Ok(result.Select(t => new
        {
            t.Kanton,
            t.Tarif,
            t.Kinder,
            t.Kirchensteuer,
            t.QstCode
        }));
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record QstSatzDto(
    string  Kanton,
    string  TarifCode,
    string  QstCode,
    int     Kinder,
    bool    Kirchensteuer,
    decimal Bruttolohn,
    decimal SteuersatzPct,
    decimal SteuerbetragCHF
);

using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// HR-Bereich → Lohnlauf. Operiert auf einer PayrollPeriode im Status
/// "provisorisch_abgeschlossen" und stellt die HR-Aktionen bereit:
///   • Vorab-Kontroll-PDF aller MA-Lohnbelege als gemergedes PDF
///   • Vorbedingungen-Check (Validate)
///   • (Phase 4) DTA-Generierung + finalisieren
/// </summary>
[Authorize]
[ApiController]
[Route("api/lohnlauf")]
public class LohnlaufController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LohnlaufService _svc;

    public LohnlaufController(AppDbContext db, LohnlaufService svc)
    {
        _db = db;
        _svc = svc;
    }

    /// <summary>
    /// Vorbedingungen-Check für eine Periode. Liefert eine Liste von Issues
    /// (leer = OK, kann provisorisch abgeschlossen werden).
    /// </summary>
    [HttpGet("{periodeId:int}/validate")]
    public async Task<IActionResult> Validate(int periodeId)
    {
        var r = await _svc.ValidateAsync(periodeId);
        return Ok(new { ok = r.Ok, issues = r.Issues });
    }

    /// <summary>
    /// Vorab-Kontroll-PDF: alle MA-Lohnbelege einer Periode in einem PDF.
    /// Wird inline als application/pdf zurückgegeben — kann im Browser-Iframe
    /// angezeigt oder als Datei heruntergeladen werden.
    /// </summary>
    [HttpGet("{periodeId:int}/vorab-pdf")]
    public async Task<IActionResult> VorabPdf(int periodeId)
    {
        var periode = await _db.PayrollPerioden.FindAsync(periodeId);
        if (periode is null) return NotFound("Periode nicht gefunden.");

        try
        {
            var bytes = await _svc.GenerateVorabPdfAsync(periodeId);
            if (bytes.Length == 0)
                return BadRequest(new { message = "Vorab-PDF leer — keine Snapshots in dieser Periode." });

            // Inline-Anzeige im Browser, Filename für „Speichern unter"
            Response.Headers.Append("Content-Disposition",
                $"inline; filename=\"Lohnlauf_Vorab_{periode.CompanyProfileId}_{periode.Year}-{periode.Month:D2}.pdf\"");
            return File(bytes, "application/pdf");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// DTA für die MA-Lohn-Auszahlungen (pain.001 XML). Generierung on-demand
    /// aus den eingefrorenen Snapshots — bei jedem Download neue MsgId.
    /// </summary>
    [HttpGet("{periodeId:int}/dta-ma")]
    public async Task<IActionResult> DtaMa(int periodeId)
    {
        var periode = await _db.PayrollPerioden.FindAsync(periodeId);
        if (periode is null) return NotFound(new { message = "Periode nicht gefunden." });
        try
        {
            var bytes = await _svc.GenerateDtaMaAsync(periodeId);
            Response.Headers.Append("Content-Disposition",
                $"attachment; filename=\"DTA_MA_{periode.CompanyProfileId}_{periode.Year}-{periode.Month:D2}.xml\"");
            // octet-stream statt application/xml: der Reverse-Proxy/WAF auf dem
            // Server blockt XML-Antworten mit 403 (Walter-Bug 20.05.2026). Die
            // Datei wird via Content-Disposition trotzdem als .xml gespeichert.
            return File(bytes, "application/octet-stream");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// DTA für Lohnabtretungs-Empfänger (Behörden) — separates pain.001-XML.
    /// Pro Behörden-IBAN die aggregierte Summe aller MA-Beträge.
    /// </summary>
    [HttpGet("{periodeId:int}/dta-behoerden")]
    public async Task<IActionResult> DtaBehoerden(int periodeId)
    {
        var periode = await _db.PayrollPerioden.FindAsync(periodeId);
        if (periode is null) return NotFound(new { message = "Periode nicht gefunden." });
        try
        {
            var bytes = await _svc.GenerateDtaBehoerdenAsync(periodeId);
            Response.Headers.Append("Content-Disposition",
                $"attachment; filename=\"DTA_Behoerden_{periode.CompanyProfileId}_{periode.Year}-{periode.Month:D2}.xml\"");
            // octet-stream statt application/xml: der Reverse-Proxy/WAF auf dem
            // Server blockt XML-Antworten mit 403 (Walter-Bug 20.05.2026). Die
            // Datei wird via Content-Disposition trotzdem als .xml gespeichert.
            return File(bytes, "application/octet-stream");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

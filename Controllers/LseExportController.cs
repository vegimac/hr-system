using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrSystem.Controllers;

/// <summary>
/// BFS-LSE-Export (Lohnstrukturerhebung).
///
/// Erster Entwurf:
///   GET /api/lse-export/preview?year=2026&month=10[&companyProfileId=X]
///       → JSON-Liste der LSE-Records (für Vorschau-Tabelle im Frontend)
///   GET /api/lse-export/csv?year=2026&month=10[&companyProfileId=X]
///       → CSV-Download mit allen Feldern
///
/// Beide Endpoints lassen companyProfileId weg → über alle Filialen.
/// Nur admin/superuser zugelassen — sensible Lohndaten.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/lse-export")]
public class LseExportController : ControllerBase
{
    private readonly LseExportService _svc;

    public LseExportController(LseExportService svc) => _svc = svc;

    [HttpGet("preview")]
    public async Task<IActionResult> Preview([FromQuery] int year, [FromQuery] int month, [FromQuery] int? companyProfileId)
    {
        if (year < 2020 || year > 2100) return BadRequest(new { error = "Jahr ungültig." });
        if (month < 1 || month > 12)    return BadRequest(new { error = "Monat ungültig." });
        var records = await _svc.BuildAsync(year, month, companyProfileId);
        return Ok(new {
            count = records.Count,
            year, month, companyProfileId,
            records
        });
    }

    [HttpGet("csv")]
    public async Task<IActionResult> Csv([FromQuery] int year, [FromQuery] int month, [FromQuery] int? companyProfileId)
    {
        if (year < 2020 || year > 2100) return BadRequest(new { error = "Jahr ungültig." });
        if (month < 1 || month > 12)    return BadRequest(new { error = "Monat ungültig." });
        var records = await _svc.BuildAsync(year, month, companyProfileId);
        var csv = _svc.ToCsv(records);
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        var fileName = companyProfileId.HasValue
            ? $"LSE_{year}-{month:D2}_Filiale-{companyProfileId.Value}.csv"
            : $"LSE_{year}-{month:D2}_alle-Filialen.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }
}

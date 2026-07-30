using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HrSystem.Controllers;

/// <summary>
/// Jahres-Lohnausweis (ESTV Form 11 dfe).
///
/// Phase 1: aggregiert die PayrollSnapshots eines Mitarbeiters über ein
/// Kalenderjahr und füllt das amtliche AcroForm-Template.
///
/// Build-Logik in <see cref="LohnausweisBuildService"/> (auch für Behörden-Link).
/// </summary>
[ApiController]
[Route("api/lohnausweis")]
[Authorize]
public class LohnausweisController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LohnausweisPdfService _pdf;

    public LohnausweisController(AppDbContext db, LohnausweisPdfService pdf)
    {
        _db = db;
        _pdf = pdf;
    }

    // ════════════════════════════════════════════════════════════════════════
    // GET /api/lohnausweis/{empId}/{year}/preview
    // ════════════════════════════════════════════════════════════════════════
    [HttpGet("{employeeId}/{year}/preview")]
    public async Task<IActionResult> Preview(int employeeId, int year)
    {
        var emp = await _db.Employees
            .Include(e => e.Employments)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound(new { error = "Mitarbeiter nicht gefunden." });

        var (data, anzahlMonate, _, _) = await LohnausweisBuildService.BuildDataAsync(_db, emp, year);
        if (data == null)
            return BadRequest(new {
                error = $"Keine Lohnabrechnungen für {emp.FirstName} {emp.LastName} im Jahr {year} gefunden."
            });

        return Ok(new { anzahlMonate, data });
    }

    // ════════════════════════════════════════════════════════════════════════
    // POST /api/lohnausweis/{empId}/{year}/pdf
    // ════════════════════════════════════════════════════════════════════════
    [HttpPost("{employeeId}/{year}/pdf")]
    public async Task<IActionResult> PdfFromPreview(int employeeId, int year,
        [FromBody] LohnausweisData payload)
    {
        var emp = await _db.Employees
            .Include(e => e.Employments)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound(new { error = "Mitarbeiter nicht gefunden." });

        var (fresh, _, freshUid, _) = await LohnausweisBuildService.BuildDataAsync(_db, emp, year);
        if (fresh != null)
            MergeStammdaten(payload, fresh);

        var (signaturePng, signerName) = await GetSignerAsync();

        byte[] bytes;
        try { bytes = _pdf.Generate(payload, signaturePng, signerName, freshUid); }
        catch (Exception ex) { return Problem("PDF konnte nicht erstellt werden: " + ex.Message); }

        var filename = $"Lohnausweis_{year}_{emp.LastName}_{emp.FirstName}.pdf";
        return File(bytes, "application/pdf", filename);
    }

    private static void MergeStammdaten(LohnausweisData payload, LohnausweisData fresh)
    {
        static string? Fill(string? frontend, string? db)
            => string.IsNullOrWhiteSpace(frontend) ? db : frontend;

        payload.CompanyUidFormatted    = Fill(payload.CompanyUidFormatted,    fresh.CompanyUidFormatted);
        payload.CompanyName            = Fill(payload.CompanyName,            fresh.CompanyName);
        payload.BranchName             = Fill(payload.BranchName,             fresh.BranchName);
        payload.CompanyStreet          = Fill(payload.CompanyStreet,          fresh.CompanyStreet);
        payload.CompanyZip             = Fill(payload.CompanyZip,             fresh.CompanyZip);
        payload.CompanyCity            = Fill(payload.CompanyCity,            fresh.CompanyCity);
        payload.CompanyCountry         = Fill(payload.CompanyCountry,         fresh.CompanyCountry);
        payload.CompanyPhone           = Fill(payload.CompanyPhone,           fresh.CompanyPhone);
        payload.HrVerantwortlicherName = Fill(payload.HrVerantwortlicherName, fresh.HrVerantwortlicherName);
        payload.MaLastname             = Fill(payload.MaLastname,             fresh.MaLastname);
        payload.MaFirstname            = Fill(payload.MaFirstname,            fresh.MaFirstname);
        payload.MaStreet               = Fill(payload.MaStreet,               fresh.MaStreet);
        payload.MaZip                  = Fill(payload.MaZip,                  fresh.MaZip);
        payload.MaCity                 = Fill(payload.MaCity,                 fresh.MaCity);
        payload.MaCountry              = Fill(payload.MaCountry,              fresh.MaCountry);
    }

    // ════════════════════════════════════════════════════════════════════════
    // GET /api/lohnausweis/{empId}/{year}/pdf
    // ════════════════════════════════════════════════════════════════════════
    [HttpGet("{employeeId}/{year}/pdf")]
    public async Task<IActionResult> PdfDirect(int employeeId, int year)
    {
        var emp = await _db.Employees
            .Include(e => e.Employments)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound(new { error = "Mitarbeiter nicht gefunden." });

        var (signaturePng, signerName) = await GetSignerAsync();
        var (bytes, filename, error) = await LohnausweisBuildService.GeneratePdfAsync(
            _db, _pdf, employeeId, year, signaturePng, signerName);
        if (bytes == null)
            return BadRequest(new { error = error ?? "PDF konnte nicht erstellt werden." });

        return File(bytes, "application/pdf", filename);
    }

    private async Task<(byte[]? signaturePng, string? signerName)> GetSignerAsync()
    {
        byte[]? png = null;
        string? name = null;
        var loggedInIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(loggedInIdStr, out var loggedInId))
        {
            var u = await _db.AppUsers
                .Where(x => x.Id == loggedInId)
                .Select(x => new { x.SignaturePng, x.FirstName, x.LastName, x.Username })
                .FirstOrDefaultAsync();
            if (u != null)
            {
                png = u.SignaturePng;
                var fullName = $"{u.FirstName} {u.LastName}".Trim();
                name = string.IsNullOrWhiteSpace(fullName) ? u.Username : fullName;
            }
        }
        return (png, name);
    }
}

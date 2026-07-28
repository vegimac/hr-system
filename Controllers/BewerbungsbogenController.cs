using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Blanko-Bewerbungsbogen-PDF für Restaurant Admin (Walter 27.07.2026).
/// Filiale aus Query oder globalem Selektor — kein MA nötig.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superuser,user")]
[Route("api/bewerbungsbogen")]
public class BewerbungsbogenController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly BewerbungsbogenPdfService _pdf;

    public BewerbungsbogenController(AppDbContext db, BewerbungsbogenPdfService pdf)
    {
        _db = db;
        _pdf = pdf;
    }

    /// <summary>GET /api/bewerbungsbogen/pdf?companyProfileId=…</summary>
    [HttpGet("pdf")]
    public async Task<IActionResult> GetPdf([FromQuery] int companyProfileId)
    {
        if (companyProfileId <= 0)
            return BadRequest(new { error = "FILIALE_FEHLT", message = "Bitte eine Filiale wählen." });

        var cp = await _db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == companyProfileId);
        if (cp is null)
            return NotFound(new { error = "FILIALE_NICHT_GEFUNDEN", message = "Filiale nicht gefunden." });

        var street = string.Join(" ", new[] { cp.Street, cp.HouseNumber }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        var plzOrt = string.Join(" ", new[] { cp.ZipCode, cp.City }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

        List<string> permitCodes;
        try
        {
            permitCodes = await _db.PermitTypes.AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.Code)
                .Select(p => p.Code)
                .ToListAsync();
        }
        catch
        {
            // Katalog-Fehler darf den Bogen nicht blockieren — Fallback in der PDF-Engine.
            permitCodes = new List<string>();
        }

        byte[] bytes;
        try
        {
            bytes = _pdf.Generate(new BewerbungsbogenInput(
                CompanyName: string.IsNullOrWhiteSpace(cp.CompanyName) ? "Schaub Restaurants GmbH" : cp.CompanyName,
                RestaurantName: cp.BranchName,
                Strasse: string.IsNullOrWhiteSpace(street) ? null : street,
                PlzOrt: string.IsNullOrWhiteSpace(plzOrt) ? null : plzOrt,
                Telefon: string.IsNullOrWhiteSpace(cp.Phone) ? null : cp.Phone.Trim(),
                PermitCodes: permitCodes));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "BEWERBUNGSBOGEN_PDF_FEHLER",
                message = "Bewerbungsbogen konnte nicht erzeugt werden.",
                detail = ex.GetType().Name + ": " + ex.Message
            });
        }

        var safeCity = (cp.City ?? cp.BranchName ?? "Filiale")
            .Replace(" ", "_", StringComparison.Ordinal);
        return File(bytes, "application/pdf", $"Bewerbungsbogen_{safeCity}.pdf");
    }
}

using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Blanko-Formular «Quellensteuer-Informationen» (Walter-Vorgabe 23.08.2026).
/// GET-only, Blanko pro Filiale — kein Lohn-Edit, im EditLock-Audit als
/// reiner Formular-Generator unkritisch (analog BewerbungsbogenController).
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superuser,user")]
[Route("api/qst-info-formular")]
public class QstInfoFormularController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly QstInfoFormularPdfService _pdf;

    public QstInfoFormularController(AppDbContext db, QstInfoFormularPdfService pdf)
    {
        _db = db;
        _pdf = pdf;
    }

    /// <summary>GET /api/qst-info-formular/pdf?companyProfileId=…</summary>
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

        byte[] bytes;
        try
        {
            bytes = _pdf.Generate(new QstInfoFormularInput(
                CompanyName: string.IsNullOrWhiteSpace(cp.CompanyName) ? "Schaub Restaurants GmbH" : cp.CompanyName,
                RestaurantName: cp.BranchName,
                Strasse: string.IsNullOrWhiteSpace(street) ? null : street,
                PlzOrt: string.IsNullOrWhiteSpace(plzOrt) ? null : plzOrt,
                Telefon: string.IsNullOrWhiteSpace(cp.Phone) ? null : cp.Phone.Trim(),
                Email: string.IsNullOrWhiteSpace(cp.Email) ? null : cp.Email.Trim()));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "QST_INFO_PDF_FEHLER",
                message = "Formular konnte nicht erzeugt werden.",
                detail = ex.GetBaseException().Message
            });
        }

        return File(bytes, "application/pdf", "Quellensteuer-Informationen.pdf");
    }
}

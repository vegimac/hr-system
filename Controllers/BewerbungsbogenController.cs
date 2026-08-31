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

    /// <summary>
    /// GET /api/bewerbungsbogen/pdf?companyProfileId=…&amp;teil=bewerbung|gespraech
    /// Walter 31.08.2026: Der Bogen ist in zwei Formulare geteilt —
    /// «bewerbung» (kurz, gibt der Bewerber ab) und «gespraech» (wird im
    /// Bewerbungsgespraech ausgefuellt). Ohne teil-Parameter kommt das
    /// Bewerbungsformular, damit alte Links weiter funktionieren.
    /// </summary>
    [HttpGet("pdf")]
    public async Task<IActionResult> GetPdf([FromQuery] int companyProfileId,
        [FromQuery] string? teil = null)
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

        var istGespraech = string.Equals(teil, "gespraech", StringComparison.OrdinalIgnoreCase);
        // «alt» = der frühere, ungeteilte Bogen — nur zur Kontrolle (Walter 31.08.2026).
        var istAlt = string.Equals(teil, "alt", StringComparison.OrdinalIgnoreCase);

        byte[] bytes;
        try
        {
            var input = new BewerbungsbogenInput(
                CompanyName: string.IsNullOrWhiteSpace(cp.CompanyName) ? "Schaub Restaurants GmbH" : cp.CompanyName,
                RestaurantName: cp.BranchName,
                Strasse: string.IsNullOrWhiteSpace(street) ? null : street,
                PlzOrt: string.IsNullOrWhiteSpace(plzOrt) ? null : plzOrt,
                Telefon: string.IsNullOrWhiteSpace(cp.Phone) ? null : cp.Phone.Trim(),
                Email: string.IsNullOrWhiteSpace(cp.Email) ? null : cp.Email.Trim(),
                Oeffnungszeiten: OeffnungszeitenText(cp));
            bytes = istAlt       ? _pdf.GenerateAlt(input)
                  : istGespraech ? _pdf.GenerateGespraech(input)
                                 : _pdf.GenerateBewerbung(input);
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
        var name = istAlt ? "Bewerbungsbogen_alt"
                 : istGespraech ? "Bewerbungsgespraech"
                                : "Bewerbung";
        return File(bytes, "application/pdf", $"{name}_{safeCity}.pdf");
    }

    /// <summary>
    /// Öffnungszeiten der Filiale als eine Zeile, z.B.
    /// «Mo–Do 08:00–01:00  ·  Fr/Sa 08:00–03:00  ·  So 08:00–24:00».
    /// Reine Ortszeit-Texte aus dem Filialprofil; aufeinanderfolgende Tage
    /// mit gleichen Zeiten werden zusammengefasst. Nichts erfasst → null,
    /// dann lässt das PDF den Kasten weg (lieber keine Angabe als eine
    /// falsche). Walter 31.08.2026.
    /// </summary>
    private static string? OeffnungszeitenText(HrSystem.Models.CompanyProfile cp)
    {
        var tage = new (string Kurz, string? Von, string? Bis)[]
        {
            ("Mo", cp.OpeningMonFrom, cp.OpeningMonTo),
            ("Di", cp.OpeningTueFrom, cp.OpeningTueTo),
            ("Mi", cp.OpeningWedFrom, cp.OpeningWedTo),
            ("Do", cp.OpeningThuFrom, cp.OpeningThuTo),
            ("Fr", cp.OpeningFriFrom, cp.OpeningFriTo),
            ("Sa", cp.OpeningSatFrom, cp.OpeningSatTo),
            ("So", cp.OpeningSunFrom, cp.OpeningSunTo),
        };
        if (tage.All(t => string.IsNullOrWhiteSpace(t.Von) && string.IsNullOrWhiteSpace(t.Bis)))
            return null;

        var teile = new List<string>();
        var i = 0;
        while (i < tage.Length)
        {
            var t = tage[i];
            if (string.IsNullOrWhiteSpace(t.Von) && string.IsNullOrWhiteSpace(t.Bis)) { i++; continue; }

            var j = i;
            while (j + 1 < tage.Length
                   && tage[j + 1].Von == t.Von && tage[j + 1].Bis == t.Bis) j++;

            var label = i == j ? tage[i].Kurz
                      : j == i + 1 ? $"{tage[i].Kurz}/{tage[j].Kurz}"
                      : $"{tage[i].Kurz}–{tage[j].Kurz}";
            teile.Add($"{label} {t.Von ?? "?"}–{t.Bis ?? "?"}");
            i = j + 1;
        }
        return teile.Count == 0 ? null : string.Join("  ·  ", teile);
    }
}

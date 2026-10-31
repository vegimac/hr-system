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
        [FromBody] LohnausweisData payload, [FromQuery] bool final = false)
    {
        var emp = await _db.Employees
            .Include(e => e.Employments)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound(new { error = "Mitarbeiter nicht gefunden." });

        // Die Vorschau zeigt den Ausweis nur. Was gedruckt wird, kommt aus dem
        // Lohn und den Lohnausweis-Angaben, nicht aus getippten Feldern.
        _ = payload;
        var (fresh, _, freshUid, _) = await LohnausweisBuildService.BuildDataAsync(_db, emp, year);
        if (fresh == null)
            return BadRequest(new {
                error = $"Keine Lohnabrechnungen für {emp.FirstName} {emp.LastName} im Jahr {year} gefunden."
            });
        payload = fresh;

        if (final)
        {
            // ── FINALISIERUNG (Walter-Vorgabe 16.08.2026) ────────────────
            // 1) Pflichtdaten — fehlende Felder blockieren den finalen
            //    Lohnausweis mit einer klaren Aufzaehlung.
            var fehlt = LohnausweisBarcodeService.PflichtdatenFehlen(payload);
            if (fehlt.Count > 0)
                return BadRequest(new
                {
                    error = "LOHNAUSWEIS_UNVOLLSTAENDIG",
                    message = "Finaler Lohnausweis blockiert — es fehlen: " + string.Join(", ", fehlt),
                    felder = fehlt,
                });

            // 2) DocID + CreationDate: beim ERSTEN Finalisieren einfrieren,
            //    Wiederdruck traegt exakt dieselbe Identifikation.
            var fin = await _db.LohnausweisFinals
                .FirstOrDefaultAsync(f => f.EmployeeId == employeeId && f.Year == year);
            if (fin == null)
            {
                fin = new LohnausweisFinal
                {
                    EmployeeId   = employeeId,
                    Year         = year,
                    DocId        = Guid.NewGuid(),
                    CreationDate = DateTime.Now,
                    CreatedBy    = User.Identity?.Name,
                };
                _db.LohnausweisFinals.Add(fin);
                await _db.SaveChangesAsync();
            }
            payload.IstFinal          = true;
            payload.DocId             = fin.DocId.ToString().ToUpperInvariant();
            payload.CreationDateFinal = fin.CreationDate.ToString("yyyy-MM-ddTHH:mm:ss");

            // 3) Qualitaets-Check: das v6-TxAB-XML muss gegen das offizielle
            //    Swissdec-Schema validieren (der gedruckte Barcode bleibt v5).
            var v6xml = LohnausweisBarcodeService.BuildXml(payload,
                LohnausweisBarcodeService.TxAbSchemaVersion.V6_20260306);
            var xsdFehler = LohnausweisBarcodeService.ValidateElm6(v6xml);
            if (xsdFehler.Count > 0)
                return BadRequest(new
                {
                    error = "TXAB_SCHEMA_INVALID",
                    message = "TxAB-XML entspricht nicht dem Swissdec-Schema: "
                              + string.Join(" | ", xsdFehler),
                });
        }

        var (signaturePng, signerName) = await GetSignerAsync();

        byte[] bytes;
        try { bytes = _pdf.Generate(payload, signaturePng, signerName, freshUid); }
        catch (InvalidOperationException vex)
        {
            // Pflichtfeld-Validierung des Barcodes (Walter 16.08.2026)
            return BadRequest(new { error = "BARCODE_VALIDIERUNG", message = vex.Message });
        }
        catch (Exception ex) { return Problem("PDF konnte nicht erstellt werden: " + ex.Message); }

        var filename = $"Lohnausweis_{year}_{emp.LastName}_{emp.FirstName}.pdf";
        return File(bytes, "application/pdf", filename);
    }

    /// <summary>
    /// Einzelner Lohnausweis: neue DocID, Ziffer 15 verweist auf die bisherige.
    /// </summary>
    [HttpPost("{employeeId:int}/{year:int}/rektifikat")]
    public async Task<IActionResult> Rektifikat(int employeeId, int year)
    {
        var fin = await _db.LohnausweisFinals
            .FirstOrDefaultAsync(f => f.EmployeeId == employeeId && f.Year == year);
        if (fin == null)
            return BadRequest(new { message = "Zuerst den Lohnausweis finalisieren. Ein Entwurf kann nicht rektifiziert werden." });
        LohnausweisErsetzen(fin);
        await _db.SaveChangesAsync();
        return Ok(new
        {
            docId = fin.DocId.ToString().ToUpperInvariant(),
            ersetzt = fin.ErsetztDocId?.ToString().ToUpperInvariant(),
        });
    }

    /// <summary>
    /// Ersatzmeldung: jeder bereits finale Lohnausweis der Filiale im Jahr
    /// wird ein Rektifikat mit neuer DocID.
    /// </summary>
    [HttpPost("ersatz")]
    public async Task<IActionResult> Ersatz(int year, int companyProfileId)
    {
        if (companyProfileId <= 0 || year < 2000)
            return BadRequest(new { message = "Jahr und Filiale fehlen." });
        if (!await DarfFilialeAsync(companyProfileId)) return Forbid();

        var von = new DateOnly(year, 1, 1);
        var bis = new DateOnly(year, 12, 31);
        var personen = await _db.PayrollSnapshots.AsNoTracking()
            .Where(s => s.CompanyProfileId == companyProfileId
                     && s.Periode.PeriodFrom >= von
                     && s.Periode.PeriodFrom <= bis)
            .Select(s => s.EmployeeId)
            .Distinct()
            .ToListAsync();
        var finals = await _db.LohnausweisFinals
            .Where(f => f.Year == year && personen.Contains(f.EmployeeId))
            .ToListAsync();
        if (finals.Count == 0)
            return BadRequest(new { message = "Für dieses Jahr gibt es noch keinen finalen Lohnausweis." });
        foreach (var fin in finals)
            LohnausweisErsetzen(fin);
        await _db.SaveChangesAsync();
        return Ok(new { anzahl = finals.Count });
    }

    private static void LohnausweisErsetzen(LohnausweisFinal fin)
    {
        fin.ErsetztDocId = fin.DocId;
        fin.ErsetztAm = fin.CreationDate;
        fin.DocId = Guid.NewGuid();
        fin.CreationDate = DateTime.Now;
    }

    private async Task<bool> DarfFilialeAsync(int companyProfileId)
    {
        if (User.IsInRole("admin") || (User.IsInRole("superuser") && !User.IsInRole("buchhaltung")))
            return true;
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return false;
        return await _db.UserBranchAccesses.AnyAsync(a =>
            a.UserId == userId && a.CompanyProfileId == companyProfileId);
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

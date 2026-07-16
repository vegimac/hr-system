using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Mutterschafts-Gespräch (Walter-Vorgabe 16.07.2026): Checkliste fürs
/// Gespräch mit der Mitarbeiterin + daraus die Mutterschaftsvereinbarung
/// (nach Word-Vorlage, Du-Form, Varianten Verlängerung/Rückkehr).
/// Beides read-only PDF — schreibt nichts. Rollen wie Mutterschafts-Modul.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/mutterschaft-vereinbarung")]
public class MutterschaftVereinbarungController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly MutterschaftPdfService _pdf;

    public MutterschaftVereinbarungController(AppDbContext db, MutterschaftPdfService pdf)
    {
        _db = db; _pdf = pdf;
    }

    public class VereinbarungDto
    {
        public DateOnly? GespraechsDatum { get; set; }     // Default: heute
        public int VerlBezahlt { get; set; }               // 0 = keine
        public int VerlUnbezahlt { get; set; }             // 0 = keine
        /// <summary>GLEICH | ANDERS | KEINE</summary>
        public string Rueckkehr { get; set; } = "GLEICH";
        public decimal? PensumProzent { get; set; }        // nur bei ANDERS
        public string? RueckkehrRestaurant { get; set; }   // nur bei ANDERS
        public bool Eingeschrieben { get; set; }           // sonst persönliche Aushändigung
    }

    [HttpGet("{pregnancyId:int}/checkliste-pdf")]
    public async Task<IActionResult> ChecklistePdf(int pregnancyId)
    {
        var common = await LoadCommonAsync(pregnancyId);
        if (common == null) return NotFound(new { error = "PREGNANCY_NOT_FOUND" });
        try
        {
            var bytes = _pdf.GenerateCheckliste(common);
            return File(bytes, "application/pdf",
                $"Mutterschafts-Checkliste_{common.MaName}_{common.MaVorname}.pdf".Replace(" ", "_"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "PDF_FEHLER", message = ex.GetBaseException().Message });
        }
    }

    [HttpPost("{pregnancyId:int}/vereinbarung-pdf")]
    public async Task<IActionResult> VereinbarungPdf(int pregnancyId, [FromBody] VereinbarungDto dto)
    {
        var common = await LoadCommonAsync(pregnancyId);
        if (common == null) return NotFound(new { error = "PREGNANCY_NOT_FOUND" });

        var rueckkehr = (dto.Rueckkehr ?? "GLEICH").Trim().ToUpperInvariant();
        if (rueckkehr is not ("GLEICH" or "ANDERS" or "KEINE"))
            return BadRequest(new { error = "RUECKKEHR_UNGUELTIG", message = "Rückkehr muss GLEICH, ANDERS oder KEINE sein." });

        var opt = new MutterschaftPdfService.MvOptionen(
            GespraechsDatum:     dto.GespraechsDatum ?? DateOnly.FromDateTime(DateTime.Today),
            VerlBezahlt:         Math.Max(0, dto.VerlBezahlt),
            VerlUnbezahlt:       Math.Max(0, dto.VerlUnbezahlt),
            Rueckkehr:           rueckkehr,
            PensumProzent:       dto.PensumProzent,
            RueckkehrRestaurant: dto.RueckkehrRestaurant,
            Eingeschrieben:      dto.Eingeschrieben);

        try
        {
            var bytes = _pdf.GenerateVereinbarung(common, opt);
            return File(bytes, "application/pdf",
                $"Mutterschaftsvereinbarung_{common.MaName}_{common.MaVorname}.pdf".Replace(" ", "_"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "PDF_FEHLER", message = ex.GetBaseException().Message });
        }
    }

    // ── Helfer ──────────────────────────────────────────────────────────────

    private async Task<MutterschaftPdfService.MvCommon?> LoadCommonAsync(int pregnancyId)
    {
        var p = await _db.EmployeePregnancies
            .Include(x => x.Employee)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == pregnancyId);
        var e = p?.Employee;
        if (p == null || e == null) return null;

        // Filiale: jüngster/aktiver Vertrag (gleiche Regel wie Kündigung/Zeugnis).
        var emp = await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == e.Id)
            .OrderByDescending(em => em.IsActive)
            .ThenByDescending(em => em.ContractStartDate)
            .FirstOrDefaultAsync();
        CompanyProfile? cp = null;
        if (emp?.CompanyProfileId != null)
            cp = await _db.CompanyProfiles.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == emp.CompanyProfileId.Value);

        // Unterzeichnerin = UNTERSCHRIFTSBERECHTIGTE der Filiale (Walter-Vorgabe
        // 16.07.2026): der Standard-Unterzeichner aus user_branch_access
        // (IsDefault=true) — gleiche Quelle wie beim Arbeitsvertrag. Das
        // Unterschrifts-BILD wird nur eingebettet, wenn die eingeloggte Person
        // selbst diese Unterzeichnerin ist (NIE die Unterschrift einer anderen
        // Person automatisch einsetzen) — sonst bleibt Platz zum Unterschreiben.
        byte[]? sigPng = null; string? signerName = null; string? signerTitle = null;
        if (cp != null)
        {
            var signatory = await _db.UserBranchAccesses.AsNoTracking()
                .Include(a => a.User)
                .Where(a => a.CompanyProfileId == cp.Id && a.IsDefault)
                .FirstOrDefaultAsync();
            if (signatory?.User != null)
            {
                var full = $"{signatory.User.FirstName} {signatory.User.LastName}".Trim();
                signerName  = string.IsNullOrWhiteSpace(full) ? signatory.User.Username : full;
                signerTitle = signatory.FunctionTitle;

                var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(idStr, out var uid) && uid == signatory.UserId)
                    sigPng = await _db.AppUsers.AsNoTracking()
                        .Where(x => x.Id == uid)
                        .Select(x => x.SignaturePng)
                        .FirstOrDefaultAsync();
            }
        }

        string? Join(string? a, string? b)
        {
            var s = string.Join(" ", new[] { a, b }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        return new MutterschaftPdfService.MvCommon(
            FirmaName:         cp?.CompanyName,
            RestaurantName:    cp?.BranchName ?? cp?.FullDisplayName,
            FirmaStrasse:      Join(cp?.Street, cp?.HouseNumber),
            FirmaPlzOrt:       Join(cp?.ZipCode, cp?.City),
            MaVorname:         e.FirstName ?? "",
            MaName:            e.LastName ?? "",
            MaStrasse:         e.Street,
            MaPlzOrt:          Join(e.ZipCode, e.City),
            EmployeeNumber:    e.EmployeeNumber,
            MaGeburtsdatum:    e.DateOfBirth,
            Ort:               cp?.City ?? "",
            Datum:             DateOnly.FromDateTime(DateTime.Today),
            ErrechneterTermin: p.ErrechneterTermin,
            UnterzeichnerName: signerName,
            UnterzeichnerTitel: signerTitle,
            SignaturePng:      sigPng);
    }
}

using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Walter-Vorgabe 20.06.2026 (ArG): vorausgefülltes SECO-Formular „Ärztliches
/// Zeugnis für die Eignung für Schicht- und Nachtarbeit" zum Abgeben an einen
/// MA. Wir füllen Betrieb (Filiale des MA) + die Angaben der untersuchten
/// Person vor; den Rest füllt die Ärztin/der Arzt.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/nacht-eignung")]
public class NachtEignungController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly NachtEignungPdfService _pdf;
    private readonly NachtVerzichtPdfService _verzicht;
    public NachtEignungController(AppDbContext db, NachtEignungPdfService pdf, NachtVerzichtPdfService verzicht)
    {
        _db = db; _pdf = pdf; _verzicht = verzicht;
    }

    [HttpGet("{empId:int}/pdf")]
    public async Task<IActionResult> GetPdf(int empId, [FromQuery] string? debug = null)
    {
        // Diagnose: Feldnamen des Templates ausgeben (für ggf. präzises Mapping).
        if (string.Equals(debug, "fields", StringComparison.OrdinalIgnoreCase))
            return Ok(new { fields = _pdf.ListTemplateFields() });

        var e = await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == empId);
        if (e == null) return NotFound(new { error = "EMP_NOT_FOUND" });

        // Filiale des MA: jüngste aktive Anstellung, sonst jüngste überhaupt.
        var emp = await _db.Employments
            .AsNoTracking()
            .Where(em => em.EmployeeId == empId && em.CompanyProfileId != null)
            .OrderByDescending(em => em.IsActive)
            .ThenByDescending(em => em.ContractStartDate)
            .FirstOrDefaultAsync();

        CompanyProfile? cp = null;
        if (emp?.CompanyProfileId != null)
            cp = await _db.CompanyProfiles.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == emp.CompanyProfileId.Value);

        string? Join(string? a, string? b)
        {
            var s = string.Join(" ", new[] { a, b }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        var data = new NachtEignungPdfService.NachtEignungData(
            BetriebName:    cp?.CompanyName,
            BetriebStrasse: Join(cp?.Street, cp?.HouseNumber),
            BetriebPlzOrt:  Join(cp?.ZipCode, cp?.City),
            BetriebTelefon: cp?.Phone,
            Nachname:       e.LastName ?? "",
            Vorname:        e.FirstName ?? "",
            Geburtsdatum:   e.DateOfBirth?.ToString("dd.MM.yyyy"),
            PersonStrasse:  Join(e.Street, e.HouseNumber),
            PersonPlzOrt:   Join(e.ZipCode, e.City)
        );

        byte[] bytes;
        try { bytes = _pdf.Generate(data); }
        catch (FileNotFoundException ex)
        { return StatusCode(500, new { error = "TEMPLATE_MISSING", message = ex.Message }); }

        return File(bytes, "application/pdf", $"{e.EmployeeNumber}-Unters-Nacht.pdf");
    }

    /// <summary>
    /// „Verzicht auf medizinische Untersuchung und Beratung bei regelmässiger
    /// Nachtarbeit" — Beilage-Layout (gelber Briefkopf), Arbeitgeber + MA + Funktion
    /// vorausgefüllt. Walter-Vorgabe 20.06.2026.
    /// </summary>
    [HttpGet("{empId:int}/verzicht-pdf")]
    public async Task<IActionResult> GetVerzichtPdf(int empId)
    {
        var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == empId);
        if (e == null) return NotFound(new { error = "EMP_NOT_FOUND" });

        var emp = await _db.Employments
            .AsNoTracking()
            .Where(em => em.EmployeeId == empId)
            .OrderByDescending(em => em.IsActive)
            .ThenByDescending(em => em.ContractStartDate)
            .FirstOrDefaultAsync();

        CompanyProfile? cp = null;
        if (emp?.CompanyProfileId != null)
            cp = await _db.CompanyProfiles.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == emp.CompanyProfileId.Value);

        // Unterzeichner aus der Filialverwaltung (Default-UserBranchAccess) —
        // exakt wie beim Arbeitsvertrag: Name aus dem User, Funktion = FunctionTitle.
        string? signerName = null, signerFunktion = null;
        if (emp?.CompanyProfileId != null)
        {
            var signatory = await _db.UserBranchAccesses
                .Include(s => s.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.CompanyProfileId == emp.CompanyProfileId.Value && s.IsDefault == true);
            if (signatory?.User != null)
            {
                signerName = ($"{signatory.User.FirstName} {signatory.User.LastName}").Trim();
                signerFunktion = signatory.FunctionTitle;
            }
        }

        string? Join(string? a, string? b)
        {
            var s = string.Join(" ", new[] { a, b }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        var data = new NachtVerzichtPdfService.NachtVerzichtData(
            ArbeitgeberName:    cp?.CompanyName,
            ArbeitgeberStrasse: Join(cp?.Street, cp?.HouseNumber),
            ArbeitgeberPlzOrt:  Join(cp?.ZipCode, cp?.City),
            ArbeitgeberOrt:     cp?.City,
            MaName:             ($"{e.FirstName} {e.LastName}").Trim(),
            MaStrasse:          Join(e.Street, e.HouseNumber),
            MaPlzOrt:           Join(e.ZipCode, e.City),
            MaGeburtsdatum:     e.DateOfBirth?.ToString("dd.MM.yyyy"),
            UnterzeichnerName:     signerName,
            UnterzeichnerFunktion: signerFunktion
        );

        var bytes = _verzicht.Generate(data);
        return File(bytes, "application/pdf", $"{e.EmployeeNumber}-Verzicht-Untersuch.pdf");
    }
}

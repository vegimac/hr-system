using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// AHV-Formular 318.260 «Anmeldung für einen Versicherungsausweis»
/// (Walter-Vorgabe 06.08.2026) — HR-Hub → Behörden-Korrespondenz.
/// Für MA ohne AHV-Nummer (z.B. Zuzug aus dem Ausland): GET liefert die
/// Vorbefüllung aus MA- + Filial-Stammdaten, das Frontend zeigt alle Felder
/// editierbar (Eltern-Namen kennt das System nicht), POST erzeugt das PDF
/// aus den editierten Werten. Reines Ausgabe-Formular — es wird NICHTS
/// persistiert.
/// </summary>
[Authorize(Roles = "admin,superuser,user")]
[ApiController]
[Route("api/ahv-anmeldung")]
public class AhvAnmeldungController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AhvAnmeldungPdfService _pdf;
    public AhvAnmeldungController(AppDbContext db, AhvAnmeldungPdfService pdf)
    {
        _db = db; _pdf = pdf;
    }

    /// <summary>Vorbefüllung für einen MA (alle Felder editierbar im UI).</summary>
    [HttpGet("{empId:int}/prefill")]
    public async Task<IActionResult> Prefill(int empId)
    {
        var e = await _db.Employees.AsNoTracking()
            .Include(x => x.NationalityRef)
            .FirstOrDefaultAsync(x => x.Id == empId);
        if (e == null) return NotFound(new { error = "EMP_NOT_FOUND" });

        // Nationalität als deutscher Volltext (Konvention: nie nur ISO-Code)
        string? natName = null;
        var natCode = e.NationalityRef?.Code;
        if (!string.IsNullOrWhiteSpace(natCode))
        {
            natName = await _db.Nationalities
                .Where(n => n.Code == natCode)
                .Select(n => n.NameDe)
                .FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(natName)) natName = natCode;
        }

        // Filiale + Stellenantritt: jüngste aktive Anstellung, sonst jüngste.
        var emp = await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == empId && em.CompanyProfileId != null)
            .OrderByDescending(em => em.IsActive)
            .ThenByDescending(em => em.ContractStartDate)
            .FirstOrDefaultAsync();
        var cp = emp?.CompanyProfileId != null
            ? await _db.CompanyProfiles.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == emp.CompanyProfileId.Value)
            : null;

        var firma = cp == null ? null
            : string.IsNullOrWhiteSpace(cp.BranchName)
                ? cp.CompanyName
                : $"{cp.CompanyName}, {cp.BranchName}";

        return Ok(new
        {
            employeeId  = e.Id,
            employeeNumber = e.EmployeeNumber,
            wohnsitzland = "Schweiz",
            grenzgaenger = false,
            name        = e.LastName,
            ledigname   = e.MaidenName,
            vornamen    = (e.FirstName ?? "").ToUpperInvariant(),
            geburtsdatum = e.DateOfBirth?.ToString("dd.MM.yyyy"),
            ahvNummer   = "",   // Formularzweck: Nummer ist ja nicht vorhanden
            geschlecht  = NormalizeGender(e.Gender, e.Salutation),
            strasse     = e.Street,
            hausNr      = "",   // employee.street enthält Strasse+Nr kombiniert
            plz         = e.ZipCode,
            ort         = e.City,
            telefon     = e.PhoneMobile ?? e.Phone2,
            email       = e.Email,
            staatsangehoerigkeit = natName,
            geburtsort  = "",
            mutterName = "", mutterVornamen = "",
            vaterName  = "", vaterVornamen  = "",
            grund      = "ZUZUG",
            grundText  = "",
            firmenname = firma,
            abrechnungsnummer = cp?.AhvKasse,
            firmaStrasse = cp?.Street,
            firmaHausNr  = cp?.HouseNumber,
            firmaPlz     = cp?.ZipCode,
            firmaOrt     = cp?.City,
            stellenantritt = (emp?.ContractStartDate ?? e.EntryDate)?.ToString("dd.MM.yyyy"),
            beilageAusweiskopie = true,
        });
    }

    /// <summary>PDF aus den (ggf. editierten) Werten erzeugen.</summary>
    [HttpPost("{empId:int}/pdf")]
    public async Task<IActionResult> Pdf(int empId, [FromBody] AhvAnmeldungDto dto)
    {
        var e = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == empId);
        if (e == null) return NotFound(new { error = "EMP_NOT_FOUND" });

        var data = new AhvAnmeldungPdfService.AhvAnmeldungData(
            Wohnsitzland: dto.Wohnsitzland,
            Grenzgaenger: dto.Grenzgaenger ?? false,
            Name:         dto.Name,
            Ledigname:    dto.Ledigname,
            Vornamen:     dto.Vornamen,
            Geburtsdatum: dto.Geburtsdatum,
            AhvNummer:    dto.AhvNummer,
            Geschlecht:   dto.Geschlecht,
            Strasse:      dto.Strasse,
            HausNr:       dto.HausNr,
            Plz:          dto.Plz,
            Ort:          dto.Ort,
            Telefon:      dto.Telefon,
            Email:        dto.Email,
            Staatsangehoerigkeit: dto.Staatsangehoerigkeit,
            Geburtsort:   dto.Geburtsort,
            MutterName:   dto.MutterName,
            MutterVornamen: dto.MutterVornamen,
            VaterName:    dto.VaterName,
            VaterVornamen: dto.VaterVornamen,
            Grund:        dto.Grund,
            GrundText:    dto.GrundText,
            Firmenname:   dto.Firmenname,
            Abrechnungsnummer: dto.Abrechnungsnummer,
            FirmaStrasse: dto.FirmaStrasse,
            FirmaHausNr:  dto.FirmaHausNr,
            FirmaPlz:     dto.FirmaPlz,
            FirmaOrt:     dto.FirmaOrt,
            Stellenantritt: dto.Stellenantritt,
            BeilageAusweiskopie: dto.BeilageAusweiskopie ?? true);

        byte[] bytes;
        try { bytes = _pdf.Generate(data); }
        catch (FileNotFoundException ex)
        { return StatusCode(500, new { error = "TEMPLATE_MISSING", message = ex.Message }); }

        return File(bytes, "application/pdf",
            $"{e.EmployeeNumber}-AHV-Anmeldung-318260.pdf");
    }

    private static string? NormalizeGender(string? gender, string? salutation)
    {
        var g = (gender ?? "").Trim().ToLowerInvariant();
        if (g is "f" or "w" or "female" or "frau" or "weiblich") return "F";
        if (g is "m" or "male" or "mann" or "herr" or "männlich") return "M";
        var s = (salutation ?? "").Trim().ToLowerInvariant();
        if (s.StartsWith("frau")) return "F";
        if (s.StartsWith("herr")) return "M";
        return null;
    }
}

public class AhvAnmeldungDto
{
    public string? Wohnsitzland { get; set; }
    public bool?   Grenzgaenger { get; set; }
    public string? Name { get; set; }
    public string? Ledigname { get; set; }
    public string? Vornamen { get; set; }
    public string? Geburtsdatum { get; set; }
    public string? AhvNummer { get; set; }
    public string? Geschlecht { get; set; }
    public string? Strasse { get; set; }
    public string? HausNr { get; set; }
    public string? Plz { get; set; }
    public string? Ort { get; set; }
    public string? Telefon { get; set; }
    public string? Email { get; set; }
    public string? Staatsangehoerigkeit { get; set; }
    public string? Geburtsort { get; set; }
    public string? MutterName { get; set; }
    public string? MutterVornamen { get; set; }
    public string? VaterName { get; set; }
    public string? VaterVornamen { get; set; }
    public string? Grund { get; set; }
    public string? GrundText { get; set; }
    public string? Firmenname { get; set; }
    public string? Abrechnungsnummer { get; set; }
    public string? FirmaStrasse { get; set; }
    public string? FirmaHausNr { get; set; }
    public string? FirmaPlz { get; set; }
    public string? FirmaOrt { get; set; }
    public string? Stellenantritt { get; set; }
    public bool?   BeilageAusweiskopie { get; set; }
}

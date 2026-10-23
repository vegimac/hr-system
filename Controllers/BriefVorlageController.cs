using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.WebStamp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Briefverwaltung: der Lauftext liegt in OneCrew, der Brief selbst
/// (Fenster, Datum, Gruss) kommt aus der Briefpost. Frankieren ruft der
/// Browser danach über /api/webstamp/brief auf — dort bleibt die Sperre,
/// dass Bestellungen nur in der Testumgebung der Post laufen.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/brief-vorlagen")]
public class BriefVorlageController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly WebStampBriefPdfService _pdf;

    public BriefVorlageController(AppDbContext db, WebStampBriefPdfService pdf)
    {
        _db = db;
        _pdf = pdf;
    }

    public class TextDto
    {
        public string? Betreff { get; set; }
        public string? Text { get; set; }
    }

    public class AuftragDto
    {
        public int EmployeeId { get; set; }
        public string? Betreff { get; set; }
        public string? Text { get; set; }
        /// <summary>App-User aus dem HR-Team. Leer = der angemeldete Benutzer.</summary>
        public int? UnterzeichnerId { get; set; }
    }

    /// <summary>Aktive Personen mit Häkchen HR-Team, plus der angemeldete Benutzer.</summary>
    [HttpGet("unterzeichner")]
    public async Task<IActionResult> Unterzeichner()
    {
        var ich = AktuelleUserId();
        var users = await _db.AppUsers.AsNoTracking()
            .Where(u => u.IsActive && (u.IsHrTeam || u.Id == ich))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Username })
            .ToListAsync();
        var personen = users
            .Select(u =>
            {
                var name = $"{u.FirstName} {u.LastName}".Trim();
                return new { id = u.Id, name = name.Length > 0 ? name : (u.Username ?? "") };
            })
            .OrderBy(u => u.name, StringComparer.Create(new System.Globalization.CultureInfo("de-CH"), true))
            .ToList();
        return Ok(new { vorgabe = ich > 0 ? ich : (int?)null, personen });
    }

    [HttpGet("austritt")]
    public async Task<IActionResult> AustrittLesen()
    {
        var v = await _db.BriefVorlagen.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == BriefVorlageText.AustrittCode);
        return Ok(new
        {
            code = BriefVorlageText.AustrittCode,
            name = v?.Name ?? BriefVorlageText.AustrittName,
            betreff = v?.Betreff ?? BriefVorlageText.AustrittBetreff,
            text = v?.Text ?? BriefVorlageText.AustrittText,
            platzhalter = BriefPlatzhalter.Bekannt.Select(p => new { key = p.Key, label = p.Label }),
        });
    }

    [HttpPut("austritt")]
    public async Task<IActionResult> AustrittSpeichern([FromBody] TextDto dto)
    {
        var betreff = (dto.Betreff ?? "").Trim();
        if (betreff.Length == 0)
            return BadRequest(new { error = "BETREFF", message = "Bitte einen Betreff eingeben." });

        var v = await _db.BriefVorlagen.FirstOrDefaultAsync(x => x.Code == BriefVorlageText.AustrittCode);
        if (v == null)
        {
            v = new BriefVorlage
            {
                Code = BriefVorlageText.AustrittCode,
                Name = BriefVorlageText.AustrittName,
            };
            _db.BriefVorlagen.Add(v);
        }
        v.Betreff = betreff;
        v.Text = dto.Text ?? "";
        v.GeaendertAm = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>PDF zum Anschauen. Unvollständige Adresse bleibt ein Entwurf, die Post wird nicht gefragt.</summary>
    [HttpPost("austritt/pdf")]
    public async Task<IActionResult> AustrittPdf([FromBody] AuftragDto dto)
    {
        var (bau, fehler) = await BaueAsync(dto);
        if (fehler != null) return fehler;
        var pdf = _pdf.Generate(bau!.Daten);
        var nr = string.IsNullOrWhiteSpace(bau.Personalnummer)
            ? "Austrittsbrief.pdf"
            : "Austrittsbrief-" + System.Text.RegularExpressions.Regex.Replace(bau.Personalnummer, @"[^\w\-]", "") + ".pdf";
        return File(pdf, "application/pdf", nr);
    }

    /// <summary>
    /// Gefüllter Text plus Produkt und Fenster aus der Briefpost.
    /// Der Browser schickt das an /api/webstamp/brief/vorschau oder /senden.
    /// </summary>
    [HttpPost("austritt/ausfuellen")]
    public async Task<IActionResult> AustrittAusfuellen([FromBody] AuftragDto dto)
    {
        var (bau, fehler) = await BaueAsync(dto);
        if (fehler != null) return fehler;
        return Ok(new
        {
            employeeId = bau!.EmployeeId,
            filialeId = bau.FilialeId,
            betreff = bau.Daten.Betreff,
            text = bau.Daten.Text,
            absenderName = bau.Daten.AbsenderName,
            produktNummer = bau.ProduktNummer,
            fensterRechts = bau.Daten.FensterRechts,
            frankierbar = bau.Frankierbar,
            grund = bau.Grund,
            hinweise = bau.Hinweise,
        });
    }

    private async Task<(Bau? Bau, IActionResult? Fehler)> BaueAsync(AuftragDto dto)
    {
        var rohBetreff = (dto.Betreff ?? "").Trim();
        if (rohBetreff.Length == 0)
            return (null, BadRequest(new { error = "BETREFF", message = "Bitte einen Betreff eingeben." }));
        if (dto.EmployeeId <= 0)
            return (null, BadRequest(new { error = "MA", message = "Bitte einen Mitarbeitenden wählen." }));

        var ma = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.EmployeeId);
        if (ma == null)
            return (null, NotFound(new { error = "MA_UNBEKANNT", message = "Mitarbeiter nicht gefunden." }));

        var anstellungen = await _db.Employments.AsNoTracking()
            .Where(x => x.EmployeeId == ma.Id && x.CompanyProfileId != null)
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => new { x.CompanyProfileId, x.JobTitle, x.IsActive })
            .ToListAsync();
        var anstellung = anstellungen.FirstOrDefault(x => x.IsActive) ?? anstellungen.FirstOrDefault();

        CompanyProfile? filiale = null;
        if (anstellung?.CompanyProfileId is int cpId)
            filiale = await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cpId);

        var setting = await _db.WebStampSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
        var fensterRechts = setting?.FensterRechts ?? false;
        var produkt = setting?.ProduktNummer;

        var austritt = ma.ExitDate ?? ma.KuendigungPer;
        var austrittText = austritt == null ? "" : austritt.Value.ToString("dd.MM.yyyy");
        var vorname = (ma.FirstName ?? "").Trim();
        var nachname = (ma.LastName ?? "").Trim();
        var anrede = (ma.LetterSalutation ?? "").Trim();
        if (anrede.Length == 0)
            anrede = vorname.Length == 0 ? "Guten Tag" : "Guten Tag " + vorname;
        var funktion = (anstellung?.JobTitle ?? "").Trim();
        var filialeName = filiale == null
            ? ""
            : (string.IsNullOrWhiteSpace(filiale.BranchName) ? filiale.CompanyName : filiale.BranchName).Trim();
        var ort = (filiale?.City ?? "").Trim();

        var werte = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["anrede"] = anrede,
            ["vorname"] = vorname,
            ["nachname"] = nachname,
            ["name"] = $"{vorname} {nachname}".Trim(),
            ["personalnummer"] = ma.EmployeeNumber ?? "",
            ["austrittsdatum"] = austrittText,
            ["funktion"] = funktion,
            ["filiale"] = filialeName,
            ["ort"] = ort,
            ["datum"] = DateTime.Now.ToString("dd.MM.yyyy"),
        };

        var hinweise = new List<string>();
        if (austrittText.Length == 0)
            hinweise.Add("Austrittsdatum fehlt. Es steht weder ein Austritt noch ein Kündigungstermin am Mitarbeitenden.");
        if (funktion.Length == 0)
            hinweise.Add("Funktion fehlt am aktuellen Vertrag.");

        string? grund = null;
        if (!WebStampController.AdresseVollstaendig(ma))
            grund = $"{vorname} {nachname}".Trim() + ": Strasse, PLZ oder Ort fehlen.";
        else if (filiale == null)
            grund = "Keine Anstellung mit Filiale. Der Brief braucht einen Absender.";
        else if (!FilialeVollstaendig(filiale))
            grund = "Bei der Filiale fehlen Strasse, PLZ oder Ort. Die Post braucht eine Absenderadresse.";
        else if (produkt is not > 0)
            grund = "Unter Briefpost ist noch kein Produkt gespeichert.";

        var (absender, unterzeichnerFehler) = await UnterzeichnerAsync(dto.UnterzeichnerId);
        if (unterzeichnerFehler != null) return (null, unterzeichnerFehler);
        var daten = new WebStampBriefPdfService.BriefDaten(
            filiale != null ? AbsenderZeilen(filiale) : new List<string>(),
            WebStampController.AdressZeilen(ma),
            string.IsNullOrWhiteSpace(ort) ? null : ort,
            DateOnly.FromDateTime(DateTime.Now),
            BriefPlatzhalter.FuegeEin(rohBetreff, werte),
            BriefPlatzhalter.FuegeEin(dto.Text, werte),
            string.IsNullOrWhiteSpace(absender) ? null : absender,
            fensterRechts);

        return (new Bau(ma.Id, filiale?.Id, ma.EmployeeNumber ?? "", produkt, grund == null, grund, hinweise, daten), null);
    }

    private int AktuelleUserId()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idStr, out var id) ? id : 0;
    }

    private async Task<(string Name, IActionResult? Fehler)> UnterzeichnerAsync(int? gewaehlt)
    {
        var ich = AktuelleUserId();
        var id = gewaehlt is > 0 ? gewaehlt.Value : ich;
        if (id <= 0)
            return ("", BadRequest(new { error = "UNTERZEICHNER", message = "Bitte einen Unterzeichner aus dem HR-Team wählen." }));

        var user = await _db.AppUsers.AsNoTracking()
            .Where(u => u.Id == id && u.IsActive)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Username, u.IsHrTeam })
            .FirstOrDefaultAsync();
        if (user == null || (!user.IsHrTeam && user.Id != ich))
            return ("", BadRequest(new { error = "UNTERZEICHNER", message = "Der Unterzeichner muss aus dem HR-Team sein." }));

        var name = $"{user.FirstName} {user.LastName}".Trim();
        if (name.Length == 0) name = user.Username ?? "";
        return (name, null);
    }

    /// <summary>Gleiche Zeilen wie der Brief der Briefpost, damit Vorschau und Frankatur dasselbe Couvert ergeben.</summary>
    private static List<string> AbsenderZeilen(CompanyProfile c)
    {
        var z = new List<string> { c.CompanyName };
        if (!string.IsNullOrWhiteSpace(c.BranchName)) z.Add(c.BranchName!.Trim());
        var strasse = $"{c.Street} {c.HouseNumber}".Trim();
        if (strasse.Length > 0) z.Add(strasse);
        var plzOrt = $"{c.ZipCode} {c.City}".Trim();
        if (plzOrt.Length > 0) z.Add(plzOrt);
        return z.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    private static bool FilialeVollstaendig(CompanyProfile c)
        => !string.IsNullOrWhiteSpace(c.CompanyName)
        && !string.IsNullOrWhiteSpace(c.Street)
        && !string.IsNullOrWhiteSpace(c.ZipCode)
        && !string.IsNullOrWhiteSpace(c.City);

    private sealed record Bau(
        int EmployeeId,
        int? FilialeId,
        string Personalnummer,
        int? ProduktNummer,
        bool Frankierbar,
        string? Grund,
        List<string> Hinweise,
        WebStampBriefPdfService.BriefDaten Daten);
}

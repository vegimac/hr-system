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
/// Beides read-only PDF — schreibt nichts. Rollen wie Mutterschafts-Modul
/// (admin/superuser/user — GF muss alles sehen, Walter 20.07.2026).
/// </summary>
[Authorize(Roles = "admin,superuser,user")]
[ApiController]
[Route("api/mutterschaft-vereinbarung")]
public class MutterschaftVereinbarungController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly MutterschaftPdfService _pdf;
    private readonly EmailService _email;
    private readonly RisikobeurteilungPdfService _risiko;

    public MutterschaftVereinbarungController(AppDbContext db, MutterschaftPdfService pdf, EmailService email, RisikobeurteilungPdfService risiko)
    {
        _db = db; _pdf = pdf; _email = email; _risiko = risiko;
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

    public class BestaetigungDto
    {
        /// <summary>GLEICH | ANDERS | KEINE</summary>
        public string Rueckkehr { get; set; } = "GLEICH";
        public int UrlaubBezahlt { get; set; }
        public int UrlaubUnbezahlt { get; set; }
        public DateOnly? Wiederaufnahme { get; set; }
        public decimal? PensumProzent { get; set; }
        public string? RueckkehrRestaurant { get; set; }
        public bool Pensionskasse { get; set; }
        public string? KindName { get; set; }
        public bool Eingeschrieben { get; set; }
    }

    /// <summary>
    /// Mutterschaftsbestätigung nach der Geburt (Walter-Vorgabe 16.07.2026,
    /// nach Word-Vorlage): Gratulation, Urlaubs-Zeitraum 98 Tage ab Geburt,
    /// Rückkehr-Varianten bzw. Beendigung, EO-Formular-Frist. Setzt das
    /// erfasste effektive Geburtsdatum voraus.
    /// </summary>
    [HttpPost("{pregnancyId:int}/bestaetigung-pdf")]
    public async Task<IActionResult> BestaetigungPdf(int pregnancyId, [FromBody] BestaetigungDto dto)
    {
        var common = await LoadCommonAsync(pregnancyId);
        if (common == null) return NotFound(new { error = "PREGNANCY_NOT_FOUND" });
        var geburt = await _db.EmployeePregnancies.AsNoTracking()
            .Where(x => x.Id == pregnancyId).Select(x => x.Geburtsdatum).FirstOrDefaultAsync();
        if (!geburt.HasValue)
            return Conflict(new { error = "GEBURT_FEHLT", message = "Bitte zuerst das definitive Geburtsdatum eintragen (Fahrplan → Geburt eintragen)." });

        var rueckkehr = (dto.Rueckkehr ?? "GLEICH").Trim().ToUpperInvariant();
        if (rueckkehr is not ("GLEICH" or "ANDERS" or "KEINE"))
            return BadRequest(new { error = "RUECKKEHR_UNGUELTIG", message = "Rückkehr muss GLEICH, ANDERS oder KEINE sein." });

        var opt = new MutterschaftPdfService.BestOptionen(
            Geburt:              geburt.Value,
            Rueckkehr:           rueckkehr,
            UrlaubBezahlt:       Math.Max(0, dto.UrlaubBezahlt),
            UrlaubUnbezahlt:     Math.Max(0, dto.UrlaubUnbezahlt),
            Wiederaufnahme:      dto.Wiederaufnahme,
            PensumProzent:       dto.PensumProzent,
            RueckkehrRestaurant: dto.RueckkehrRestaurant,
            Pensionskasse:       dto.Pensionskasse,
            KindName:            dto.KindName,
            Eingeschrieben:      dto.Eingeschrieben);

        try
        {
            var bytes = _pdf.GenerateBestaetigung(common, opt);
            return File(bytes, "application/pdf",
                $"Mutterschaftsbestaetigung_{common.MaName}_{common.MaVorname}.pdf".Replace(" ", "_"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "PDF_FEHLER", message = ex.GetBaseException().Message });
        }
    }

    public class ArztbriefDto
    {
        public int ArztId { get; set; }
    }

    /// <summary>
    /// Brief an den behandelnden Arzt (Walter-Vorgabe 16.07.2026, nach
    /// Word-Vorlage): medizinische Eignungsuntersuchung Mutterschutz.
    /// Arzt aus dem Ärzte-Verzeichnis; read-only PDF.
    /// </summary>
    [HttpPost("{pregnancyId:int}/arztbrief-pdf")]
    public async Task<IActionResult> ArztbriefPdf(int pregnancyId, [FromBody] ArztbriefDto dto)
    {
        var common = await LoadCommonAsync(pregnancyId);
        if (common == null) return NotFound(new { error = "PREGNANCY_NOT_FOUND" });
        var arzt = await _db.Aerzte.AsNoTracking().FirstOrDefaultAsync(a => a.Id == dto.ArztId);
        if (arzt == null) return NotFound(new { error = "ARZT_NOT_FOUND", message = "Arzt nicht im Verzeichnis gefunden." });
        try
        {
            var bytes = _pdf.GenerateArztbrief(common, ToArztInfo(arzt));
            return File(bytes, "application/pdf",
                $"Arztbrief_Eignungsuntersuchung_{common.MaName}_{common.MaVorname}.pdf".Replace(" ", "_"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "PDF_FEHLER", message = ex.GetBaseException().Message });
        }
    }

    /// <summary>
    /// Eignungsbeurteilung — Ärztliches Zeugnis nach MuSchV Art. 3
    /// (Walter-Vorgabe 16.07.2026, nach Word-Vorlage): dem Arzt zusammen
    /// mit der Risikobeurteilung mitgegeben. Arzt optional (arztId 0 = leer).
    /// </summary>
    [HttpPost("{pregnancyId:int}/eignung-pdf")]
    public async Task<IActionResult> EignungPdf(int pregnancyId, [FromBody] ArztbriefDto dto)
    {
        var common = await LoadCommonAsync(pregnancyId);
        if (common == null) return NotFound(new { error = "PREGNANCY_NOT_FOUND" });
        // Arzt-Feld bleibt bewusst LEER (Walter 16.07.2026: dort setzt jeder
        // Arzt seinen eigenen Praxis-Stempel) — dto.ArztId wird ignoriert.
        try
        {
            var bytes = _pdf.GenerateEignung(common);
            return File(bytes, "application/pdf",
                $"Eignungsbeurteilung_{common.MaName}_{common.MaVorname}.pdf".Replace(" ", "_"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "PDF_FEHLER", message = ex.GetBaseException().Message });
        }
    }

    /// <summary>
    /// Arztbrief direkt per E-Mail an die Praxis senden (PDF im Anhang).
    /// Wird vom Frontend NUR nach expliziter Bestätigung aufgerufen.
    /// </summary>
    [HttpPost("{pregnancyId:int}/arztbrief-email")]
    public async Task<IActionResult> ArztbriefEmail(int pregnancyId, [FromBody] ArztbriefDto dto)
    {
        var common = await LoadCommonAsync(pregnancyId);
        if (common == null) return NotFound(new { error = "PREGNANCY_NOT_FOUND" });
        var arzt = await _db.Aerzte.AsNoTracking().FirstOrDefaultAsync(a => a.Id == dto.ArztId);
        if (arzt == null) return NotFound(new { error = "ARZT_NOT_FOUND", message = "Arzt nicht im Verzeichnis gefunden." });
        if (string.IsNullOrWhiteSpace(arzt.Email))
            return BadRequest(new { error = "ARZT_OHNE_EMAIL", message = "Für diesen Arzt ist keine E-Mail-Adresse hinterlegt." });

        try
        {
            var bytes = _pdf.GenerateArztbrief(common, ToArztInfo(arzt));
            var arztName = string.Join(" ", new[] { arzt.Titel, arzt.Vorname, arzt.Nachname }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            var subject = $"Medizinische Eignungsuntersuchung Mutterschutz — Frau {common.MaVorname} {common.MaName}";
            var nl = Environment.NewLine;
            var text = "Sehr geehrte Damen und Herren" + nl + nl
                     + "Im Anhang erhalten Sie unser Schreiben betreffend die medizinische Eignungsuntersuchung "
                     + $"für Frau {common.MaVorname} {common.MaName}"
                     + (common.MaGeburtsdatum.HasValue ? $", geb. {common.MaGeburtsdatum.Value:dd.MM.yyyy}" : "")
                     + "." + nl + nl + "Freundliche Grüsse" + nl
                     + $"{common.UnterzeichnerName}" + nl
                     + $"{common.FirmaName}{(string.IsNullOrWhiteSpace(common.RestaurantName) ? "" : " · " + common.RestaurantName)}";
            var html = text.Replace(nl, "<br>");
            // Beilage: personalisierte Risikobeurteilung (Walter 16.07.2026)
            byte[]? risiko = null;
            try { risiko = _risiko.Generate(await BuildBetriebsAngabenAsync(pregnancyId, common)); } catch { /* Brief geht trotzdem raus */ }
            var anhaenge = new List<(byte[] Data, string Name)>
            {
                (bytes, $"Arztbrief_Eignungsuntersuchung_{common.MaName}_{common.MaVorname}.pdf".Replace(" ", "_"))
            };
            if (risiko != null)
                anhaenge.Add((risiko, $"Risikobeurteilung_Mutterschutz_{common.MaName}_{common.MaVorname}.pdf".Replace(" ", "_")));
            // Beilage 2: Eignungsbeurteilung (Aerztliches Zeugnis, MuSchV Art. 3)
            try
            {
                var eignung = _pdf.GenerateEignung(common);
                anhaenge.Add((eignung, $"Eignungsbeurteilung_{common.MaName}_{common.MaVorname}.pdf".Replace(" ", "_")));
            }
            catch { /* Brief geht trotzdem raus */ }
            var ok = await _email.SendWithAttachmentsAsync(arzt.Email!, arztName, subject, html, text, anhaenge);
            if (!ok)
                return StatusCode(500, new { error = "MAIL_FEHLER", message = "E-Mail-Versand fehlgeschlagen — SMTP-Konfiguration prüfen (Systemeinstellungen → E-Mail)." });
            return Ok(new { ok = true, to = arzt.Email });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "MAIL_FEHLER", message = ex.GetBaseException().Message });
        }
    }

    /// <summary>
    /// Risikobeurteilung Mutterschutz «Für den Arzt» — das offizielle
    /// 7-Seiten-PDF mit den Angaben der Filiale/MA auf Seite 1
    /// (Walter-Vorgabe 16.07.2026). Beilage zum Arztbrief.
    /// </summary>
    [HttpGet("{pregnancyId:int}/risikobeurteilung-pdf")]
    public async Task<IActionResult> RisikobeurteilungPdf(int pregnancyId)
    {
        var common = await LoadCommonAsync(pregnancyId);
        if (common == null) return NotFound(new { error = "PREGNANCY_NOT_FOUND" });
        try
        {
            var bytes = _risiko.Generate(await BuildBetriebsAngabenAsync(pregnancyId, common));
            return File(bytes, "application/pdf",
                $"Risikobeurteilung_Mutterschutz_{common.MaName}_{common.MaVorname}.pdf".Replace(" ", "_"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "PDF_FEHLER", message = ex.GetBaseException().Message });
        }
    }

    private async Task<RisikobeurteilungPdfService.BetriebsAngaben> BuildBetriebsAngabenAsync(
        int pregnancyId, MutterschaftPdfService.MvCommon common)
    {
        // Filial-Telefon (Konvention: IMMER CompanyProfile.Phone, nie privat).
        var empId = await _db.EmployeePregnancies.AsNoTracking()
            .Where(x => x.Id == pregnancyId).Select(x => x.EmployeeId).FirstOrDefaultAsync();
        var cpId = await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == empId)
            .OrderByDescending(em => em.IsActive)
            .ThenByDescending(em => em.ContractStartDate)
            .Select(em => em.CompanyProfileId)
            .FirstOrDefaultAsync();
        string? phone = null;
        if (cpId != null)
            phone = await _db.CompanyProfiles.AsNoTracking()
                .Where(c => c.Id == cpId.Value).Select(c => c.Phone).FirstOrDefaultAsync();

        // MA-Funktion: Klartext aus app_text (JOB_GROUP), Fallback Code.
        var jobCode = await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == empId)
            .OrderByDescending(em => em.IsActive)
            .ThenByDescending(em => em.ContractStartDate)
            .Select(em => em.JobTitle)
            .FirstOrDefaultAsync();
        string? jobDisplay = null;
        if (!string.IsNullOrWhiteSpace(jobCode))
        {
            var key = $"{jobCode}.NAME";
            jobDisplay = await _db.AppTexts.AsNoTracking()
                .Where(t => t.Module == "JOB_GROUP" && t.TextKey == key && t.LanguageCode == "de" && t.IsActive)
                .Select(t => t.Content)
                .FirstOrDefaultAsync() ?? jobCode;
        }

        // Unterzeichner-Name in Vorname/Name splitten (letztes Wort = Nachname).
        string? vVor = null, vNach = null;
        if (!string.IsNullOrWhiteSpace(common.UnterzeichnerName))
        {
            var teile = common.UnterzeichnerName.Trim().Split(' ');
            vNach = teile[^1];
            vVor  = teile.Length > 1 ? string.Join(" ", teile[..^1]) : null;
        }

        return new RisikobeurteilungPdfService.BetriebsAngaben(
            Name:          $"{common.FirmaName}{(string.IsNullOrWhiteSpace(common.RestaurantName) ? "" : " · " + common.RestaurantName)}",
            Strasse:       common.FirmaStrasse,
            PlzOrt:        common.FirmaPlzOrt,
            Kontaktperson: common.UnterzeichnerName,
            Telefon:       phone,
            MaVorname:     common.MaVorname,
            MaName:        common.MaName,
            MaFunktion:    jobDisplay,
            MaGeburtsdatum: common.MaGeburtsdatum,
            VerantwortlichVorname:  vVor,
            VerantwortlichName:     vNach,
            VerantwortlichFunktion: common.UnterzeichnerTitel);
    }

    private static MutterschaftPdfService.ArztInfo ToArztInfo(Arzt a) => new(
        Titel:      a.Titel,
        Vorname:    a.Vorname,
        Nachname:   a.Nachname,
        Fachgebiet: a.Fachgebiet,
        PraxisName: a.PraxisName,
        Strasse:    a.Strasse,
        PlzOrt:     string.Join(" ", new[] { a.Plz, a.Ort }.Where(x => !string.IsNullOrWhiteSpace(x))));

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
            SignaturePng:      sigPng,
            FirmaTelefon:      cp?.Phone,
            FirmaEmail:        cp?.Email);
    }
}

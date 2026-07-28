using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Kündigungsschreiben (Walter-Vorgabe 22.06.2026). Liefert (a) die vor-
/// berechneten Brief-Daten inkl. Kündigungsfrist, letztem Arbeitstag und
/// Sperrfrist-Prüfung (GET …/info) und (b) das fertige PDF (POST …/pdf).
/// GF (user) inkl. — Kündigung während Probezeit aus Restaurant-Admin
/// (Walter 20.07.2026: auf GF-sichtbaren Screens alles freigeben).
/// </summary>
[Authorize(Roles = "admin,superuser,user")]
[ApiController]
[Route("api/kuendigung")]
public class KuendigungController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly KuendigungPdfService _pdf;
    private readonly SperrfristService _sperrfrist;

    public KuendigungController(AppDbContext db, KuendigungPdfService pdf, SperrfristService sperrfrist)
    {
        _db = db; _pdf = pdf; _sperrfrist = sperrfrist;
    }

    public record NoticeInfo(bool InProbation, int Dienstjahr, int? Months, int? Days,
                             string FristText, DateOnly LetzterArbeitstag, string RuleText);

    public class KuendigungPdfDto
    {
        public DateOnly? KuendigungsDatum { get; set; }
        public DateOnly? LetzterArbeitstag { get; set; }   // optionaler Override
        public string?   Ort { get; set; }
        public string?   Grund { get; set; }               // optional (Freitext / Auswahl)
        /// <summary>ordentlich | probezeit | fristlos — steuert die Frist-Rechnung (Walter 21.07.2026).</summary>
        public string?   GrundType { get; set; }
        /// <summary>
        /// true = Einschreiben («EINSCHREIBEN» über der Adresse);
        /// false = persönlich übergeben (PDF: Zeuge der Übergabe zwischen AG- und MA-Unterschrift).
        /// </summary>
        public bool      Eingeschrieben { get; set; }
    }

    [HttpGet("{empId:int}/info")]
    public async Task<IActionResult> GetInfo(
        int empId,
        [FromQuery] DateOnly? datum = null,
        [FromQuery] string? grundType = null)
    {
        var guard = await GuardBranchAsync(empId);
        if (guard != null) return guard;
        var ctx = await LoadContextAsync(empId);
        if (ctx is null) return NotFound(new { error = "EMP_NOT_FOUND" });
        var (e, emp, cp) = ctx.Value;

        var kdat = datum ?? DateOnly.FromDateTime(DateTime.Today);
        var notice = ComputeNotice(e, emp, cp, kdat, grundType);
        var sperr = await _sperrfrist.ComputeAsync(empId, kdat);

        return Ok(new
        {
            employee = new
            {
                id   = e.Id,
                name = ($"{e.FirstName} {e.LastName}").Trim(),
                briefanrede = Briefanrede(e),
                strasse = e.Street,
                plzOrt  = Join(e.ZipCode, e.City),
            },
            company = new
            {
                name    = cp?.CompanyName,
                strasse = Join(cp?.Street, cp?.HouseNumber),
                plzOrt  = Join(cp?.ZipCode, cp?.City),
                ort     = cp?.City,
            },
            entryDate        = e.EntryDate.HasValue ? DateOnly.FromDateTime(e.EntryDate.Value).ToString("yyyy-MM-dd") : null,
            dienstjahr       = notice.Dienstjahr,
            inProbation      = notice.InProbation,
            noticeMonths     = notice.Months,
            noticeDays       = notice.Days,
            noticeText       = notice.FristText,
            noticeRule       = notice.RuleText,
            kuendigungsDatum = kdat.ToString("yyyy-MM-dd"),
            letzterArbeitstag = notice.LetzterArbeitstag.ToString("yyyy-MM-dd"),
            sperrfrist = new
            {
                status     = sperr.Status,
                statusText = sperr.StatusText,
                kuendigungAbDatum = sperr.KuendigungAbDatum?.ToString("yyyy-MM-dd"),
                // GESCHUETZT = Sperrfrist aktiv → Kündigung aktuell unzulässig.
                blocked    = sperr.Status == "GESCHUETZT",
                // Nach AU-Ende keine Soft-Warnung mehr — normale L-GAV-Frist.
                warn       = false,
            }
        });
    }

    [HttpPost("{empId:int}/pdf")]
    public async Task<IActionResult> GetPdf(int empId, [FromBody] KuendigungPdfDto dto)
    {
        var guard = await GuardBranchAsync(empId);
        if (guard != null) return guard;
        var ctx = await LoadContextAsync(empId);
        if (ctx is null) return NotFound(new { error = "EMP_NOT_FOUND" });
        var (e, emp, cp) = ctx.Value;

        var kdat   = dto.KuendigungsDatum ?? DateOnly.FromDateTime(DateTime.Today);
        var notice = ComputeNotice(e, emp, cp, kdat, dto.GrundType);
        // Letzter Arbeitstag: Override (falls HR angepasst) sonst berechnet.
        var letzter = dto.LetzterArbeitstag ?? notice.LetzterArbeitstag;
        var ort     = string.IsNullOrWhiteSpace(dto.Ort) ? (cp?.City ?? "") : dto.Ort!.Trim();

        // Unterschrift + Name des EINGELOGGTEN Users (nie eine andere Person).
        var (sigPng, signerName, signerFunktion) = await GetSignerAsync(cp?.Id);

        var data = new KuendigungPdfService.KuendigungData(
            FirmaName:    cp?.CompanyName,
            FirmaStrasse: Join(cp?.Street, cp?.HouseNumber),
            FirmaPlzOrt:  Join(cp?.ZipCode, cp?.City),
            MaName:       ($"{e.FirstName} {e.LastName}").Trim(),
            MaStrasse:    e.Street,
            MaPlzOrt:     Join(e.ZipCode, e.City),
            Briefanrede:  Briefanrede(e),
            Ort:          ort,
            KuendigungsDatum: kdat,
            FristText:    notice.FristText,
            LetzterArbeitstag: letzter,
            Grund:        string.IsNullOrWhiteSpace(dto.Grund) ? null : dto.Grund!.Trim(),
            UnterzeichnerName: signerName,
            Eingeschrieben: dto.Eingeschrieben,
            UnterzeichnerFunktion: signerFunktion);

        var bytes = _pdf.Generate(data, sigPng);
        // PDF allein speichert nichts am MA (Walter 21.07.2026) —
        // Eintrag «Gekündigt am / per» nur via POST …/eintragen.
        return File(bytes, "application/pdf", $"{e.EmployeeNumber}-Kuendigung.pdf");
    }

    public class KuendigungEintragenDto
    {
        public DateOnly? KuendigungsDatum { get; set; }
        public DateOnly? LetzterArbeitstag { get; set; }
        public string?   GrundType { get; set; }
        /// <summary>«AG» = durch uns (Default beim Schreiben), «AN» = durch Mitarbeiter.</summary>
        public string?   KuendigungDurch { get; set; }
        /// <summary>Austrittsgrund-Code (AustrittsgrundCodes), optional.</summary>
        public string?   Austrittsgrund { get; set; }
    }

    /// <summary>
    /// Schreibt «Gekündigt am» + «Kündigung per» + «Kündigung durch» +
    /// optional Austrittsgrund am MA (Walter 21.07.2026 / 26.07.2026).
    /// Bewusst getrennt vom PDF — Schreiben erstellen ≠ in Stammdaten eintragen.
    /// Austrittsdatum wird nicht gesetzt.
    /// </summary>
    [HttpPost("{empId:int}/eintragen")]
    public async Task<IActionResult> Eintragen(int empId, [FromBody] KuendigungEintragenDto dto)
    {
        var guard = await GuardBranchAsync(empId);
        if (guard != null) return guard;
        var tracked = await _db.Employees.FirstOrDefaultAsync(x => x.Id == empId);
        if (tracked is null) return NotFound(new { error = "EMP_NOT_FOUND" });

        var ctx = await LoadContextAsync(empId);
        if (ctx is null) return NotFound(new { error = "EMP_NOT_FOUND" });
        var (e, emp, cp) = ctx.Value;

        var kdat = dto.KuendigungsDatum ?? DateOnly.FromDateTime(DateTime.Today);
        var notice = ComputeNotice(e, emp, cp, kdat, dto.GrundType);
        var letzter = dto.LetzterArbeitstag ?? notice.LetzterArbeitstag;

        var durch = string.IsNullOrWhiteSpace(dto.KuendigungDurch)
            ? "AG" // Kündigungsschreiben = durch uns
            : dto.KuendigungDurch.Trim().ToUpperInvariant();
        if (durch != "AG" && durch != "AN")
            return BadRequest(new { error = "KUENDIGUNG_DURCH_INVALID",
                message = "Kündigung durch muss «AG» (durch uns) oder «AN» (durch Mitarbeiter) sein." });

        string? austrittsgrund = null;
        if (!string.IsNullOrWhiteSpace(dto.Austrittsgrund))
        {
            austrittsgrund = AustrittsgrundCodes.Normalize(dto.Austrittsgrund);
            if (austrittsgrund == null)
                return BadRequest(new { error = "AUSTRITTSGRUND_INVALID",
                    message = "Ungültiger Austrittsgrund." });
        }

        // Kind=Unspecified — nie UTC in timestamp without time zone (Walter 30.06.2026).
        tracked.KuendigungAusgesprochenAm = new DateTime(kdat.Year, kdat.Month, kdat.Day);
        tracked.KuendigungPer             = new DateTime(letzter.Year, letzter.Month, letzter.Day);
        tracked.KuendigungDurch           = durch;
        if (austrittsgrund != null)
            tracked.Austrittsgrund = austrittsgrund;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            ok = true,
            kuendigungAusgesprochenAm = kdat.ToString("yyyy-MM-dd"),
            kuendigungPer = letzter.ToString("yyyy-MM-dd"),
            kuendigungDurch = durch,
            austrittsgrund = tracked.Austrittsgrund
        });
    }

    public class RueckzugPdfDto
    {
        /// <summary>Datum der urspruenglich ausgesprochenen Kuendigung (Pflicht).</summary>
        public DateOnly KuendigungVom { get; set; }
        public DateOnly? Datum { get; set; }            // Briefdatum, Default heute
        public string?   Ort { get; set; }
        public string?   Grund { get; set; }            // optionaler Rueckzugs-Grund
        public bool      Eingeschrieben { get; set; }
        /// <summary>true = Schwangerschafts-Variante (Kuendigung nichtig, OR 336c) —
        /// Brief «Fortbestehen des Arbeitsverhaeltnisses» nach Walter-Text 16.07.2026.</summary>
        public bool      NichtigSchwangerschaft { get; set; }
        /// <summary>Datum, an dem die MA die Schwangerschaft gemeldet hat.</summary>
        public DateOnly? SchwangerschaftGemeldetAm { get; set; }
    }

    /// <summary>
    /// Rueckzug einer ausgesprochenen Kuendigung (Walter-Vorgabe 16.07.2026) —
    /// im HR-Bereich abgelegt (Hub-Karte «Kuendigung / Zeugnisse»). Read-only
    /// PDF; das Einverstaendnis der/des MA wird auf dem Schreiben unterzeichnet.
    /// </summary>
    [HttpPost("{empId:int}/rueckzug-pdf")]
    public async Task<IActionResult> GetRueckzugPdf(int empId, [FromBody] RueckzugPdfDto dto)
    {
        var guard = await GuardBranchAsync(empId);
        if (guard != null) return guard;
        var ctx = await LoadContextAsync(empId);
        if (ctx is null) return NotFound(new { error = "EMP_NOT_FOUND" });
        var (e, _, cp) = ctx.Value;

        if (dto.KuendigungVom == default)
            return BadRequest(new { error = "KUENDIGUNG_VOM_FEHLT", message = "Bitte das Datum der ausgesprochenen Kündigung angeben." });

        var datum = dto.Datum ?? DateOnly.FromDateTime(DateTime.Today);
        var ort   = string.IsNullOrWhiteSpace(dto.Ort) ? (cp?.City ?? "") : dto.Ort!.Trim();
        var (sigPng, signerName, signerFunktion) = await GetSignerAsync(cp?.Id);

        var data = new KuendigungPdfService.RueckzugData(
            FirmaName:    cp?.CompanyName,
            FirmaStrasse: Join(cp?.Street, cp?.HouseNumber),
            FirmaPlzOrt:  Join(cp?.ZipCode, cp?.City),
            MaName:       ($"{e.FirstName} {e.LastName}").Trim(),
            MaStrasse:    e.Street,
            MaPlzOrt:     Join(e.ZipCode, e.City),
            Briefanrede:  Briefanrede(e),
            Ort:          ort,
            Datum:        datum,
            KuendigungVom: dto.KuendigungVom,
            Grund:        string.IsNullOrWhiteSpace(dto.Grund) ? null : dto.Grund!.Trim(),
            UnterzeichnerName: signerName,
            UnterzeichnerFunktion: signerFunktion,
            Eingeschrieben: dto.Eingeschrieben,
            NichtigSchwangerschaft: dto.NichtigSchwangerschaft,
            SchwangerschaftGemeldetAm: dto.SchwangerschaftGemeldetAm);

        try
        {
            var bytes = _pdf.GenerateRueckzug(data, sigPng);
            // Walter-Vorgabe 16.07.2026 (Praezisierung): das PDF loescht die
            // Kuendigungs-Daten NICHT mehr automatisch — erst nach dem Brief
            // fragt das Frontend «Soll die Kuendigung beim MA aufgehoben
            // werden?» und ruft dann POST …/kuendigung-aufheben.
            return File(bytes, "application/pdf", $"{e.EmployeeNumber}-Kuendigungsrueckzug.pdf");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "PDF_FEHLER", message = ex.GetBaseException().Message });
        }
    }

    public class BestaetigungPdfDto
    {
        /// <summary>Kündigungsdatum des Mitarbeitenden (wann die Kündigung eingegangen ist).</summary>
        public DateOnly KuendigungsDatumMa { get; set; }
        /// <summary>Kündigung auf Datum (= letzter Arbeitstag / Vertragsende).</summary>
        public DateOnly KuendigungAuf { get; set; }
        public DateOnly? Datum { get; set; }   // Briefdatum, Default heute
        public string?   Ort { get; set; }
        public bool      Eingeschrieben { get; set; }
    }

    /// <summary>
    /// Kündigungsbestätigung (Walter 26.07.2026) — AG bestätigt den Erhalt
    /// der MA-Kündigung und das Vertragsende. Pflicht-Daten:
    /// Kündigungsdatum des Mitarbeitenden + Kündigung auf Datum.
    /// PDF: Seite 1 Brief · 2 Referenzangaben · 3 Swica · 4–5 PK-Überweisung.
    /// </summary>
    [HttpPost("{empId:int}/bestaetigung-pdf")]
    public async Task<IActionResult> GetBestaetigungPdf(int empId, [FromBody] BestaetigungPdfDto dto)
    {
        var guard = await GuardBranchAsync(empId);
        if (guard != null) return guard;
        var ctx = await LoadContextAsync(empId);
        if (ctx is null) return NotFound(new { error = "EMP_NOT_FOUND" });
        var (e, _, cp) = ctx.Value;

        if (dto.KuendigungsDatumMa == default)
            return BadRequest(new { error = "KUENDIGUNG_DATUM_MA_FEHLT", message = "Bitte das Kündigungsdatum des Mitarbeitenden angeben." });
        if (dto.KuendigungAuf == default)
            return BadRequest(new { error = "KUENDIGUNG_AUF_FEHLT", message = "Bitte das «Kündigung auf»-Datum angeben." });

        var datum = dto.Datum ?? DateOnly.FromDateTime(DateTime.Today);
        var ort   = string.IsNullOrWhiteSpace(dto.Ort) ? (cp?.City ?? "") : dto.Ort!.Trim();
        var (_, signerName, signerFunktion) = await GetSignerAsync(cp?.Id);
        // Referenzangaben: Standard-Unterzeichner der Filiale (wie Arbeitsvertrag),
        // nicht der eingeloggte User — Walter 27.07.2026.
        var (refName, refFunktion) = await GetDefaultSignatoryAsync(cp?.Id);
        var exitSurveyUrl = await ResolveExitSurveyUrlAsync(cp);

        DateOnly? geburtsdatum = e.DateOfBirth.HasValue
            ? DateOnly.FromDateTime(e.DateOfBirth.Value)
            : null;
        var telefon = !string.IsNullOrWhiteSpace(e.PhoneMobile) ? e.PhoneMobile
            : e.Phone2;

        var data = new KuendigungPdfService.BestaetigungData(
            FirmaName:       cp?.CompanyName,
            RestaurantName:  cp?.BranchName,
            FirmaStrasse:    Join(cp?.Street, cp?.HouseNumber),
            FirmaPlzOrt:     Join(cp?.ZipCode, cp?.City),
            MaName:          ($"{e.FirstName} {e.LastName}").Trim(),
            MaVorname:       (e.FirstName ?? "").Trim(),
            MaNachname:      (e.LastName ?? "").Trim(),
            MaStrasse:       e.Street,
            MaPlzOrt:        Join(e.ZipCode, e.City),
            DuAnrede:        DuAnrede(e),
            Ort:             ort,
            Datum:           datum,
            KuendigungsDatumMa: dto.KuendigungsDatumMa,
            KuendigungAuf:   dto.KuendigungAuf,
            UnterzeichnerName: signerName,
            UnterzeichnerFunktion: signerFunktion,
            Eingeschrieben:  dto.Eingeschrieben,
            ExitSurveyUrl:   exitSurveyUrl,
            MaAhvNummer:     e.SocialSecurityNumber,
            MaGeburtsdatum:  geburtsdatum,
            MaTelefon:       telefon,
            MaEmail:         e.Email,
            MaLand:          e.Country,
            MaZivilstand:    e.MaritalStatus,
            MaZivilstandSeit: e.MaritalStatusSince,
            ReferenzVertreterName: refName,
            ReferenzVertreterFunktion: refFunktion);

        try
        {
            var bytes = _pdf.GenerateBestaetigung(data);
            return File(bytes, "application/pdf", $"{e.EmployeeNumber}-Kuendigungsbestaetigung.pdf");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "PDF_FEHLER", message = ex.GetBaseException().Message });
        }
    }

    public class AufhebungPdfDto
    {
        /// <summary>Beginn des Arbeitsverhältnisses (Vertragsbeginn / Eintritt).</summary>
        public DateOnly ArbeitsverhaeltnisVon { get; set; }
        /// <summary>Auflösung per (= letzter Arbeitstag).</summary>
        public DateOnly AufhebungPer { get; set; }
        /// <summary>Letzter Lohn bis spätestens am …</summary>
        public DateOnly LetzterLohnBis { get; set; }
        public DateOnly? Datum { get; set; }
        public string?   Ort { get; set; }
        public bool      Eingeschrieben { get; set; }
    }

    /// <summary>
    /// Aufhebungsvereinbarung (Walter 28.07.2026) — einvernehmliche Auflösung.
    /// Pflicht: Arbeitsverhältnis von · Auflösung per · letzter Lohn bis.
    /// PDF: Seite 1 Brief (AG+AN-Unterschrift) · 2 Referenzangaben · 3 Swica · 4–5 PK.
    /// </summary>
    [HttpPost("{empId:int}/aufhebung-pdf")]
    public async Task<IActionResult> GetAufhebungPdf(int empId, [FromBody] AufhebungPdfDto dto)
    {
        var guard = await GuardBranchAsync(empId);
        if (guard != null) return guard;
        var ctx = await LoadContextAsync(empId);
        if (ctx is null) return NotFound(new { error = "EMP_NOT_FOUND" });
        var (e, emp, cp) = ctx.Value;

        if (dto.ArbeitsverhaeltnisVon == default)
            return BadRequest(new { error = "AV_VON_FEHLT", message = "Bitte den Beginn des Arbeitsverhältnisses angeben." });
        if (dto.AufhebungPer == default)
            return BadRequest(new { error = "AUFHEBUNG_PER_FEHLT", message = "Bitte das «Auflösung per»-Datum angeben." });
        if (dto.LetzterLohnBis == default)
            return BadRequest(new { error = "LOHN_BIS_FEHLT", message = "Bitte das Datum «letzter Lohn bis spätestens» angeben." });

        var datum = dto.Datum ?? DateOnly.FromDateTime(DateTime.Today);
        var ort   = string.IsNullOrWhiteSpace(dto.Ort) ? (cp?.City ?? "") : dto.Ort!.Trim();
        var (_, signerName, signerFunktion) = await GetSignerAsync(cp?.Id);
        var (refName, refFunktion) = await GetDefaultSignatoryAsync(cp?.Id);

        DateOnly? geburtsdatum = e.DateOfBirth.HasValue
            ? DateOnly.FromDateTime(e.DateOfBirth.Value)
            : null;
        var telefon = !string.IsNullOrWhiteSpace(e.PhoneMobile) ? e.PhoneMobile
            : e.Phone2;

        var data = new KuendigungPdfService.AufhebungData(
            FirmaName:       cp?.CompanyName,
            RestaurantName:  cp?.BranchName,
            FirmaStrasse:    Join(cp?.Street, cp?.HouseNumber),
            FirmaPlzOrt:     Join(cp?.ZipCode, cp?.City),
            MaName:          ($"{e.FirstName} {e.LastName}").Trim(),
            MaVorname:       (e.FirstName ?? "").Trim(),
            MaNachname:      (e.LastName ?? "").Trim(),
            MaStrasse:       e.Street,
            MaPlzOrt:        Join(e.ZipCode, e.City),
            DuAnrede:        DuAnrede(e),
            Ort:             ort,
            Datum:           datum,
            ArbeitsverhaeltnisVon: dto.ArbeitsverhaeltnisVon,
            AufhebungPer:    dto.AufhebungPer,
            LetzterLohnBis:  dto.LetzterLohnBis,
            UnterzeichnerName: signerName,
            UnterzeichnerFunktion: signerFunktion,
            ArbeitnehmerRolle: ArbeitnehmerRolle(e),
            Eingeschrieben:  dto.Eingeschrieben,
            MaAhvNummer:     e.SocialSecurityNumber,
            MaGeburtsdatum:  geburtsdatum,
            MaTelefon:       telefon,
            MaEmail:         e.Email,
            MaLand:          e.Country,
            MaZivilstand:    e.MaritalStatus,
            MaZivilstandSeit: e.MaritalStatusSince,
            ReferenzVertreterName: refName,
            ReferenzVertreterFunktion: refFunktion);

        try
        {
            var bytes = _pdf.GenerateAufhebung(data);
            return File(bytes, "application/pdf", $"{e.EmployeeNumber}-Aufhebungsvereinbarung.pdf");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "PDF_FEHLER", message = ex.GetBaseException().Message });
        }
    }

    /// <summary>
    /// Hebt die am MA erfasste Kuendigung auf (Walter-Vorgabe 16.07.2026):
    /// loescht «gekuendigt am» + «Kuendigung per» — die ToDo «Vertragsende
    /// wegen Kuendigung» verschwindet damit. Wird vom Frontend NACH der
    /// Erstellung des Rueckzugs-Briefs auf Nachfrage aufgerufen.
    /// </summary>
    [HttpPost("{empId:int}/kuendigung-aufheben")]
    public async Task<IActionResult> KuendigungAufheben(int empId)
    {
        var guard = await GuardBranchAsync(empId);
        if (guard != null) return guard;
        var tracked = await _db.Employees.FirstOrDefaultAsync(x => x.Id == empId);
        if (tracked == null) return NotFound(new { error = "EMP_NOT_FOUND" });
        tracked.KuendigungAusgesprochenAm = null;
        tracked.KuendigungPer             = null;
        tracked.KuendigungDurch           = null;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ── Helfer ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Filial-Zugriffs-Check (Walter 22.07.2026, Review-Fix): admin sieht
    /// alles; reiner superuser sieht alles; buchhaltung (Doppel-Claim!) und
    /// user (GF) sind auf ihre user_branch_access-Filialen beschraenkt —
    /// Filiale des MA = juengster Vertrag (analog LoadContextAsync). MA ohne
    /// Filial-Zuordnung ist fuer beschraenkte Rollen tabu.
    /// </summary>
    private async Task<IActionResult?> GuardBranchAsync(int empId)
    {
        if (User.IsInRole("admin")) return null;
        // buchhaltung ZUERST pruefen — hat via Doppel-Claim auch superuser
        // (CLAUDE.md: Filial-Beschraenkung trotz superuser-Claim).
        var restricted = User.IsInRole("buchhaltung") || !User.IsInRole("superuser");
        if (!restricted) return null;

        var cpId = await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == empId)
            .OrderByDescending(em => em.IsActive)
            .ThenByDescending(em => em.ContractStartDate)
            .Select(em => em.CompanyProfileId)
            .FirstOrDefaultAsync();
        if (cpId == null)
            return StatusCode(403, new { error = "BRANCH_REQUIRED",
                message = "Dieser Mitarbeiter hat keine Filial-Zuordnung — Zugriff nur für Admin/HR." });

        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(idStr, out var uid))
            return StatusCode(403, new { error = "NO_USER" });
        var ok = await _db.UserBranchAccesses
            .AnyAsync(a => a.UserId == uid && a.CompanyProfileId == cpId.Value);
        if (!ok)
            return StatusCode(403, new { error = "BRANCH_FORBIDDEN",
                message = "Kein Zugriff auf die Filiale dieses Mitarbeiters." });
        return null;
    }

    private async Task<(Employee e, Employment? emp, CompanyProfile? cp)?> LoadContextAsync(int empId)
    {
        var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == empId);
        if (e == null) return null;

        var emp = await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == empId)
            .OrderByDescending(em => em.IsActive)
            .ThenByDescending(em => em.ContractStartDate)
            .FirstOrDefaultAsync();

        CompanyProfile? cp = null;
        if (emp?.CompanyProfileId != null)
            cp = await _db.CompanyProfiles.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == emp.CompanyProfileId.Value);

        return (e, emp, cp);
    }

    /// <summary>
    /// Kündigungsfrist. <paramref name="grundType"/>:
    /// «probezeit» → immer Tagesfrist (Filial-Einstellung, Default 3 Kalendertage —
    /// wie Arbeitsvertrag; Walter 21.07.2026); «fristlos» → letzter Tag = Kündigungsdatum;
    /// sonst datumsbasiert (Probezeit-Ende bzw. Monatsfrist OR Art. 335c).
    /// </summary>
    private NoticeInfo ComputeNotice(
        Employee e, Employment? emp, CompanyProfile? cp, DateOnly kdat, string? grundType = null)
    {
        DateOnly? entry = e.EntryDate.HasValue ? DateOnly.FromDateTime(e.EntryDate.Value) : null;

        // Probezeitende: explizit, sonst Eintritt + Probemonate.
        DateOnly? probeEnde = null;
        if (emp?.ProbationEndDate != null)
            probeEnde = DateOnly.FromDateTime(emp.ProbationEndDate.Value);
        else if (entry.HasValue && (emp?.ProbationPeriodMonths ?? 0) > 0)
            probeEnde = entry.Value.AddMonths(emp!.ProbationPeriodMonths!.Value);

        int dienstjahr = entry.HasValue ? ComputeDienstjahr(entry.Value, kdat) : 1;
        var gt = (grundType ?? "").Trim().ToLowerInvariant();

        // Fristlose Kündigung: sofort — letzter Arbeitstag = Kündigungsdatum.
        if (gt == "fristlos")
        {
            return new NoticeInfo(false, dienstjahr, null, 0,
                "fristlos (sofort)", kdat,
                "Regel: fristlose Kündigung — kein Fristlauf, letzter Arbeitstag = Kündigungsdatum.");
        }

        // Probezeit-Kündigung: immer die hinterlegte Tagesfrist (auch wenn
        // das Probezeit-Ende-Datum schon vorbei ist — die Auswahl im UI gilt).
        // Default 3 Kalendertage = Arbeitsvertrags-Text (ContractPdfService).
        bool forceProbezeit = gt == "probezeit";
        bool inProbezeitByDate = probeEnde.HasValue && kdat <= probeEnde.Value;
        if (forceProbezeit || inProbezeitByDate)
        {
            int days = cp?.NoticePeriodDuringProbationDays ?? 3;
            string probeRule = forceProbezeit && !inProbezeitByDate
                ? $"Regel: Kündigung in der Probezeit — Frist {days} Kalendertage "
                  + (cp?.NoticePeriodDuringProbationDays != null
                      ? "gemäss Arbeitsvertrag/Filial-Einstellung."
                      : "gemäss Arbeitsvertrag (Standard 3 Kalendertage).")
                : $"Regel: während der Probezeit"
                  + (probeEnde.HasValue ? $" (bis {probeEnde:dd.MM.yyyy})" : "")
                  + $" gilt eine Frist von {days} Kalendertagen"
                  + (cp?.NoticePeriodDuringProbationDays != null
                      ? " gemäss Arbeitsvertrag/Filial-Einstellung (OR Art. 335b lässt Verkürzung zu)."
                      : " gemäss Arbeitsvertrag (Standard 3 Kalendertage; OR Art. 335b).");
            return new NoticeInfo(true, dienstjahr, null, days,
                $"{days} Kalendertagen", kdat.AddDays(days), probeRule);
        }

        // Nach der Probezeit: Monatsfrist auf Ende eines Monats.
        // Walter 25.07.2026 — L-GAV Gastgewerbe (Default, nicht OR-335c-Staffel):
        //   1.–5. Dienstjahr → 1 Monat auf Monatsende
        //   ab 6. Dienstjahr → 2 Monate auf Monatsende
        // Filial-Override: notice_period_after_probation_months (1.–5.),
        // notice_period_from_tenth_year_months (ab 6.; Feldname historisch).
        int months;
        if (dienstjahr >= 6)
            months = cp?.NoticePeriodFromTenthYearMonths ?? 2;
        else
            months = cp?.NoticePeriodAfterProbationMonths ?? 1;
        var letzter = new DateOnly(kdat.Year, kdat.Month, 1).AddMonths(months + 1).AddDays(-1);
        string txt = $"{months} Monat{(months == 1 ? "" : "en")} auf Ende eines Monats";

        // Regel-Herkunft transparent machen (Walter 15.07.2026 / L-GAV 25.07.2026).
        string rule;
        if (dienstjahr >= 6 && cp?.NoticePeriodFromTenthYearMonths != null)
            rule = $"Regel: ab 6. Dienstjahr {months} Monate gemäss Arbeitsvertrag/Filial-Einstellung (L-GAV).";
        else if (dienstjahr >= 6)
            rule = "Regel: ab 6. Dienstjahr 2 Monate auf Monatsende (L-GAV Gastgewerbe).";
        else if (cp?.NoticePeriodAfterProbationMonths != null)
            rule = $"Regel: 1.–5. Dienstjahr {months} Monat{(months == 1 ? "" : "e")} gemäss Arbeitsvertrag/Filial-Einstellung (L-GAV).";
        else
            rule = "Regel: 1.–5. Dienstjahr 1 Monat auf Monatsende (L-GAV Gastgewerbe).";
        return new NoticeInfo(false, dienstjahr, months, null, txt, letzter, rule);
    }

    private static int ComputeDienstjahr(DateOnly entry, DateOnly at)
    {
        if (at < entry) return 1;
        int monate = (at.Year - entry.Year) * 12 + (at.Month - entry.Month);
        if (at.Day < entry.Day) monate--;
        if (monate < 0) monate = 0;
        return monate / 12 + 1;
    }

    private string Briefanrede(Employee e)
    {
        if (!string.IsNullOrWhiteSpace(e.LetterSalutation)) return e.LetterSalutation!.Trim();
        var anrede = !string.IsNullOrWhiteSpace(e.Salutation) ? e.Salutation!.Trim()
            : (e.Gender == "female" ? "Frau" : e.Gender == "male" ? "Herr" : "");
        if (string.Equals(anrede, "Divers", StringComparison.OrdinalIgnoreCase)
            || string.Equals(anrede, "Diverse", StringComparison.OrdinalIgnoreCase))
            anrede = "";
        var ln = (e.LastName ?? "").Trim();
        if (anrede == "Frau") return $"Sehr geehrte Frau {ln}".Trim();
        if (anrede == "Herr") return $"Sehr geehrter Herr {ln}".Trim();
        return "Sehr geehrte Damen und Herren";
    }

    /// <summary>
    /// Öffentliche URL des eigenen Austritts-Fragebogens (Walter 26.07.2026) —
    /// aus smtp_setting.site_url, Fallback onecrew.ch/kuendigung/.
    /// Mit Filial-Code im Query (?f=075), damit die Antwort anonym der Filiale
    /// zugeordnet werden kann — ohne den MA zu identifizieren.
    /// </summary>
    private async Task<string> ResolveExitSurveyUrlAsync(CompanyProfile? cp)
    {
        var siteRow = await _db.SmtpSettings.AsNoTracking().FirstOrDefaultAsync();
        var baseUrl = (siteRow != null && !string.IsNullOrWhiteSpace(siteRow.SiteUrl))
            ? siteRow.SiteUrl.Trim()
            : "https://onecrew.ch/";
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        var url = baseUrl + "kuendigung/";
        var code = (cp?.RestaurantCode ?? "").Trim();
        if (code.Length > 0)
            url += "?f=" + Uri.EscapeDataString(code);
        return url;
    }

    /// <summary>
    /// Du-Anrede für die Kündigungsbestätigung (Walter-Vorlage): «Liebe Vorname»
    /// / «Lieber Vorname». Nutzt LetterSalutation wenn sie schon Du-Form ist.
    /// </summary>
    private static string DuAnrede(Employee e)
    {
        var ls = (e.LetterSalutation ?? "").Trim();
        if (ls.StartsWith("Liebe ", StringComparison.OrdinalIgnoreCase)
            || ls.StartsWith("Lieber ", StringComparison.OrdinalIgnoreCase)
            || ls.StartsWith("Hallo ", StringComparison.OrdinalIgnoreCase))
            return ls;

        var fn = (e.FirstName ?? "").Trim();
        if (fn.Length == 0) return "Hallo";

        var g = (e.Gender ?? "").Trim().ToLowerInvariant();
        if (g is "female" or "w" or "f") return $"Liebe {fn}";
        if (g is "male" or "m") return $"Lieber {fn}";

        var anrede = (e.Salutation ?? "").Trim();
        if (string.Equals(anrede, "Frau", StringComparison.OrdinalIgnoreCase)) return $"Liebe {fn}";
        if (string.Equals(anrede, "Herr", StringComparison.OrdinalIgnoreCase)) return $"Lieber {fn}";
        return $"Hallo {fn}";
    }

    /// <summary>«Arbeitnehmer» / «Arbeitnehmerin» für die Unterschrifts-Spalte der Aufhebung.</summary>
    private static string ArbeitnehmerRolle(Employee e)
    {
        var g = (e.Gender ?? "").Trim().ToLowerInvariant();
        if (g is "female" or "w" or "f") return "Arbeitnehmerin";
        if (g is "male" or "m") return "Arbeitnehmer";
        var anrede = (e.Salutation ?? "").Trim();
        if (string.Equals(anrede, "Frau", StringComparison.OrdinalIgnoreCase)) return "Arbeitnehmerin";
        return "Arbeitnehmer";
    }

    private static string? Join(string? a, string? b)
    {
        var s = string.Join(" ", new[] { a, b }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private async Task<(byte[]? png, string? name, string? funktion)> GetSignerAsync(int? companyProfileId = null)
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(idStr, out var uid))
        {
            var u = await _db.AppUsers.AsNoTracking()
                .Where(x => x.Id == uid)
                .Select(x => new { x.SignaturePng, x.FirstName, x.LastName, x.Username })
                .FirstOrDefaultAsync();
            if (u != null)
            {
                // Funktion aus dem Filial-Zugang (user_branch_access.FunctionTitle,
                // z.B. «HR-Verantwortliche») — Walter 16.07.2026: unter dem Namen
                // auf dem Schreiben. Geschlechtsform steuert Walter ueber den
                // Feld-Text pro Benutzer (Verantwortliche/Verantwortlicher).
                string? funktion = null;
                if (companyProfileId.HasValue)
                    funktion = await _db.UserBranchAccesses.AsNoTracking()
                        .Where(a => a.UserId == uid && a.CompanyProfileId == companyProfileId.Value
                                 && a.FunctionTitle != null && a.FunctionTitle != "")
                        .Select(a => a.FunctionTitle)
                        .FirstOrDefaultAsync();
                var full = $"{u.FirstName} {u.LastName}".Trim();
                return (u.SignaturePng, string.IsNullOrWhiteSpace(full) ? u.Username : full, funktion);
            }
        }
        return (null, null, null);
    }

    /// <summary>
    /// Standard-Unterzeichner der Filiale — gleiche Quelle wie Arbeitsvertrag
    /// (<c>user_branch_access.IsDefault</c>). Fallback: Rolle GESCHAEFTSFUEHRER.
    /// Für Referenzangaben auf der Kündigungsbestätigung (Walter 27.07.2026).
    /// </summary>
    private async Task<(string? name, string? funktion)> GetDefaultSignatoryAsync(int? companyProfileId)
    {
        if (!companyProfileId.HasValue) return (null, null);

        var signatory = await _db.UserBranchAccesses.AsNoTracking()
            .Include(a => a.User)
            .Where(a => a.CompanyProfileId == companyProfileId.Value && a.IsDefault)
            .FirstOrDefaultAsync();
        if (signatory?.User == null)
        {
            signatory = await _db.UserBranchAccesses.AsNoTracking()
                .Include(a => a.User)
                .Where(a => a.CompanyProfileId == companyProfileId.Value
                         && a.Role == "GESCHAEFTSFUEHRER")
                .OrderBy(a => a.Id)
                .FirstOrDefaultAsync();
        }
        if (signatory?.User == null) return (null, null);

        var full = $"{signatory.User.FirstName} {signatory.User.LastName}".Trim();
        var name = string.IsNullOrWhiteSpace(full) ? signatory.User.Username : full;
        var funktion = !string.IsNullOrWhiteSpace(signatory.FunctionTitle)
            ? signatory.FunctionTitle
            : (signatory.Role == "GESCHAEFTSFUEHRER" ? "Geschäftsführer/in" : null);
        return (name, funktion);
    }
}

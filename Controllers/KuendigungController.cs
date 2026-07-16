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
/// HR-Bereich → admin/superuser.
/// </summary>
[Authorize(Roles = "admin,superuser")]
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
        /// <summary>true = Versand per Einschreiben («EINSCHREIBEN» ueber der Adresse).</summary>
        public bool      Eingeschrieben { get; set; }
    }

    [HttpGet("{empId:int}/info")]
    public async Task<IActionResult> GetInfo(int empId, [FromQuery] DateOnly? datum = null)
    {
        var ctx = await LoadContextAsync(empId);
        if (ctx is null) return NotFound(new { error = "EMP_NOT_FOUND" });
        var (e, emp, cp) = ctx.Value;

        var kdat = datum ?? DateOnly.FromDateTime(DateTime.Today);
        var notice = ComputeNotice(e, emp, cp, kdat);
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
            }
        });
    }

    [HttpPost("{empId:int}/pdf")]
    public async Task<IActionResult> GetPdf(int empId, [FromBody] KuendigungPdfDto dto)
    {
        var ctx = await LoadContextAsync(empId);
        if (ctx is null) return NotFound(new { error = "EMP_NOT_FOUND" });
        var (e, emp, cp) = ctx.Value;

        var kdat   = dto.KuendigungsDatum ?? DateOnly.FromDateTime(DateTime.Today);
        var notice = ComputeNotice(e, emp, cp, kdat);
        // Letzter Arbeitstag: Override (falls HR angepasst) sonst berechnet.
        var letzter = dto.LetzterArbeitstag ?? notice.LetzterArbeitstag;
        var ort     = string.IsNullOrWhiteSpace(dto.Ort) ? (cp?.City ?? "") : dto.Ort!.Trim();

        // Unterschrift + Name des EINGELOGGTEN Users (nie eine andere Person).
        var (sigPng, signerName) = await GetSignerAsync();

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
            Eingeschrieben: dto.Eingeschrieben);

        var bytes = _pdf.Generate(data, sigPng);

        // Kündigungs-Daten am MA persistieren (Walter-Vorgabe 16.07.2026):
        // «ausgesprochen am» + «per» werden beim Erstellen des Schreibens
        // gesetzt (Anzeige in der Anstellungs-Zeile; ToDo 2 Wochen vor Ablauf).
        // Das Austrittsdatum wird bewusst NICHT gesetzt — es kann früher liegen
        // und wird beim effektiven Vertragsende erfasst.
        var tracked = await _db.Employees.FirstOrDefaultAsync(x => x.Id == empId);
        if (tracked != null)
        {
            tracked.KuendigungAusgesprochenAm = kdat.ToDateTime(TimeOnly.MinValue);
            tracked.KuendigungPer             = letzter.ToDateTime(TimeOnly.MinValue);
            await _db.SaveChangesAsync();
        }

        return File(bytes, "application/pdf", $"{e.EmployeeNumber}-Kuendigung.pdf");
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
        var ctx = await LoadContextAsync(empId);
        if (ctx is null) return NotFound(new { error = "EMP_NOT_FOUND" });
        var (e, _, cp) = ctx.Value;

        if (dto.KuendigungVom == default)
            return BadRequest(new { error = "KUENDIGUNG_VOM_FEHLT", message = "Bitte das Datum der ausgesprochenen Kündigung angeben." });

        var datum = dto.Datum ?? DateOnly.FromDateTime(DateTime.Today);
        var ort   = string.IsNullOrWhiteSpace(dto.Ort) ? (cp?.City ?? "") : dto.Ort!.Trim();
        var (sigPng, signerName) = await GetSignerAsync();

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

    /// <summary>
    /// Hebt die am MA erfasste Kuendigung auf (Walter-Vorgabe 16.07.2026):
    /// loescht «gekuendigt am» + «Kuendigung per» — die ToDo «Vertragsende
    /// wegen Kuendigung» verschwindet damit. Wird vom Frontend NACH der
    /// Erstellung des Rueckzugs-Briefs auf Nachfrage aufgerufen.
    /// </summary>
    [HttpPost("{empId:int}/kuendigung-aufheben")]
    public async Task<IActionResult> KuendigungAufheben(int empId)
    {
        var tracked = await _db.Employees.FirstOrDefaultAsync(x => x.Id == empId);
        if (tracked == null) return NotFound(new { error = "EMP_NOT_FOUND" });
        tracked.KuendigungAusgesprochenAm = null;
        tracked.KuendigungPer             = null;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ── Helfer ──────────────────────────────────────────────────────────────

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

    private NoticeInfo ComputeNotice(Employee e, Employment? emp, CompanyProfile? cp, DateOnly kdat)
    {
        DateOnly? entry = e.EntryDate.HasValue ? DateOnly.FromDateTime(e.EntryDate.Value) : null;

        // Probezeitende: explizit, sonst Eintritt + Probemonate.
        DateOnly? probeEnde = null;
        if (emp?.ProbationEndDate != null)
            probeEnde = DateOnly.FromDateTime(emp.ProbationEndDate.Value);
        else if (entry.HasValue && (emp?.ProbationPeriodMonths ?? 0) > 0)
            probeEnde = entry.Value.AddMonths(emp!.ProbationPeriodMonths!.Value);

        int dienstjahr = entry.HasValue ? ComputeDienstjahr(entry.Value, kdat) : 1;

        if (probeEnde.HasValue && kdat <= probeEnde.Value)
        {
            int days = cp?.NoticePeriodDuringProbationDays ?? 7;   // OR Art. 335b
            string probeRule = $"Regel: waehrend der Probezeit (bis {probeEnde:dd.MM.yyyy}) "
                + $"gilt eine Frist von {days} Tagen"
                + (cp?.NoticePeriodDuringProbationDays != null
                    ? " gemaess Arbeitsvertrag/Filial-Einstellung (OR Art. 335b laesst Verkuerzung zu)."
                    : " (OR Art. 335b: 7 Tage).");
            return new NoticeInfo(true, dienstjahr, null, days,
                $"{days} Tagen", kdat.AddDays(days), probeRule);
        }

        // Nach der Probezeit: Monatsfrist auf Ende eines Monats (OR Art. 335c).
        int months = dienstjahr >= 10
            ? (cp?.NoticePeriodFromTenthYearMonths ?? 3)
            : (cp?.NoticePeriodAfterProbationMonths ?? (dienstjahr <= 1 ? 1 : 2));
        var letzter = new DateOnly(kdat.Year, kdat.Month, 1).AddMonths(months + 1).AddDays(-1);
        string txt = $"{months} Monat{(months == 1 ? "" : "en")} auf Ende eines Monats";

        // Regel-Herkunft transparent machen (Walter 15.07.2026): WARUM diese Frist?
        string rule;
        if (dienstjahr >= 10 && cp?.NoticePeriodFromTenthYearMonths != null)
            rule = $"Regel: ab 10. Dienstjahr {months} Monate gemaess Arbeitsvertrag/Filial-Einstellung (OR Art. 335c: 3 Monate).";
        else if (dienstjahr >= 10)
            rule = "Regel: ab 10. Dienstjahr 3 Monate (OR Art. 335c).";
        else if (cp?.NoticePeriodAfterProbationMonths != null)
            rule = $"Regel: nach der Probezeit {months} Monat{(months == 1 ? "" : "e")} gemaess Arbeitsvertrag/Filial-Einstellung "
                 + $"(gilt bis zum 9. Dienstjahr; L-GAV/OR Art. 335c).";
        else if (dienstjahr <= 1)
            rule = "Regel: im 1. Dienstjahr 1 Monat (OR Art. 335c).";
        else
            rule = "Regel: im 2.-9. Dienstjahr 2 Monate (OR Art. 335c).";
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

    private static string? Join(string? a, string? b)
    {
        var s = string.Join(" ", new[] { a, b }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private async Task<(byte[]? png, string? name)> GetSignerAsync()
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
                var full = $"{u.FirstName} {u.LastName}".Trim();
                return (u.SignaturePng, string.IsNullOrWhiteSpace(full) ? u.Username : full);
            }
        }
        return (null, null);
    }
}

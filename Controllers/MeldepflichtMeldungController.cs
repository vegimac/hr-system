using System.Globalization;
using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Services;
using HrSystem.Services.Meldepflicht;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Formular 2b an die kantonale Behörde (Walter 20.10.2026).
/// Geht nur auf Knopf. Kategorie MELDEPFLICHT: ohne Haken an die Test-Adresse.
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/meldepflicht-meldung")]
[Authorize]
public class MeldepflichtMeldungController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly VersandFreigabeService _freigabe;

    public MeldepflichtMeldungController(AppDbContext db, EmailService email, VersandFreigabeService freigabe)
    {
        _db = db;
        _email = email;
        _freigabe = freigabe;
    }

    public record SendenDto(string Art, string? Datum);

    [HttpGet]
    public async Task<IActionResult> Stand(int employeeId)
    {
        var log = await _db.MailLogs.AsNoTracking()
            .Where(m => m.EmployeeId == employeeId && m.Kategorie == VersandKategorien.Code(VersandKategorie.Meldepflicht))
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync();
        if (log == null) return Ok(new { gesendet = false });

        var adresse = (log.RedirectedTo ?? log.ToEmail ?? "").Trim().ToLowerInvariant();
        var rueck = string.IsNullOrEmpty(adresse) ? null : await _db.MailBounces.AsNoTracking()
            .Where(b => !b.Erledigt && b.Adresse.ToLower() == adresse && b.EmpfangenAm >= log.CreatedAt)
            .OrderByDescending(b => b.EmpfangenAm)
            .Select(b => new { b.Hart, b.Grund })
            .FirstOrDefaultAsync();

        return Ok(new
        {
            gesendet = log.Ok,
            am = log.CreatedAt,
            an = log.ToEmail,
            umgeleitet = log.RedirectedTo,
            fehler = log.Error,
            ruecklaeufer = rueck == null ? null : rueck.Grund,
            ruecklaeuferHart = rueck?.Hart ?? false
        });
    }

    [HttpPost]
    public async Task<IActionResult> Senden(int employeeId, [FromBody] SendenDto dto)
    {
        var beendigung = (dto.Art ?? "").Trim().ToLowerInvariant() is "beendigung" or "abmeldung" or "austritt";
        var aufnahme = (dto.Art ?? "").Trim().ToLowerInvariant() is "aufnahme" or "anmeldung";
        if (!beendigung && !aufnahme)
            return BadRequest(new { error = "Art muss Aufnahme oder Beendigung sein." });

        var ma = await _db.Employees.AsNoTracking()
            .Include(e => e.NationalityRef)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (ma == null) return NotFound();

        var heute = DateOnly.FromDateTime(DateTime.Now);
        var bewilligung = await _db.EmployeePermitHistories.AsNoTracking()
            .Where(h => h.EmployeeId == employeeId && h.ErwerbMeldepflichtig)
            .OrderByDescending(h => h.ValidFrom)
            .FirstOrDefaultAsync();
        if (bewilligung == null)
            return BadRequest(new { error = "Keine Bewilligung mit Meldepflicht." });

        DateOnly datum;
        if (!string.IsNullOrWhiteSpace(dto.Datum) && DateOnly.TryParse(dto.Datum, out var gewaehlt))
            datum = gewaehlt;
        else if (beendigung && ma.ExitDate.HasValue)
            datum = DateOnly.FromDateTime(ma.ExitDate.Value);
        else if (beendigung && ma.KuendigungPer.HasValue)
            datum = DateOnly.FromDateTime(ma.KuendigungPer.Value);
        else
            datum = heute;

        var stichtag = datum.ToDateTime(TimeOnly.MinValue);
        var vertrag = await _db.Employments.AsNoTracking()
            .Include(e => e.CompanyProfile)
            .Include(e => e.JobGroup)
            .Where(e => e.EmployeeId == employeeId
                     && e.ContractStartDate <= stichtag
                     && (e.ContractEndDate == null || e.ContractEndDate >= stichtag))
            .OrderByDescending(e => e.ContractStartDate)
            .FirstOrDefaultAsync()
            ?? await _db.Employments.AsNoTracking()
                .Include(e => e.CompanyProfile)
                .Include(e => e.JobGroup)
                .Where(e => e.EmployeeId == employeeId)
                .OrderByDescending(e => e.ContractStartDate)
                .FirstOrDefaultAsync();

        var filiale = vertrag?.CompanyProfile;
        var kanton = filiale?.KantonCode;
        var adressen = MigrationsamtAdressen.Fuer(kanton);
        if (adressen.Count == 0)
            return BadRequest(new { error = $"Keine Behördenadresse für den Arbeitsort-Kanton «{kanton ?? "—"}»." });

        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        var user = userId == null ? null : await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        var stundenlohn = vertrag?.EmploymentModel is "FLEX" or "MTP";
        var lohn = stundenlohn ? vertrag?.HourlyRate : vertrag?.MonthlySalary;
        var wochen = vertrag?.WeeklyHours ?? 0m;
        var stunden = (int)decimal.Truncate(wochen);
        var minuten = (int)decimal.Round((wochen - stunden) * 60m, 0);
        if (minuten == 60) { stunden++; minuten = 0; }

        var uidNr = (filiale?.UidNummer ?? filiale?.UidBfs ?? "").Replace("CHE", "", StringComparison.OrdinalIgnoreCase).Trim().TrimStart('-').Trim();
        var daten = new Formular2bDaten
        {
            Zemis = ma.ZemisNumber,
            Name = ma.LastName,
            Vorname = ma.FirstName,
            Geburtsdatum = ma.DateOfBirth?.ToString("dd.MM.yyyy"),
            Nationalitaet = ma.NationalityRef?.NameDe ?? ma.Nationality,
            Telefon = ma.PhoneMobile ?? ma.Phone2,
            Strasse = ma.Street,
            PlzOrt = Join(" ", ma.ZipCode, ma.City),
            Weiblich = Weiblich(ma.Gender),
            Arbeitgeber = string.IsNullOrWhiteSpace(filiale?.CompanyName) ? "Schaub Restaurants GmbH" : filiale!.CompanyName,
            ArbeitgeberStrasse = filiale?.Street,
            ArbeitgeberPlzOrt = Join(" ", filiale?.ZipCode, filiale?.City),
            Uid = uidNr,
            KontaktName = user?.DisplayName,
            KontaktTelefon = filiale?.Phone,
            KontaktEmail = filiale?.Email,
            Taetigkeit = await TaetigkeitAsync(vertrag, Weiblich(ma.Gender)),
            Arbeitsort = Join(" ", filiale?.City, filiale?.KantonCode == null ? null : $"({filiale.KantonCode})"),
            Kanton = kanton,
            Stundenlohn = stundenlohn,
            Dreizehnter = vertrag?.ThirteenthSalary != false,
            Lohn = lohn.HasValue ? lohn.Value.ToString("N2", CultureInfo.GetCultureInfo("de-CH")) : null,
            Pensum = vertrag?.EmploymentPercentage?.ToString("0.##", CultureInfo.InvariantCulture),
            Wochenstunden = vertrag?.WeeklyHours.HasValue == true ? stunden.ToString() : null,
            Wochenminuten = vertrag?.WeeklyHours.HasValue == true ? minuten.ToString() : null,
            Beendigung = beendigung,
            Datum = datum.ToString("dd.MM.yyyy"),
        };

        var vorlagePfad = Path.Combine(AppContext.BaseDirectory, "Assets", "Formulare", "formular-2b.pdf");
        if (!System.IO.File.Exists(vorlagePfad))
            return StatusCode(500, new { error = "Formularvorlage fehlt." });
        var pdf = Formular2bPdf.Fuellen(await System.IO.File.ReadAllBytesAsync(vorlagePfad), daten);

        await _freigabe.EnsureRowsAsync();
        var scharf = await _freigabe.IstScharfAsync(VersandKategorie.Meldepflicht, VersandFreigabeService.Kanal.Mail);
        var ziele = scharf ? adressen : adressen.Take(1).ToList();

        var artText = beendigung ? "Beendigung" : "Aufnahme";
        var betreff = $"Formular 2b {artText} der Erwerbstätigkeit — {ma.LastName} {ma.FirstName}";
        var text = $@"Sehr geehrte Damen und Herren

anbei das Formular 2b zur {artText} der Erwerbstätigkeit von {ma.FirstName} {ma.LastName}.
Arbeitsort: {filiale?.City} ({kanton}), Datum: {datum:dd.MM.yyyy}.

Freundliche Grüsse
{user?.DisplayName}
Schaub Restaurants GmbH";
        var html = EmailService.HtmlRahmen(
            $"Formular 2b · {artText}",
            $@"<p style=""font-size:15px;margin:0 0 14px"">Sehr geehrte Damen und Herren</p>
<p style=""font-size:14px;line-height:1.55;margin:0"">anbei das Formular 2b zur {artText} der Erwerbstätigkeit von <strong>{System.Net.WebUtility.HtmlEncode(ma.FirstName)} {System.Net.WebUtility.HtmlEncode(ma.LastName)}</strong>.<br>Arbeitsort: {System.Net.WebUtility.HtmlEncode(filiale?.City)} ({System.Net.WebUtility.HtmlEncode(kanton)}), Datum: {datum:dd.MM.yyyy}.</p>
<p style=""font-size:14px;line-height:1.55;margin:14px 0 0"">Freundliche Grüsse<br>{System.Net.WebUtility.HtmlEncode(user?.DisplayName)}<br>Schaub Restaurants GmbH</p>");

        var datei = $"Formular-2b-{artText}-{ma.LastName}.pdf";
        var ok = true;
        foreach (var an in ziele)
        {
            var dieser = await _email.SendWithAttachmentAsync(
                an, "Migrationsamt", betreff, html, text, pdf, datei,
                VersandKategorie.Meldepflicht, employeeId);
            ok = ok && dieser;
        }
        if (!ok)
            return StatusCode(502, new { error = "Versand fehlgeschlagen. Siehe Mail-Protokoll." });

        return Ok(new
        {
            ok = true,
            art = artText,
            datum = datum.ToString("dd.MM.yyyy"),
            kanton,
            an = string.Join(", ", ziele),
            umgeleitet = !scharf
        });
    }

    async Task<string> TaetigkeitAsync(Models.Employment? vertrag, bool? weiblich)
    {
        var code = string.IsNullOrWhiteSpace(vertrag?.JobTitle) ? vertrag?.JobGroupCode : vertrag!.JobTitle;
        string? anzeige = null;
        if (!string.IsNullOrWhiteSpace(code))
        {
            var key = code.Trim() + ".NAME";
            anzeige = await _db.AppTexts.AsNoTracking()
                .Where(t => t.Module == "JOB_GROUP" && t.TextKey == key && t.LanguageCode == "de" && t.IsActive)
                .Select(t => t.Content)
                .FirstOrDefaultAsync();
        }
        var text = Funktionsbezeichnung.Geschlecht(anzeige ?? code, weiblich);
        return string.IsNullOrWhiteSpace(text) ? "Mitarbeit Gastronomie" : text;
    }

    static string? Join(string trenner, params string?[] teile)
    {
        var s = string.Join(trenner, teile.Where(t => !string.IsNullOrWhiteSpace(t)));
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    static bool? Weiblich(string? g)
    {
        if (string.IsNullOrWhiteSpace(g)) return null;
        var s = g.Trim().ToLowerInvariant();
        if (s is "f" or "w" or "1" or "female" or "frau" or "weiblich") return true;
        if (s is "m" or "0" or "male" or "mann" or "herr" or "männlich") return false;
        return null;
    }
}

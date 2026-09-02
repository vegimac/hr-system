using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// OneCrew-Kommunikation in ZWEI strikt getrennten Wegen (Walter 30.06.2026):
///
///   1) Postfach (Zustellung „postfach")  → für administrative/sensible HR-Themen
///      (Lohn, Vertrag, Bewilligung, QST, Arztzeugnis, Dokumentenanfragen …).
///      Die Mitteilung wird als Text-Notiz ins MA-Postfach (<see cref="MailboxDocument"/>)
///      gelegt; SMS ist nur Push, der Link führt zum Login → Postfach.
///
///   2) Moments (Zustellung „moment")     → für ECHTE persönliche Momente
///      (Danke/Wertschätzung, Geburtstag/Jubiläum, freiwillige Anlässe, kurze
///      Ja/Nein). Öffnung über EINMALIGEN Token-Link OHNE Login/Passwort/
///      Datenschutzabfrage. NUR für MA mit aktivem Opt-in, KEINE sensiblen
///      HR-Daten, KEINE Dokumente.
///
/// Erstellen ist HR-geschützt; die öffentliche Moment-Seite läuft anonym über
/// den (gehashten) Token. Opt-in setzt der MA selbst im Postfach.
/// </summary>
[ApiController]
[Route("api/moments")]
public class MomentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EcallSmsService _sms;

    public MomentsController(AppDbContext db, EcallSmsService sms)
    {
        _db = db;
        _sms = sms;
    }

    private int? UserId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    /// <summary>
    /// SMS-Direktversand nach dem Erstellen (Walter 07.07.2026, Etappe 2) —
    /// best-effort NACH dem Commit (Moment/Postfach-Notiz existiert auch, wenn
    /// die SMS scheitert; dann Link manuell übergeben). Versand über
    /// EcallSmsService — ob scharf oder an die Test-Nummer, entscheidet der
    /// Haken der Kategorie in der Systemsteuerung (Walter 01.09.2026).
    /// Rückgabe-Objekt für die UI: smsSent/smsTo/redirectedTo/smsError.
    /// </summary>
    private async Task<(bool sent, string? to, string? redirectedTo, string? error)>
        TrySendMomentSmsAsync(Employee emp, string smsBody, VersandKategorie kategorie)
    {
        var phone = (emp.PhoneMobile ?? "").Trim();
        if (phone.Length == 0)
            return (false, null, null, "Keine Mobilnummer hinterlegt.");
        try
        {
            var res = await _sms.SendSmsAsync(phone, smsBody, kategorie, employeeId: emp.Id);
            if (!res.Ok) return (false, phone, null, res.Error);
            // Umleitung aus dem Ergebnis, nicht aus den Einstellungen.
            return (true, phone, res.RedirectedTo, null);
        }
        catch (Exception ex)
        {
            return (false, phone, null, ex.Message);
        }
    }

    // ── Token: Klartext im Link, nur der SHA-256-Hash in der DB ─────────────
    private static (string token, string hash) NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        var token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (token, HashToken(token));
    }

    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Briefanrede des MA (Walter-Vorgabe 01.07.2026): immer die in der
    /// MA-Verwaltung gepflegte Briefanrede. Fehlt sie, wird sie aus Geschlecht +
    /// Vorname gebildet („Liebe {Vorname}" / „Lieber {Vorname}"), sonst „Hallo {Vorname}".</summary>
    private static string AnredeFor(Employee e)
    {
        if (!string.IsNullOrWhiteSpace(e.LetterSalutation)) return e.LetterSalutation!.Trim();
        var fn = (e.FirstName ?? "").Trim();
        if (fn.Length == 0) return "Hallo";
        return (e.Gender ?? "").Trim().ToLowerInvariant() switch
        {
            "female" => $"Liebe {fn}",
            "male"   => $"Lieber {fn}",
            _        => $"Hallo {fn}",
        };
    }

    /// <summary>Vollendete Dienstjahre seit Eintritt (für {Years} bei Arbeitsjubiläum).
    /// NULL, wenn kein Eintrittsdatum vorhanden → {Years} bleibt unaufgelöst und der
    /// Moment kann nicht gesendet werden (Walter-Vorgabe 01.07.2026).</summary>
    private static string? YearsOfService(Employee e)
    {
        if (e.EntryDate == null) return null;
        var ed = e.EntryDate.Value.Date;
        var now = DateTime.Now.Date;
        if (ed > now) return null;
        var years = now.Year - ed.Year;
        if (now.Month < ed.Month || (now.Month == ed.Month && now.Day < ed.Day)) years--;
        return years.ToString();
    }

    /// <summary>Aktuelle Version des Zustimmungstextes (Walter-Vorgabe 30.06.2026).</summary>
    public const string ConsentTextVersion = "2026-06-30";

    // Moment-Typen + ihre Consent-Kategorie liegen jetzt datengetrieben in der
    // Tabelle moment_type (Walter-Vorgabe 01.07.2026). Hier bleibt nur die harte
    // Sperrliste als zusätzlicher Schutz.

    // Ausdrücklich gesperrte Typen — dürfen NIE als Moment erstellt werden.
    private static readonly HashSet<string> BlockedTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ChildBirthday", "FamilyEvent", "PartnerEvent", "MedicalDetails", "AccidentDetails",
            "DocumentRequest", "Payroll", "Tax", "Permit", "Contract", "HRAdmin",
        };

    private static bool CategoryAllowed(string category, EmployeeMomentConsent c) => category switch
    {
        "birthday"     => c.AllowBirthdayAndAnniversaryMoments,
        "appreciation" => c.AllowAppreciationMoments,
        "care"         => c.AllowCareMoments,
        _              => false,
    };

    // Einheitliche Admin-Fehlermeldung bei nicht freigegebenem Moment (Spec-Wortlaut).
    private const string NotConsentedMessage = "Für diesen Mitarbeitenden ist dieser Moment nicht freigegeben.";

    // ── Sensibel-Guard: Moments dürfen KEINE administrativen HR-Themen tragen ─
    private static readonly string[] SensitiveKeywords =
    {
        "lohn", "salär", "salaer", "gehalt", "lohnausweis", "lohnabrechnung", "lohnzettel",
        "vertrag", "arbeitsvertrag", "kündigung", "kuendigung", "bewilligung", "aufenthalt",
        "quellensteuer", "steuer", "ahv", "iban", "konto", "arztzeugnis", "krankheit",
        "krankschreibung", "unfall", "ktg", "uvg", "dokument", "zeugnis", "passwort"
    };

    private static string? FindSensitive(params string?[] parts)
    {
        foreach (var p in parts)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var lower = p.ToLowerInvariant();
            foreach (var kw in SensitiveKeywords)
                if (lower.Contains(kw)) return kw;
        }
        return null;
    }

    public record CreateMomentDto(int EmployeeId, string? Typ, string? Zustellung, string? Absender,
                                  string? DokumentName, string? Title, string? SmsText, string? FullText,
                                  string? Antwortart);

    /// <summary>Erstellen → je nach Zustellung Postfach-Notiz ODER Moment-Token-Link.</summary>
    [HttpPost]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> Create([FromBody] CreateMomentDto dto)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == dto.EmployeeId);
        if (emp == null) return BadRequest(new { error = "Mitarbeiter nicht gefunden." });
        if (string.IsNullOrWhiteSpace(dto.FullText)) return BadRequest(new { error = "Vollständige Mitteilung fehlt." });

        var zustellung = dto.Zustellung == "moment" || dto.Zustellung == "direkt" ? "moment" : "postfach";
        var absender = string.IsNullOrWhiteSpace(dto.Absender) ? null : dto.Absender.Trim();

        var briefanrede = AnredeFor(emp);
        var yearsStr = YearsOfService(emp); // null wenn nicht berechenbar → {Years} bleibt stehen
        string Resolve(string? s) => (s ?? "")
            .Replace("{Briefanrede}", briefanrede)
            .Replace("{Anrede}", briefanrede)                 // Alias (Rückwärtskompatibilität)
            .Replace("{Years}", yearsStr ?? "{Years}")        // unaufgelöst lassen, wenn nicht berechenbar
            .Replace("{SenderName}", absender ?? "")
            .Replace("{Absender}", absender ?? "")            // Alias
            .Replace("{Vorname}", emp.FirstName ?? "");

        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        // ───────────────────────────── Weg 1: Postfach ─────────────────────
        if (zustellung == "postfach")
        {
            try
            {
                var cpId = await _db.Employments.AsNoTracking()
                    .Where(e => e.EmployeeId == emp.Id && e.CompanyProfileId != null)
                    .OrderByDescending(e => e.ContractStartDate)
                    .Select(e => e.CompanyProfileId)
                    .FirstOrDefaultAsync();
                if (cpId == null)
                    cpId = await _db.CompanyProfiles.OrderBy(c => c.Id).Select(c => (int?)c.Id).FirstOrDefaultAsync();
                if (cpId == null)
                    return BadRequest(new { error = "Keine Filiale konfiguriert — Postfach-Mitteilung nicht möglich." });

                var titel = !string.IsNullOrWhiteSpace(dto.Title) ? dto.Title!.Trim()
                          : !string.IsNullOrWhiteSpace(dto.DokumentName) ? dto.DokumentName!.Trim()
                          : !string.IsNullOrWhiteSpace(absender) ? $"Mitteilung von {absender}"
                          : "Neue HR-Mitteilung";

                var mbox = new MailboxDocument
                {
                    CompanyProfileId = cpId.Value,
                    UploadedBy       = UserId(),
                    UploadedAt       = DateTime.Now,
                    OriginalFilename = titel,
                    // Reine Text-Mitteilung (keine Datei). storage_filename hat einen
                    // UNIQUE-Constraint → leerer String kollidiert ab der 2. Notiz.
                    // Daher ein eindeutiger Platzhalter (es liegt keine Datei auf Disk).
                    StorageFilename  = $"msg-{Guid.NewGuid():N}",
                    MimeType         = null,
                    FileSizeBytes    = null,
                    Bemerkung        = "Moment",
                    MessageBody      = Resolve(dto.FullText),
                    EmployeeId       = emp.Id,
                    NotifyUserId     = null,
                    TargetType       = "EMPLOYEE",
                };
                _db.MailboxDocuments.Add(mbox);
                await _db.SaveChangesAsync();

                var pushSms = string.IsNullOrWhiteSpace(dto.SmsText)
                    ? "OneCrew: In deinem persönlichen Postfach wartet eine neue HR-Nachricht."
                    : Resolve(dto.SmsText);

                var pfUrl = $"{baseUrl}/postfach.html";
                var (sent, smsTo, redirectedTo, smsError) =
                    await TrySendMomentSmsAsync(emp, $"{pushSms}\n{pfUrl}", VersandKategorie.Postfach);

                return Ok(new
                {
                    zustellung = "postfach",
                    url = pfUrl,
                    smsText = pushSms,
                    mailboxDocumentId = mbox.Id,
                    smsSent = sent,
                    smsTo,
                    redirectedTo,
                    smsError,
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "MOMENT_CREATE_FAILED", message = Flatten(ex) });
            }
        }

        // ───────────────────────────── Weg 2: Moment ───────────────────────
        // Pflicht-Prüfungen (Walter-Vorgabe 30.06.2026). Schlägt EINE fehl:
        // kein Token, kein Link, keine SMS — und genau diese Admin-Meldung.
        var type = (dto.Typ ?? "").Trim();

        // (3) Moment-Typ zugelassen? Gesperrte Typen sofort raus, sonst muss der
        // Typ als aktiver moment_type existieren; seine Consent-Kategorie steuert (2).
        if (BlockedTypes.Contains(type))
            return Conflict(new { error = "MOMENT_NOT_ALLOWED", message = NotConsentedMessage });
        var momentType = await _db.MomentTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Code == type && t.IsActive);
        if (momentType == null)
            return Conflict(new { error = "MOMENT_NOT_ALLOWED", message = NotConsentedMessage });
        var category = momentType.ConsentCategory;

        // (1) Freigabe aktiv?  (2) passende Unterkategorie erlaubt?
        var consent = await _db.EmployeeMomentConsents.AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmployeeId == emp.Id);
        if (consent == null || !consent.MomentsConsentEnabled || !CategoryAllowed(category, consent))
            return Conflict(new { error = "MOMENT_NOT_ALLOWED", message = NotConsentedMessage });

        // (4) Keine gesperrten Themen im Text (Lohn/Vertrag/Krankheit/…).
        var hit = FindSensitive(dto.Title, dto.FullText, dto.SmsText);
        if (hit != null)
            return Conflict(new { error = "MOMENT_NOT_ALLOWED", message = NotConsentedMessage });

        var antwortart = dto.Antwortart == "janein" ? "janein" : "lesen";
        var (token, tokenHash) = NewToken();

        var m = new MomentPage
        {
            EmployeeId  = emp.Id,
            SenderId    = UserId(),
            MomentType  = type,
            Title       = string.IsNullOrWhiteSpace(dto.Title) ? null : Resolve(dto.Title),
            MessageHtml = Resolve(dto.FullText),
            TokenHash   = tokenHash,
            ExpiresAt   = DateTime.Now.AddDays(30),
            Status      = "erstellt",
            CreatedAt   = DateTime.Now,
            SmsText     = string.IsNullOrWhiteSpace(dto.SmsText) ? null : Resolve(dto.SmsText),
            Antwortart  = antwortart,
        };

        // Pflicht-Platzhalter, die nicht befüllt werden konnten → nicht senden.
        var _check = (m.Title ?? "") + "\n" + (m.MessageHtml ?? "") + "\n" + (m.SmsText ?? "");
        if (_check.Contains("{Years}") || _check.Contains("{Briefanrede}"))
            return BadRequest(new { error = "MOMENT_PLACEHOLDER_UNRESOLVED", message = "Der Text enthält einen Platzhalter, der nicht automatisch befüllt werden konnte (z.B. {Years} ohne Eintrittsdatum). Bitte den Text anpassen — der Moment wurde nicht erstellt." });

        try
        {
            _db.MomentPages.Add(m);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "MOMENT_CREATE_FAILED", message = Flatten(ex) });
        }

        var momentUrl = $"{baseUrl}/moment.html?t={token}";
        var momentSms = string.IsNullOrWhiteSpace(m.SmsText)
            ? "OneCrew: Du hast eine persönliche Nachricht. Tippe auf den Link:"
            : m.SmsText!;
        var (mSent, mTo, mRedirect, mError) =
            await TrySendMomentSmsAsync(emp, $"{momentSms}\n{momentUrl}", VersandKategorie.Moment);

        return Ok(new
        {
            zustellung = "moment",
            url = momentUrl,
            momentId = m.Id,
            smsSent = mSent,
            smsTo = mTo,
            redirectedTo = mRedirect,
            smsError = mError,
        });
    }

    private static string Flatten(Exception ex)
    {
        var msg = ex.Message;
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
            msg += "  →  " + inner.Message;
        return msg;
    }

    /// <summary>Verlauf (für die Moments-Seite) — nur die Token-Link-Moments.</summary>
    [HttpGet]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> List([FromQuery] int take = 50)
    {
        var list = await _db.MomentPages.AsNoTracking()
            .OrderByDescending(m => m.Id)
            .Take(Math.Clamp(take, 1, 200))
            .Select(m => new
            {
                m.Id, m.MomentType, m.Title, m.Antwortart, m.Status,
                m.CreatedAt, m.ExpiresAt, m.OpenedAt, m.RespondedAt, m.ResponseValue,
                employee = m.Employee != null ? (m.Employee.FirstName + " " + m.Employee.LastName).Trim() : null
            })
            .ToListAsync();
        return Ok(list);
    }

    private static object ConsentDto(EmployeeMomentConsent? c) => new
    {
        momentsConsentEnabled      = c?.MomentsConsentEnabled ?? false,
        allowBirthdayAndAnniversary = c?.AllowBirthdayAndAnniversaryMoments ?? false,
        allowAppreciation          = c?.AllowAppreciationMoments ?? false,
        allowCare                  = c?.AllowCareMoments ?? false,
        consentTextVersion         = c?.ConsentTextVersion,
        grantedAt                  = c?.GrantedAt,
        revokedAt                  = c?.RevokedAt,
        currentTextVersion         = ConsentTextVersion,
    };

    /// <summary>Consent-Status eines MA (für die Moments-Compose-Maske, HR-seitig).</summary>
    [HttpGet("consent/{employeeId:int}")]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> ConsentStatus(int employeeId)
    {
        var c = await _db.EmployeeMomentConsents.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeId == employeeId);
        return Ok(ConsentDto(c));
    }

    // ── MA-Selbstbedienung: eigene Moments-Freigabe lesen/setzen (im Profil) ──
    private async Task<(int? empId, string? userName)> CurrentEmployeeAsync()
    {
        var uid = UserId();
        if (uid == null) return (null, null);
        var u = await _db.AppUsers.AsNoTracking()
            .Where(x => x.Id == uid.Value)
            .Select(x => new { x.EmployeeId, x.Username })
            .FirstOrDefaultAsync();
        return (u?.EmployeeId, u?.Username);
    }

    [HttpGet("my-consent")]
    [Authorize(Roles = "admin,superuser,user,employee,buchhaltung")]
    public async Task<IActionResult> MyConsent()
    {
        var (empId, _) = await CurrentEmployeeAsync();
        if (empId == null) return NotFound(new { error = "Kein Mitarbeiter-Profil verknüpft." });
        var c = await _db.EmployeeMomentConsents.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeId == empId.Value);
        return Ok(ConsentDto(c));
    }

    public record MyConsentDto(bool Enabled, bool Birthday, bool Appreciation, bool Care);

    [HttpPut("my-consent")]
    [Authorize(Roles = "admin,superuser,user,employee,buchhaltung")]
    public async Task<IActionResult> SetMyConsent([FromBody] MyConsentDto dto)
    {
        var (empId, userName) = await CurrentEmployeeAsync();
        if (empId == null) return NotFound(new { error = "Kein Mitarbeiter-Profil verknüpft." });

        var c = await _db.EmployeeMomentConsents.FirstOrDefaultAsync(x => x.EmployeeId == empId.Value);
        if (c == null)
        {
            c = new EmployeeMomentConsent { EmployeeId = empId.Value };
            _db.EmployeeMomentConsents.Add(c);
        }

        var now = DateTime.Now;
        if (dto.Enabled)
        {
            // Einschalten: Zustimmung mit Zeitstempel, Widerruf zurücksetzen.
            c.MomentsConsentEnabled = true;
            c.GrantedAt = now;
            c.RevokedAt = null;
            c.ConsentTextVersion = ConsentTextVersion;
        }
        else
        {
            // Ausschalten: Widerruf mit Zeitstempel.
            c.MomentsConsentEnabled = false;
            c.RevokedAt = now;
        }
        // Unterkategorien (greifen nur wenn Haupt-Freigabe aktiv).
        c.AllowBirthdayAndAnniversaryMoments = dto.Birthday;
        c.AllowAppreciationMoments           = dto.Appreciation;
        c.AllowCareMoments                   = dto.Care;
        c.LastChangedAt = now;
        c.LastChangedBy = userName;
        c.Source        = "EmployeeProfile";

        await _db.SaveChangesAsync();
        return Ok(ConsentDto(c));
    }

    // ── Öffentliche Moment-Seite (anonym, nur über den Token) ───────────────

    private async Task<MomentPage?> FindByTokenAsync(string token) =>
        await _db.MomentPages.Include(x => x.Employee).FirstOrDefaultAsync(x => x.TokenHash == HashToken(token));

    private static object PublicContent(MomentPage m) => new
    {
        vorname     = m.Employee?.FirstName ?? "",
        momentType  = m.MomentType,
        title       = m.Title,
        messageHtml = m.MessageHtml,
        antwortart  = m.Antwortart,
        status      = m.Status,
        responseValue = m.ResponseValue,
        respondedAt   = m.RespondedAt,
    };

    /// <summary>Moment laden + als geöffnet markieren. Kein Login, keine Verifikation.</summary>
    [AllowAnonymous]
    [HttpGet("public/{token}")]
    public async Task<IActionResult> GetPublic(string token)
    {
        var m = await FindByTokenAsync(token);
        if (m == null) return NotFound(new { error = "Dieser Moment wurde nicht gefunden." });

        if (m.ExpiresAt != null && m.ExpiresAt < DateTime.Now)
        {
            if (m.Status != "abgelaufen") { m.Status = "abgelaufen"; await _db.SaveChangesAsync(); }
            return StatusCode(410, new { error = "Dieser Moment-Link ist abgelaufen." });
        }

        if (m.OpenedAt == null)
        {
            m.OpenedAt = DateTime.Now;
            if (m.Status == "erstellt") m.Status = "geoeffnet";
            await _db.SaveChangesAsync();
        }
        return Ok(PublicContent(m));
    }

    public record RespondDto(string? Value);

    /// <summary>Kurze Ja/Nein-Antwort speichern (nur bei Antwortart „janein").</summary>
    [AllowAnonymous]
    [HttpPost("public/{token}/respond")]
    public async Task<IActionResult> Respond(string token, [FromBody] RespondDto dto)
    {
        var m = await FindByTokenAsync(token);
        if (m == null) return NotFound(new { error = "Moment nicht gefunden." });
        if (m.ExpiresAt != null && m.ExpiresAt < DateTime.Now)
            return StatusCode(410, new { error = "Dieser Moment-Link ist abgelaufen." });
        if (m.Antwortart != "janein")
            return BadRequest(new { error = "Für diesen Moment ist keine Antwort vorgesehen." });

        var v = (dto.Value ?? "").Trim().ToLowerInvariant();
        if (v != "ja" && v != "nein") return BadRequest(new { error = "Bitte Ja oder Nein wählen." });

        m.ResponseValue = v;
        m.RespondedAt = DateTime.Now;
        m.Status = "beantwortet";
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}

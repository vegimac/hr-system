using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Admin-Endpoint für die SMTP-Konfiguration (Singleton, smtp_setting).
///
/// Sichtbarkeit/Schreibrechte: nur admin (nicht superuser), weil das
/// SMTP-Passwort sensibel ist und Mail-Versand im Namen der Firma
/// passiert.
///
/// Endpoints:
///   GET  /api/admin/smtp                — aktuelle Konfig (Passwort gemaskt)
///   PUT  /api/admin/smtp                — Konfig speichern (Passwort
///                                          nur ändern wenn nicht-leer/nicht-maskiert)
///   POST /api/admin/smtp/test           — Test-Mail mit aktueller (gespeicherter) Konfig
///   POST /api/admin/smtp/test-with-config — Test-Mail mit DTO-Konfig (für
///                                          Validierung VOR dem Speichern)
/// </summary>
[Authorize(Roles = "admin")]
[ApiController]
[Route("api/admin/smtp")]
public class AdminSmtpController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SimpleAesService _aes;
    private readonly EmailService _emailSvc;
    private readonly ILogger<AdminSmtpController> _log;

    // Sentinel-Wert: Frontend schickt "***UNCHANGED***" zurück, wenn der
    // User das Passwort-Feld nicht angefasst hat. Damit unterscheiden wir
    // "leer lassen" (= Passwort entfernen) von "unverändert" (= bestehendes
    // Passwort beibehalten).
    public const string PasswordUnchangedSentinel = "***UNCHANGED***";

    public AdminSmtpController(AppDbContext db, SimpleAesService aes,
                               EmailService emailSvc, ILogger<AdminSmtpController> log)
    {
        _db = db;
        _aes = aes;
        _emailSvc = emailSvc;
        _log = log;
    }

    public class SmtpDto
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";   // beim GET = Sentinel; beim PUT = Klartext oder Sentinel
        public bool   HasPassword { get; set; }      // nur GET — zeigt im UI an, ob ein Passwort hinterlegt ist
        public string FromName { get; set; } = "Schaub HR";
        public string FromAddress { get; set; } = "";
        public string? TestRedirectTo { get; set; }
        public string SiteUrl { get; set; } = "https://onecrew.ch/";
        public bool   IsFromDb { get; set; }         // GET — false = noch kein DB-Row, Werte aus appsettings.json

        // ── Rückläufer-Postfach (Walter-Vorgabe 01.09.2026) ───────────────
        public string? BounceAddress { get; set; }
        public string? BounceImapHost { get; set; }
        public int     BounceImapPort { get; set; } = 993;
        public string? BounceImapUser { get; set; }
        public string  BounceImapPassword { get; set; } = "";  // wie oben: Sentinel = unverändert
        public bool    BounceHasPassword { get; set; }         // nur GET
        public bool    BounceAbrufAktiv { get; set; }
        public DateTime? BounceLetzterAbruf { get; set; }      // nur GET
    }

    public class TestRequestDto
    {
        public string To { get; set; } = "";          // Test-Empfänger
        public SmtpDto? Config { get; set; }          // optional: alternative Konfig zum Testen
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var row = await _db.SmtpSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == 1);
        if (row == null)
        {
            // Noch nichts in DB → effektive Konfig (Fallback aus appsettings.json) zeigen,
            // aber Passwort NIE rausgeben.
            var cfg = await _emailSvc.GetEffectiveConfigAsync(useCache: false);
            return Ok(new SmtpDto
            {
                Host = cfg.Host,
                Port = cfg.Port,
                Username = cfg.Username,
                Password = PasswordUnchangedSentinel,
                HasPassword = !string.IsNullOrEmpty(cfg.Password),
                FromName = cfg.FromName,
                FromAddress = cfg.FromAddress,
                TestRedirectTo = cfg.TestRedirectTo,
                SiteUrl = cfg.SiteUrl,
                IsFromDb = false,
                BounceImapPassword = PasswordUnchangedSentinel,
                BounceImapPort = 993
            });
        }

        return Ok(new SmtpDto
        {
            Host = row.Host,
            Port = row.Port,
            Username = row.Username,
            Password = PasswordUnchangedSentinel,
            HasPassword = !string.IsNullOrEmpty(row.PasswordEncrypted),
            FromName = row.FromName,
            FromAddress = row.FromAddress,
            TestRedirectTo = row.TestRedirectTo,
            SiteUrl = row.SiteUrl,
            IsFromDb = true,
            BounceAddress      = row.BounceAddress,
            BounceImapHost     = row.BounceImapHost,
            BounceImapPort     = row.BounceImapPort > 0 ? row.BounceImapPort : 993,
            BounceImapUser     = row.BounceImapUser,
            BounceImapPassword = PasswordUnchangedSentinel,
            BounceHasPassword  = !string.IsNullOrEmpty(row.BounceImapPasswordEncrypted),
            BounceAbrufAktiv   = row.BounceAbrufAktiv,
            BounceLetzterAbruf = row.BounceLetzterAbruf
        });
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] SmtpDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Host))
            return BadRequest(new { error = "Host darf nicht leer sein." });
        if (string.IsNullOrWhiteSpace(dto.FromAddress))
            return BadRequest(new { error = "Absender-Adresse darf nicht leer sein." });
        if (dto.Port <= 0 || dto.Port > 65535)
            return BadRequest(new { error = "Port ungültig." });

        // Test-Adresse ist PFLICHT (Walter-Vorgabe 01.09.2026): sie ist das
        // Umleitungsziel für jede Kategorie ohne Haken. Ohne sie könnte eine
        // nicht freigegebene Mail nirgends hin — und darf erst recht nicht
        // scharf rausgehen.
        if (string.IsNullOrWhiteSpace(dto.TestRedirectTo))
            return BadRequest(new { error = "TESTADRESSE_FEHLT",
                message = "Die Test-Adresse ist Pflicht — sie ist das Umleitungsziel für alle Verteiler ohne Haken." });

        var row = await _db.SmtpSettings.FirstOrDefaultAsync(r => r.Id == 1);
        var isNew = row == null;
        if (isNew)
        {
            row = new SmtpSetting { Id = 1 };
            _db.SmtpSettings.Add(row);
        }

        row!.Host          = dto.Host.Trim();
        row.Port           = dto.Port;
        row.Username       = (dto.Username ?? "").Trim();
        row.FromName       = string.IsNullOrWhiteSpace(dto.FromName) ? "Schaub HR" : dto.FromName.Trim();
        row.FromAddress    = dto.FromAddress.Trim();
        row.TestRedirectTo = dto.TestRedirectTo!.Trim();
        row.SiteUrl        = string.IsNullOrWhiteSpace(dto.SiteUrl) ? "https://onecrew.ch/" : dto.SiteUrl.Trim();

        // ── Rückläufer-Postfach ───────────────────────────────────────────
        // Leere Felder sind erlaubt: das ist der Zustand «noch nicht
        // eingerichtet», und dann bleibt schlicht alles wie vorher.
        row.BounceAddress   = Leer(dto.BounceAddress);
        row.BounceImapHost  = Leer(dto.BounceImapHost);
        row.BounceImapPort  = dto.BounceImapPort > 0 && dto.BounceImapPort <= 65535 ? dto.BounceImapPort : 993;
        row.BounceImapUser  = Leer(dto.BounceImapUser);
        if (dto.BounceImapPassword != PasswordUnchangedSentinel)
            row.BounceImapPasswordEncrypted =
                string.IsNullOrEmpty(dto.BounceImapPassword) ? "" : _aes.Encrypt(dto.BounceImapPassword);

        // Der Haken lässt sich nur setzen, wenn auch wirklich ein Postfach
        // dahintersteht — sonst stünde in der Maske «aktiv», während der
        // Dienst in Wahrheit jede Stunde nichts tut.
        var bereit = !string.IsNullOrWhiteSpace(row.BounceImapHost)
                  && !string.IsNullOrWhiteSpace(row.BounceImapUser)
                  && !string.IsNullOrEmpty(row.BounceImapPasswordEncrypted);
        row.BounceAbrufAktiv = dto.BounceAbrufAktiv && bereit;

        row.UpdatedAt      = DateTime.UtcNow;
        row.UpdatedByUserId = GetCurrentUserId();

        // Passwort-Logik:
        //   Sentinel  → unverändert lassen
        //   leer ""   → Passwort löschen
        //   sonst     → neu setzen (verschlüsseln)
        if (dto.Password != PasswordUnchangedSentinel)
        {
            row.PasswordEncrypted = string.IsNullOrEmpty(dto.Password) ? "" : _aes.Encrypt(dto.Password);
        }

        await _db.SaveChangesAsync();
        EmailService.InvalidateCache();
        _log.LogInformation("[AdminSmtp] Konfig gespeichert von User {UserId} (host={Host} from={From} testRedirect={Redirect})",
                            row.UpdatedByUserId, row.Host, row.FromAddress, row.TestRedirectTo ?? "<aus>");

        return Ok(new { ok = true });
    }

    /// <summary>
    /// Test-Mail mit der GESPEICHERTEN Konfig.
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTest([FromBody] TestRequestDto req)
    {
        if (string.IsNullOrWhiteSpace(req.To))
            return BadRequest(new { ok = false, error = "Empfänger-Adresse fehlt." });

        try
        {
            var cfg = await _emailSvc.GetEffectiveConfigAsync(useCache: false);
            await SendTestMailWithConfig(cfg, req.To);
            return Ok(BuildOkResult(cfg, req.To));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[AdminSmtp] Test-Mail fehlgeschlagen an {To}", req.To);
            return Ok(new { ok = false, error = ex.Message, errorType = ex.GetType().Name });
        }
    }

    /// <summary>
    /// Test-Mail mit einer NICHT-GESPEICHERTEN Konfig (DTO im Body).
    /// Damit kann der User VOR dem Speichern testen, ob die Daten stimmen.
    /// Wenn das Passwort = Sentinel ist, holen wir das gespeicherte Passwort
    /// aus der DB (für den Fall: User ändert nur Host und will testen).
    /// </summary>
    [HttpPost("test-with-config")]
    public async Task<IActionResult> SendTestWithConfig([FromBody] TestRequestDto req)
    {
        if (string.IsNullOrWhiteSpace(req.To))
            return BadRequest(new { ok = false, error = "Empfänger-Adresse fehlt." });
        if (req.Config == null)
            return BadRequest(new { ok = false, error = "Konfig fehlt." });

        var d = req.Config;
        if (string.IsNullOrWhiteSpace(d.Host))
            return Ok(new { ok = false, error = "Host darf nicht leer sein." });
        if (string.IsNullOrWhiteSpace(d.FromAddress))
            return Ok(new { ok = false, error = "Absender-Adresse darf nicht leer sein." });

        // Passwort: Sentinel → aus DB holen
        var password = d.Password ?? "";
        if (password == PasswordUnchangedSentinel)
        {
            var row = await _db.SmtpSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == 1);
            password = row != null ? _aes.Decrypt(row.PasswordEncrypted) : "";
        }

        var cfg = new EmailService.EffectiveSmtpConfig(
            d.Host.Trim(),
            d.Port > 0 ? d.Port : 587,
            (d.Username ?? "").Trim(),
            password,
            string.IsNullOrWhiteSpace(d.FromName) ? "Schaub HR" : d.FromName.Trim(),
            d.FromAddress.Trim(),
            string.IsNullOrWhiteSpace(d.TestRedirectTo) ? null : d.TestRedirectTo.Trim(),
            string.IsNullOrWhiteSpace(d.SiteUrl) ? "https://onecrew.ch/" : d.SiteUrl.Trim(),
            // Rücksendeadresse mitnehmen (Walter 01.09.2026): Sonst prüft die
            // Test-Mail genau das NICHT, was am ehesten schiefgeht — nämlich
            // ob der Mailserver einen abweichenden Envelope-Absender
            // überhaupt annimmt.
            string.IsNullOrWhiteSpace(d.BounceAddress) ? null : d.BounceAddress.Trim());

        try
        {
            await SendTestMailWithConfig(cfg, req.To);
            return Ok(BuildOkResult(cfg, req.To));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[AdminSmtp] Test-Mail (mit Config) fehlgeschlagen an {To}", req.To);
            return Ok(new { ok = false, error = ex.Message, errorType = ex.GetType().Name });
        }
    }

    private async Task SendTestMailWithConfig(EmailService.EffectiveSmtpConfig cfg, string to)
    {
        var subject = "Test-Mail aus Schaub HR";
        var html = $@"<!DOCTYPE html><html><body style=""font-family:-apple-system,BlinkMacSystemFont,sans-serif;background:#f1f5f9;padding:24px"">
            <div style=""max-width:480px;margin:0 auto;background:#fff;border-radius:12px;padding:24px;box-shadow:0 2px 8px rgba(0,0,0,0.06)"">
                <h2 style=""margin:0 0 12px;color:#0f172a"">✓ Test-Mail empfangen</h2>
                <p style=""color:#475569;font-size:14px;line-height:1.6"">
                    Wenn diese Mail bei dir angekommen ist, ist deine SMTP-Konfiguration korrekt.<br>
                    <strong>Host:</strong> {System.Net.WebUtility.HtmlEncode(cfg.Host)}:{cfg.Port}<br>
                    <strong>Absender:</strong> {System.Net.WebUtility.HtmlEncode(cfg.FromAddress)}<br>
                    <strong>Zeit:</strong> {DateTime.Now:dd.MM.yyyy HH:mm:ss}
                </p>
            </div></body></html>";
        var text = $"Test-Mail aus Schaub HR\n\nWenn diese Mail bei dir angekommen ist, ist deine SMTP-Konfiguration korrekt.\n\nHost: {cfg.Host}:{cfg.Port}\nAbsender: {cfg.FromAddress}\nZeit: {DateTime.Now:dd.MM.yyyy HH:mm:ss}";

        await _emailSvc.SendTestMailAsync(cfg, to, subject, html, text);
    }

    /// <summary>
    /// Ergebnis des Test-Mail-Knopfs. Seit dem Umbau auf die Freigabe-Matrix
    /// (Walter 01.09.2026) geht die Test-Mail IMMER an die von Hand
    /// eingetippte Adresse — sie steht bewusst ausserhalb der Kategorien.
    /// Vorher wurde sie in die Test-Umleitung gezogen; das war sinnvoll,
    /// solange «Feld gefüllt» der Hauptschalter war. Jetzt bleibt das Feld
    /// dauerhaft gefüllt, und ein Testknopf, der nie dorthin sendet, wohin
    /// man ihn schickt, wäre nutzlos.
    /// </summary>
    private static object BuildOkResult(EmailService.EffectiveSmtpConfig cfg, string requestedTo)
    {
        return new
        {
            ok = true,
            requestedTo,
            actualTo = requestedTo,
            redirected = false,
            host = cfg.Host,
            port = cfg.Port
        };
    }

    /// <summary>Leerstring und Nur-Leerzeichen werden zu NULL.</summary>
    private static string? Leer(string? v)
        => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ══ Rückläufer (Walter-Vorgabe 01.09.2026) ═══════════════════════════

    /// <summary>
    /// Postfach jetzt abrufen. Läuft auch, wenn der Haken «Abruf aktiv»
    /// noch aus ist — genau so testet man die Verbindung, bevor man den
    /// stündlichen Dienst scharf schaltet.
    /// </summary>
    [HttpPost("bounce/abrufen")]
    public async Task<IActionResult> BounceAbrufen([FromServices] BounceAbrufService dienst,
                                                   [FromQuery] bool auchGelesene,
                                                   CancellationToken ct)
    {
        var res = await dienst.AbrufenAsync(ct, auchGelesene);
        if (res.Fehler != null)
            return Ok(new { ok = false, fehler = res.Fehler, res.Geprueft, res.Erfasst });

        // Eine neu gesperrte Adresse soll sofort wirken, nicht erst nach
        // Ablauf des Minuten-Caches.
        EmailService.SperrlisteVerwerfen();

        return Ok(new { ok = true, res.Geprueft, res.Erfasst, res.Uebersprungen, res.Unklar });
    }

    /// <summary>Die letzten Rückläufer, neueste zuerst.</summary>
    [HttpGet("bounce")]
    public async Task<IActionResult> BounceListe([FromQuery] int limit = 50,
                                                 [FromQuery] bool nurOffen = false)
    {
        var q = _db.MailBounces.AsNoTracking().AsQueryable();
        if (nurOffen) q = q.Where(b => !b.Erledigt);

        var rows = await q
            .OrderByDescending(b => b.EmpfangenAm)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(b => new
            {
                b.Id,
                b.EmpfangenAm,
                b.Adresse,
                b.Hart,
                b.Code,
                b.Grund,
                b.OriginalBetreff,
                b.Erledigt,
                b.ErledigtAm,
                b.EmployeeId,
                MaNummer = b.Employee != null ? b.Employee.EmployeeNumber : null,
                MaName   = b.Employee != null
                    ? ((b.Employee.FirstName ?? "") + " " + (b.Employee.LastName ?? "")).Trim()
                    : null,
            })
            .ToListAsync();

        return Ok(rows);
    }

    /// <summary>
    /// Rückläufer als erledigt markieren. Hebt zugleich die Versandsperre
    /// auf — die Adresse wird also wieder angeschrieben.
    /// </summary>
    [HttpPost("bounce/{id:int}/erledigt")]
    public async Task<IActionResult> BounceErledigt(int id)
    {
        var row = await _db.MailBounces.FirstOrDefaultAsync(b => b.Id == id);
        if (row == null) return NotFound();

        row.Erledigt          = true;
        row.ErledigtAm        = DateTime.Now;
        row.ErledigtVonUserId = GetCurrentUserId();
        await _db.SaveChangesAsync();

        EmailService.SperrlisteVerwerfen();
        return Ok(new { ok = true });
    }

    private int? GetCurrentUserId()
    {
        var sub = User.FindFirstValue("sub")
               ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }
}

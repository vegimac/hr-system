using HrSystem.Data;
using HrSystem.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace HrSystem.Services;

/// <summary>
/// SMTP-Versand für MA-Postfach-Benachrichtigungen (Lohnzettel-Bereit-
/// Mail, eingehende Dokumente).
///
/// Konfiguration kommt primär aus der DB (smtp_setting, Singleton-Row).
/// Falls die Tabelle leer ist, fallen wir auf appsettings.json:Smtp
/// zurück — das ist die Migrations-Brücke vom alten Hard-Coded-Setup.
///
/// Test-Modus: wenn TestRedirectTo gesetzt ist, gehen ALLE Mails an
/// diese Adresse statt an den eigentlichen Empfänger — der eigentliche
/// Empfänger steht im Subject-Prefix [TEST → original@adresse]. Damit
/// kann Walter den Lohnlauf-Auto-Versand auf Echt-Daten testen, ohne
/// dass Mails versehentlich an MA rausgehen.
///
/// Caching: 30 Sekunden, damit nicht jede Mail einen DB-Roundtrip
/// auslöst. Wenn Walter im Admin etwas ändert, sieht er den Effekt
/// also evtl. erst nach max. 30 Sekunden — Admin-Controller ruft nach
/// PUT explizit InvalidateCache() auf, um sofortige Wirkung zu haben.
/// </summary>
public class EmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _log;
    private readonly AppDbContext _db;
    private readonly SimpleAesService _aes;

    private static EffectiveSmtpConfig? _cache;
    private static DateTime _cacheUntil = DateTime.MinValue;
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public EmailService(IConfiguration config, ILogger<EmailService> log,
                        AppDbContext db, SimpleAesService aes)
    {
        _config = config;
        _log    = log;
        _db     = db;
        _aes    = aes;
    }

    public record EffectiveSmtpConfig(
        string Host, int Port, string Username, string Password,
        string FromName, string FromAddress, string? TestRedirectTo, string SiteUrl);

    /// <summary>
    /// Liefert die effektive Konfig: erst aus DB (smtp_setting), wenn
    /// nichts da steht, Fallback auf appsettings.json:Smtp.
    /// </summary>
    public async Task<EffectiveSmtpConfig> GetEffectiveConfigAsync(bool useCache = true)
    {
        if (useCache)
        {
            lock (_cacheLock)
            {
                if (_cache != null && DateTime.UtcNow < _cacheUntil)
                    return _cache;
            }
        }

        var row = await _db.SmtpSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == 1);
        EffectiveSmtpConfig cfg;
        if (row != null && !string.IsNullOrWhiteSpace(row.Host))
        {
            cfg = new EffectiveSmtpConfig(
                row.Host,
                row.Port > 0 ? row.Port : 587,
                row.Username ?? "",
                _aes.Decrypt(row.PasswordEncrypted),
                string.IsNullOrWhiteSpace(row.FromName) ? "Schaub HR" : row.FromName,
                row.FromAddress ?? row.Username ?? "",
                string.IsNullOrWhiteSpace(row.TestRedirectTo) ? null : row.TestRedirectTo,
                string.IsNullOrWhiteSpace(row.SiteUrl) ? "https://onecrew.ch/" : row.SiteUrl);
        }
        else
        {
            var smtp = _config.GetSection("Smtp");
            var port = int.TryParse(smtp["Port"], out var p) ? p : 587;
            cfg = new EffectiveSmtpConfig(
                smtp["Host"] ?? "",
                port,
                smtp["Username"] ?? "",
                smtp["Password"] ?? "",
                smtp["FromName"] ?? "Schaub HR",
                smtp["FromAddress"] ?? smtp["Username"] ?? "",
                string.IsNullOrWhiteSpace(smtp["TestRedirectTo"]) ? null : smtp["TestRedirectTo"],
                string.IsNullOrWhiteSpace(smtp["SiteUrl"]) ? "https://onecrew.ch/" : smtp["SiteUrl"]!);
        }

        lock (_cacheLock)
        {
            _cache = cfg;
            _cacheUntil = DateTime.UtcNow + CacheTtl;
        }
        return cfg;
    }

    /// <summary>
    /// Cache invalidieren — vom Admin-Controller nach PUT aufrufen,
    /// damit die nächste Mail die neue Konfig sieht.
    /// </summary>
    public static void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cache = null;
            _cacheUntil = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Sendet eine Mail. Wirft KEINE Exceptions raus — ein Mail-Fehler darf
    /// einen Lohnabschluss nicht blockieren. Fehler werden geloggt.
    /// </summary>
    public async Task<bool> SendAsync(string to, string? toName, string subject, string htmlBody, string textBody)
    {
        var cfg = await GetEffectiveConfigAsync();
        try { await SendCoreAsync(cfg, to, toName, subject, htmlBody, textBody, throwOnError: false); return true; }
        catch (Exception ex)
        {
            _log.LogError(ex, "[EmailService] Mail-Versand fehlgeschlagen an {To} — {Subject}", to, subject);
            return false;
        }
    }

    /// <summary>
    /// Wie SendAsync, aber mit explizit übergebener Konfig — vom
    /// Test-Mail-Endpoint genutzt, um die im UI eingegebene (noch nicht
    /// gespeicherte) Konfig zu testen. Wirft Exceptions hoch.
    /// </summary>
    public async Task SendTestMailAsync(EffectiveSmtpConfig cfg, string to, string subject, string htmlBody, string textBody)
    {
        await SendCoreAsync(cfg, to, null, subject, htmlBody, textBody, throwOnError: true);
    }

    /// <summary>
    /// Versand mit PDF-Anhang (Walter 16.07.2026, z.B. Arztbrief).
    /// Liefert true bei Erfolg, wirft bei Fehler NICHT (loggt nur).
    /// </summary>
    public async Task<bool> SendWithAttachmentAsync(string to, string? toName, string subject,
        string htmlBody, string textBody, byte[] attachment, string attachmentName)
        => await SendWithAttachmentsAsync(to, toName, subject, htmlBody, textBody,
               new List<(byte[], string)> { (attachment, attachmentName) });

    /// <summary>Versand mit MEHREREN PDF-Anhaengen (Walter 16.07.2026,
    /// z.B. Arztbrief + Risikobeurteilung).</summary>
    public async Task<bool> SendWithAttachmentsAsync(string to, string? toName, string subject,
        string htmlBody, string textBody, List<(byte[] Data, string Name)> attachments)
    {
        var cfg = await GetEffectiveConfigAsync();
        try
        {
            await SendCoreAsync(cfg, to, toName, subject, htmlBody, textBody,
                throwOnError: true, attachments: attachments);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[EmailService] Mail mit Anhang fehlgeschlagen an {To} — {Subject}", to, subject);
            return false;
        }
    }

    private async Task SendCoreAsync(EffectiveSmtpConfig cfg, string to, string? toName, string subject, string htmlBody, string textBody, bool throwOnError, List<(byte[] Data, string Name)>? attachments = null)
    {
        if (string.IsNullOrWhiteSpace(cfg.Host) || string.IsNullOrWhiteSpace(cfg.FromAddress))
        {
            var msg = "[EmailService] SMTP nicht konfiguriert (Host/FromAddress fehlt)";
            _log.LogWarning("{Msg} — Mail an {To} wird übersprungen.", msg, to);
            if (throwOnError) throw new InvalidOperationException(msg);
            return;
        }

        var effectiveTo = to;
        var effectiveToName = toName;
        var effectiveSubject = subject;
        if (!string.IsNullOrWhiteSpace(cfg.TestRedirectTo))
        {
            effectiveTo = cfg.TestRedirectTo!;
            effectiveToName = "Test-Empfänger";
            effectiveSubject = $"[TEST → {to}] {subject}";
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(cfg.FromName, cfg.FromAddress));
        mime.To.Add(new MailboxAddress(effectiveToName ?? "", effectiveTo));
        mime.Subject = effectiveSubject;

        var builder = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody };
        if (attachments != null)
            foreach (var (data, name) in attachments)
                if (data is { Length: > 0 })
                    builder.Attachments.Add(name, data, new MimeKit.ContentType("application", "pdf"));
        mime.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        var secure = cfg.Port == 465 ? SecureSocketOptions.SslOnConnect
                                     : SecureSocketOptions.StartTls;
        await client.ConnectAsync(cfg.Host, cfg.Port, secure);
        if (!string.IsNullOrWhiteSpace(cfg.Username))
            await client.AuthenticateAsync(cfg.Username, cfg.Password);
        await client.SendAsync(mime);
        await client.DisconnectAsync(true);

        _log.LogInformation("[EmailService] Mail gesendet an {To} (effektiv: {Eff}) — {Subject}",
                            to, effectiveTo, subject);
    }

    /// <summary>
    /// Spezial-Helper: "Dein Lohnzettel ist bereit"-Mail an einen MA.
    /// Site-URL wird aus der DB-Konfig genommen (oder Override-Param).
    /// </summary>
    public async Task SendLohnzettelNotificationAsync(string toEmail, string firstName, int year, int month, string? siteUrlOverride = null)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) return;

        var cfg = await GetEffectiveConfigAsync();
        var siteUrl = string.IsNullOrWhiteSpace(siteUrlOverride) ? cfg.SiteUrl : siteUrlOverride;

        var monatNames = new[] {
            "Januar","Februar","März","April","Mai","Juni",
            "Juli","August","September","Oktober","November","Dezember"
        };
        var monatLabel = (month >= 1 && month <= 12) ? $"{monatNames[month-1]} {year}" : $"{year}-{month:D2}";

        var subject = $"Dein Lohnzettel {monatLabel} ist bereit";
        var greeting = string.IsNullOrWhiteSpace(firstName) ? "Hallo" : $"Hallo {firstName}";
        var loginUrl = string.IsNullOrWhiteSpace(siteUrl) ? "https://onecrew.ch/" : siteUrl.TrimEnd('/') + "/";

        var html = $@"<!DOCTYPE html>
<html><head><meta charset=""UTF-8""></head>
<body style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f1f5f9;margin:0;padding:20px"">
  <div style=""max-width:540px;margin:0 auto;background:#fff;border-radius:14px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.06)"">
    <div style=""background:linear-gradient(135deg,#1e3a8a 0%,#2563eb 100%);color:#fff;padding:24px 28px"">
      <div style=""font-weight:700;font-size:14px;opacity:0.9"">Schaub HR</div>
      <div style=""font-size:22px;font-weight:700;margin-top:6px"">📬 {monatLabel}</div>
    </div>
    <div style=""padding:24px 28px;color:#0f172a"">
      <p style=""font-size:16px;margin:0 0 14px"">{greeting},</p>
      <p style=""font-size:14px;line-height:1.55;margin:0 0 18px"">
        dein Lohnzettel für <strong>{monatLabel}</strong> ist in deinem persönlichen Postfach bereit.
        Du kannst ihn jederzeit dort einsehen, herunterladen oder ausdrucken.
      </p>
      <p style=""text-align:center;margin:24px 0"">
        <a href=""{loginUrl}"" style=""display:inline-block;background:#2563eb;color:#fff;text-decoration:none;padding:12px 28px;border-radius:10px;font-weight:600;font-size:14px"">
          Zum Postfach →
        </a>
      </p>
      <p style=""font-size:12.5px;color:#64748b;line-height:1.5;margin:18px 0 0"">
        Login: deine Personalnummer + dein gewähltes Passwort. Falls du dein Passwort vergessen hast, melde dich bei deinem Geschäftsführer oder der HR-Verantwortlichen.
      </p>
    </div>
    <div style=""padding:14px 28px;background:#f8fafc;color:#94a3b8;font-size:11.5px;text-align:center;border-top:1px solid #e2e8f0"">
      Diese Nachricht wurde automatisch von Schaub HR versendet — bitte nicht antworten.
    </div>
  </div>
</body></html>";

        var text = $@"{greeting},

Dein Lohnzettel für {monatLabel} ist in deinem persönlichen Postfach bereit.

Zum Postfach: {loginUrl}

Login: deine Personalnummer + dein gewähltes Passwort.
Falls du dein Passwort vergessen hast, melde dich bei deinem Geschäftsführer oder der HR-Verantwortlichen.

—
Schaub HR (automatisch versendet — bitte nicht antworten)";

        await SendAsync(toEmail, firstName, subject, html, text);
    }

    /// <summary>
    /// Behörden-Mail mit Download-Link zum Jahres-Lohnausweis (Walter 30.07.2026).
    /// KEIN PDF-Anhang — nur Link zur Landing-Page (Messaging-Preview-Schutz).
    /// Sie-Form, da Empfänger eine Behörde ist.
    /// </summary>
    public async Task SendLohnausweisBehoerdeNotificationAsync(
        string toEmail,
        string? behoerdeName,
        string employeeDisplayName,
        int year,
        string downloadUrl,
        DateTime expiresAt,
        string? sachbearbeiterName = null)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) return;

        var subject = $"Lohnausweis {year} — {employeeDisplayName}";
        // Mit SB: persönlich z.Hd.; sonst allgemeine Behörden-Anrede.
        string empf;
        if (!string.IsNullOrWhiteSpace(sachbearbeiterName))
        {
            var sb = sachbearbeiterName.Trim();
            empf = string.IsNullOrWhiteSpace(behoerdeName)
                ? $"Sehr geehrte Damen und Herren, z.Hd. {sb}"
                : $"Sehr geehrte Damen und Herren ({behoerdeName.Trim()}), z.Hd. {sb}";
        }
        else
        {
            empf = string.IsNullOrWhiteSpace(behoerdeName)
                ? "Sehr geehrte Damen und Herren"
                : $"Sehr geehrte Damen und Herren ({behoerdeName.Trim()})";
        }
        var gueltig = expiresAt.ToString("dd.MM.yyyy");
        var safeUrl = System.Net.WebUtility.HtmlEncode(downloadUrl);
        var safeMa = System.Net.WebUtility.HtmlEncode(employeeDisplayName);

        var html = $@"<!DOCTYPE html>
<html><head><meta charset=""UTF-8""></head>
<body style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f1f5f9;margin:0;padding:20px"">
  <div style=""max-width:540px;margin:0 auto;background:#fff;border-radius:14px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.06)"">
    <div style=""background:linear-gradient(135deg,#3f3f3f 0%,#1a1a1a 100%);color:#fff;padding:24px 28px"">
      <div style=""font-weight:700;font-size:14px;opacity:0.9"">Schaub Restaurants GmbH — HR</div>
      <div style=""font-size:20px;font-weight:700;margin-top:6px"">Lohnausweis {year}</div>
    </div>
    <div style=""padding:24px 28px;color:#0f172a"">
      <p style=""font-size:15px;margin:0 0 14px"">{System.Net.WebUtility.HtmlEncode(empf)},</p>
      <p style=""font-size:14px;line-height:1.55;margin:0 0 18px"">
        im Rahmen der Lohnabtretung stellen wir Ihnen den Jahres-Lohnausweis
        <strong>{year}</strong> für <strong>{safeMa}</strong> zum Download bereit.
      </p>
      <p style=""text-align:center;margin:24px 0"">
        <a href=""{safeUrl}"" style=""display:inline-block;background:#3f3f3f;color:#fff;text-decoration:none;padding:12px 28px;border-radius:10px;font-weight:600;font-size:14px"">
          Lohnausweis herunterladen →
        </a>
      </p>
      <p style=""font-size:12.5px;color:#64748b;line-height:1.5;margin:18px 0 0"">
        Der Link ist bis <strong>{gueltig}</strong> gültig. Aus Datenschutzgründen
        ist kein PDF angehängt — bitte öffnen Sie den Link und laden Sie das Dokument dort herunter.
      </p>
    </div>
    <div style=""padding:14px 28px;background:#f8fafc;color:#94a3b8;font-size:11.5px;text-align:center;border-top:1px solid #e2e8f0"">
      Diese Nachricht wurde automatisch von Schaub HR versendet — bitte nicht antworten.
    </div>
  </div>
</body></html>";

        var text = $@"{empf},

im Rahmen der Lohnabtretung stellen wir Ihnen den Jahres-Lohnausweis {year}
für {employeeDisplayName} zum Download bereit.

Download: {downloadUrl}

Der Link ist bis {gueltig} gültig. Aus Datenschutzgründen ist kein PDF angehängt.

—
Schaub HR (automatisch versendet — bitte nicht antworten)";

        await SendAsync(toEmail, behoerdeName, subject, html, text);
    }
}

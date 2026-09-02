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
/// Ob umgeleitet wird, entscheidet ab 01.09.2026 NICHT mehr allein das
/// Feld, sondern die Haken-Matrix je <see cref="VersandKategorie"/> in
/// der Systemsteuerung (Tabelle versand_kategorie): Haken = scharf,
/// kein Haken = Umleitung. Die Test-Adresse bleibt dauerhaft stehen und
/// ist nur noch das Ziel der Umleitung.
/// Steht sie leer und die Kategorie ist nicht scharf, wird der Versand
/// BLOCKIERT statt scharf durchgelassen.
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

    private readonly VersandFreigabeService _freigabe;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebHostEnvironment _env;

    /// <summary>
    /// Content-ID des eingebetteten Logos. Mail-Clients (Gmail, Outlook)
    /// blockieren nachgeladene Bilder von fremden Servern — darum reist das
    /// Logo als Teil der Mail mit und wird ueber cid: referenziert.
    /// </summary>
    public const string LogoCid = "onecrew-logo";

    private static byte[]? _logoBytes;
    private static bool _logoGesucht;
    private static readonly object _logoLock = new();

    // ── Sperrliste harter Rückläufer (Walter-Vorgabe 01.09.2026) ──────────
    // Adressen, die nachweislich nicht existieren, werden nicht mehr
    // angeschrieben. Als Menge im Speicher gehalten und minütlich erneuert:
    // bei einem Versand an 300 MA wäre sonst pro Mail eine Abfrage fällig.
    private static HashSet<string>? _gesperrt;
    private static DateTime _gesperrtBis = DateTime.MinValue;
    private static readonly object _gesperrtLock = new();
    private static readonly TimeSpan GesperrtTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Cache der Sperrliste verwerfen — aufrufen, wenn ein Rückläufer als
    /// erledigt markiert oder eine Adresse korrigiert wurde, damit die
    /// nächste Mail sofort wieder durchgeht.
    /// </summary>
    public static void SperrlisteVerwerfen()
    {
        lock (_gesperrtLock) { _gesperrt = null; _gesperrtBis = DateTime.MinValue; }
    }

    /// <summary>
    /// Ist diese Adresse wegen eines offenen HARTEN Rückläufers gesperrt?
    ///
    /// Bewusst NICHT fail-safe in Richtung «blockieren»: Wenn die Abfrage
    /// scheitert, wird gesendet. Anders als bei der Freigabe-Matrix ist
    /// hier der teurere Fehler, versehentlich NICHTS zu verschicken — ein
    /// Lohnzettel-Hinweis, der wegen einer Datenbank-Störung ausbleibt,
    /// fällt niemandem auf.
    /// </summary>
    private async Task<bool> IstGesperrtAsync(string adresse)
    {
        if (string.IsNullOrWhiteSpace(adresse)) return false;
        var adr = adresse.Trim().ToLowerInvariant();

        lock (_gesperrtLock)
        {
            if (_gesperrt != null && DateTime.UtcNow < _gesperrtBis)
                return _gesperrt.Contains(adr);
        }

        try
        {
            // Eigener Scope: SendCoreAsync läuft mitten im Lohnlauf, und der
            // injizierte Kontext hat dann offene Änderungen. Eine Lese-
            // abfrage darauf ist harmlos, aber wir bleiben beim Muster von
            // TryWriteLogAsync und fassen den Aufrufer-Kontext nicht an.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var liste = await db.MailBounces.AsNoTracking()
                .Where(b => b.Hart && !b.Erledigt)
                .Select(b => b.Adresse)
                .Distinct()
                .ToListAsync();

            var menge = new HashSet<string>(
                liste.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);

            lock (_gesperrtLock) { _gesperrt = menge; _gesperrtBis = DateTime.UtcNow.Add(GesperrtTtl); }
            return menge.Contains(adr);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[EmailService] Sperrliste konnte nicht gelesen werden — es wird gesendet.");
            return false;
        }
    }

    public EmailService(IConfiguration config, ILogger<EmailService> log,
                        AppDbContext db, SimpleAesService aes,
                        VersandFreigabeService freigabe,
                        IServiceScopeFactory scopeFactory,
                        IWebHostEnvironment env)
    {
        _config       = config;
        _log          = log;
        _db           = db;
        _aes          = aes;
        _freigabe     = freigabe;
        _scopeFactory = scopeFactory;
        _env          = env;
    }

    /// <summary>
    /// Das offizielle OneCrew-Logo (schwarz, transparenter Grund) fuer den
    /// Mail-Kopf. Bewusst dieselbe Datei wie in der Sidebar der App — so
    /// bleibt die Mail automatisch aktuell, wenn das Logo je ersetzt wird.
    /// Einmal von Platte gelesen und danach im Speicher gehalten: bei einem
    /// Massenversand an alle MA waere sonst pro Mail ein Datei-Zugriff faellig.
    /// Fehlt die Datei, liefert die Methode null; der Kopf zeigt dann nur den
    /// alt-Text, die Mail bleibt lesbar (siehe HtmlRahmen).
    /// </summary>
    private byte[]? LogoLaden()
    {
        lock (_logoLock)
        {
            if (_logoGesucht) return _logoBytes;
            _logoGesucht = true;
            try
            {
                const string datei = "onecrew-logo.png";
                var kandidaten = new[]
                {
                    Path.Combine(_env.WebRootPath ?? "", "img", datei),
                    Path.Combine(_env.ContentRootPath ?? "", "wwwroot", "img", datei),
                    Path.Combine(AppContext.BaseDirectory, "wwwroot", "img", datei),
                };
                foreach (var pfad in kandidaten)
                {
                    if (!string.IsNullOrWhiteSpace(pfad) && File.Exists(pfad))
                    {
                        _logoBytes = File.ReadAllBytes(pfad);
                        _log.LogInformation("[EmailService] Mail-Logo geladen: {Pfad} ({Bytes} Bytes)",
                                            pfad, _logoBytes.Length);
                        return _logoBytes;
                    }
                }
                _log.LogWarning("[EmailService] Mail-Logo onecrew-logo.png nicht gefunden — "
                              + "der Mail-Kopf zeigt stattdessen nur den alt-Text.");
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[EmailService] Mail-Logo konnte nicht gelesen werden.");
            }
            return _logoBytes;
        }
    }

    /// <param name="BounceAddress">
    /// Rücksendeadresse (Return-Path) für Zustellmeldungen. NICHT der
    /// sichtbare Absender — der bleibt FromAddress. null = wie bisher,
    /// Rückläufer gehen an FromAddress.
    /// </param>
    public record EffectiveSmtpConfig(
        string Host, int Port, string Username, string Password,
        string FromName, string FromAddress, string? TestRedirectTo, string SiteUrl,
        string? BounceAddress = null);

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
                string.IsNullOrWhiteSpace(row.SiteUrl) ? "https://onecrew.ch/" : row.SiteUrl,
                string.IsNullOrWhiteSpace(row.BounceAddress) ? null : row.BounceAddress.Trim());
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
    public async Task<bool> SendAsync(string to, string? toName, string subject, string htmlBody, string textBody,
        VersandKategorie kategorie, int? employeeId = null, int? gruppenMailLogId = null)
    {
        var cfg = await GetEffectiveConfigAsync();
        try { await SendCoreAsync(cfg, to, toName, subject, htmlBody, textBody, throwOnError: false, kategorie: kategorie, employeeId: employeeId, gruppenMailLogId: gruppenMailLogId); return true; }
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
        string htmlBody, string textBody, byte[] attachment, string attachmentName,
        VersandKategorie kategorie, int? employeeId = null)
        => await SendWithAttachmentsAsync(to, toName, subject, htmlBody, textBody,
               new List<(byte[], string)> { (attachment, attachmentName) }, kategorie, employeeId);

    /// <summary>Versand mit MEHREREN PDF-Anhaengen (Walter 16.07.2026,
    /// z.B. Arztbrief + Risikobeurteilung).</summary>
    public async Task<bool> SendWithAttachmentsAsync(string to, string? toName, string subject,
        string htmlBody, string textBody, List<(byte[] Data, string Name)> attachments,
        VersandKategorie kategorie, int? employeeId = null, int? gruppenMailLogId = null)
    {
        var cfg = await GetEffectiveConfigAsync();
        try
        {
            await SendCoreAsync(cfg, to, toName, subject, htmlBody, textBody,
                throwOnError: true, attachments: attachments, kategorie: kategorie, employeeId: employeeId,
                gruppenMailLogId: gruppenMailLogId);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[EmailService] Mail mit Anhang fehlgeschlagen an {To} — {Subject}", to, subject);
            return false;
        }
    }

    private async Task SendCoreAsync(EffectiveSmtpConfig cfg, string to, string? toName, string subject, string htmlBody, string textBody, bool throwOnError, List<(byte[] Data, string Name)>? attachments = null, VersandKategorie? kategorie = null, int? employeeId = null, int? gruppenMailLogId = null)
    {
        var kategorieCode = kategorie.HasValue ? VersandKategorien.Code(kategorie.Value) : null;
        var anhaenge = attachments?.Count(a => a.Data is { Length: > 0 }) ?? 0;

        if (string.IsNullOrWhiteSpace(cfg.Host) || string.IsNullOrWhiteSpace(cfg.FromAddress))
        {
            var msg = "[EmailService] SMTP nicht konfiguriert (Host/FromAddress fehlt)";
            _log.LogWarning("{Msg} — Mail an {To} wird übersprungen.", msg, to);
            await TryWriteLogAsync(kategorieCode, employeeId, to, null, subject, anhaenge, false, "SMTP nicht konfiguriert", gruppenMailLogId);
            if (throwOnError) throw new InvalidOperationException(msg);
            return;
        }

        var effectiveTo = to;
        var effectiveToName = toName;
        var effectiveSubject = subject;
        string? redirectedTo = null;

        // Freigabe-Matrix (Walter 01.09.2026): pro Kategorie ein Haken in der
        // Systemsteuerung. Haken = scharf, kein Haken = an die Test-Adresse.
        // kategorie == null heisst: bewusst ausserhalb der Matrix (Admin-
        // Test-Mail an eine von Hand eingetippte Adresse).
        if (kategorie.HasValue)
        {
            var scharf = await _freigabe.IstScharfAsync(kategorie.Value, VersandFreigabeService.Kanal.Mail);
            if (!scharf)
            {
                if (string.IsNullOrWhiteSpace(cfg.TestRedirectTo))
                {
                    // Nicht scharf, aber kein Umleitungsziel: NICHT senden.
                    // Der sichere Ausgang gewinnt — sonst würde ausgerechnet
                    // eine unvollständige Konfiguration alles scharf schalten.
                    var msg = $"Kategorie {kategorieCode} ist nicht scharf, "
                            + "aber es ist keine Test-Adresse hinterlegt — Versand blockiert.";
                    _log.LogWarning("[EmailService] {Msg} (an {To})", msg, to);
                    await TryWriteLogAsync(kategorieCode, employeeId, to, null, subject, anhaenge, false, msg, gruppenMailLogId);
                    if (throwOnError) throw new InvalidOperationException(msg);
                    return;
                }
                effectiveTo = cfg.TestRedirectTo!;
                effectiveToName = "Test-Empfänger";
                effectiveSubject = $"[TEST → {to}] {subject}";
                redirectedTo = cfg.TestRedirectTo;
            }
        }

        // Harte Rückläufer: nicht erneut ins Leere senden (Walter 01.09.2026).
        // Geprüft wird die EFFEKTIVE Adresse — bei einer Umleitung auf die
        // Test-Adresse greift die Sperre also bewusst nicht, sonst könnte
        // man einen gesperrten Fall gar nicht mehr nachstellen.
        if (await IstGesperrtAsync(effectiveTo))
        {
            var msg = $"Adresse {effectiveTo} ist wegen eines offenen Rückläufers gesperrt "
                    + "(Adresse existiert nicht) — Versand übersprungen.";
            _log.LogWarning("[EmailService] {Msg}", msg);
            await TryWriteLogAsync(kategorieCode, employeeId, to, redirectedTo, subject, anhaenge, false, msg, gruppenMailLogId);
            if (throwOnError) throw new InvalidOperationException(msg);
            return;
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(cfg.FromName, cfg.FromAddress));
        mime.To.Add(new MailboxAddress(effectiveToName ?? "", effectiveTo));
        mime.Subject = effectiveSubject;

        // Rücksendeadresse: siehe unten beim Senden. Bewusst KEINE
        // Sender-Kopfzeile in der Nachricht — die würde Outlook als
        // «hr@srgmbh.ch im Auftrag von bounce@srgmbh.ch» anzeigen.

        var builder = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody };

        // Logo nur einbetten, wenn die Vorlage es auch referenziert — sonst
        // haengt an jeder Mail ein unsichtbares Bild, das manche Clients als
        // "Anhang"-Klammer anzeigen.
        if (htmlBody != null && htmlBody.Contains("cid:" + LogoCid, StringComparison.OrdinalIgnoreCase))
        {
            var logo = LogoLaden();
            if (logo != null && logo.Length > 0)
            {
                var res = builder.LinkedResources.Add(
                    "onecrew-logo.png", logo, new MimeKit.ContentType("image", "png"));
                res.ContentId = LogoCid;
                // inline, damit der Client es nicht als Datei-Anhang listet
                res.ContentDisposition = new MimeKit.ContentDisposition(
                    MimeKit.ContentDisposition.Inline);
            }
        }

        if (attachments != null)
            foreach (var (data, name) in attachments)
                if (data is { Length: > 0 })
                    builder.Attachments.Add(name, data, MimeTypVon(name));
        mime.Body = builder.ToMessageBody();

        var versuch = await UebermittelnAsync(cfg, mime);

        if (!versuch.Ok)
        {
            await TryWriteLogAsync(kategorieCode, employeeId, to, redirectedTo, subject, anhaenge, false, versuch.Fehler, gruppenMailLogId);

            // Wiedervorlage (Walter-Vorgabe 01.09.2026): Ist der Fehler
            // vorübergehend, merken wir die fertige Mail und versuchen es
            // später gestaffelt nochmals. Vorher endete der Weg genau hier
            // — der Empfänger war still verloren.
            //
            // Nur mit Kategorie: die Admin-Test-Mail wird von Hand ausgelöst
            // und soll ihren Fehler sofort im Fenster zeigen, statt in einer
            // Viertelstunde unbemerkt nochmals rauszugehen.
            if (versuch.Voruebergehend && kategorie.HasValue)
                await TryWiedervorlageAsync(mime, kategorieCode, employeeId, gruppenMailLogId,
                                            to, effectiveTo, redirectedTo, subject,
                                            anhaenge, versuch.Fehler, versuch.Code);

            // Weiterhin werfen, egal ob throwOnError: die Aufrufer führen
            // ihre Fehlerlisten über diesen Weg. Ob die Mail nachträglich
            // doch noch ankommt, sagt die Wiedervorlage — in dieser Sekunde
            // ist sie nicht zugestellt, und genau das gibt der Aufrufer zurück.
            throw versuch.Ausnahme
                  ?? new InvalidOperationException(versuch.Fehler ?? "Mail-Versand fehlgeschlagen");
        }

        await TryWriteLogAsync(kategorieCode, employeeId, to, redirectedTo, subject, anhaenge, true, null, gruppenMailLogId);
        _log.LogInformation("[EmailService] Mail gesendet an {To} (effektiv: {Eff}) — {Subject}",
                            to, effectiveTo, subject);
    }

    // ── Übermittlung + Fehler-Einstufung (Walter-Vorgabe 01.09.2026) ──────

    /// <summary>Ergebnis eines einzelnen SMTP-Übermittlungsversuchs.</summary>
    /// <param name="Voruebergehend">
    /// true = es lohnt sich, dieselbe Mail später nochmals zu schicken.
    /// </param>
    /// <param name="Code">Erweiterter Status-Code aus der Antwort, z.B. «5.7.0».</param>
    public record SmtpVersuch(bool Ok, bool Voruebergehend, string? Fehler, string? Code, Exception? Ausnahme);

    /// <summary>
    /// Verbindet, meldet an und übergibt die Nachricht. Wirft nicht, sondern
    /// liefert das Ergebnis samt Einstufung — der Aufrufer entscheidet, ob
    /// er den Fall in die Wiedervorlage legt oder aufgibt.
    /// </summary>
    private async Task<SmtpVersuch> UebermittelnAsync(EffectiveSmtpConfig cfg, MimeMessage mime)
    {
        using var client = new SmtpClient();
        var secure = cfg.Port == 465 ? SecureSocketOptions.SslOnConnect
                                     : SecureSocketOptions.StartTls;
        try
        {
            await client.ConnectAsync(cfg.Host, cfg.Port, secure);
            if (!string.IsNullOrWhiteSpace(cfg.Username))
                await client.AuthenticateAsync(cfg.Username, cfg.Password);
        }
        catch (System.Security.Authentication.AuthenticationException ex)
        {
            // Falsche Zugangsdaten werden durch Wiederholen nicht richtig —
            // im Gegenteil: viele Anbieter sperren das Konto nach mehreren
            // Fehlversuchen. Also bewusst als endgültig behandeln.
            return new SmtpVersuch(false, false, ex.Message, null, ex);
        }
        catch (SmtpCommandException ex)
        {
            // Der Server hat geantwortet, nur ablehnend — etwa 421 «too many
            // connections». Das ist dieselbe Frage wie beim Senden, also
            // dieselbe Einstufung.
            var (v, c) = FehlerEinstufen(ex);
            return new SmtpVersuch(false, v, ex.Message, c, ex);
        }
        catch (Exception ex)
        {
            // Verbindung gar nicht zustande gekommen (Netz, DNS, TLS). Die
            // Nachricht war nirgends — ein zweiter Versuch kann sie also
            // nicht doppeln, und beim nächsten Mal steht der Server wieder.
            return new SmtpVersuch(false, true, ex.Message, null, ex);
        }

        var uebergeben = false;
        try
        {
            // Rücksendeadresse (Walter-Vorgabe 01.09.2026): Auf einem Umschlag
            // stehen zwei Absender — der Briefkopf, den der Empfänger liest
            // (From, bleibt hr@…), und die Rücksendeadresse, an die die Post
            // Unzustellbares zurückbringt (Envelope-Absender, Return-Path).
            //
            // Diese Überladung setzt NUR den Umschlag (SMTP «MAIL FROM») und
            // fasst die Nachricht selbst nicht an. Der naheliegende Weg über
            // MimeMessage.Sender wäre falsch: der schreibt zusätzlich eine
            // Sender-Kopfzeile, und Outlook macht daraus beim Empfänger
            // «hr@srgmbh.ch im Auftrag von bounce@srgmbh.ch». Genau das will
            // niemand auf einer Mail an alle Mitarbeitenden lesen.
            if (!string.IsNullOrWhiteSpace(cfg.BounceAddress))
            {
                var umschlag = MailboxAddress.Parse(cfg.BounceAddress);
                await client.SendAsync(mime, umschlag, mime.To.Mailboxes);
            }
            else
            {
                await client.SendAsync(mime);
            }
            uebergeben = true;
            await client.DisconnectAsync(true);
            return new SmtpVersuch(true, false, null, null, null);
        }
        catch (Exception ex)
        {
            // Stolperstelle: Scheitert erst das Trennen der Verbindung, ist
            // die Mail längst übergeben. Würde dieser Fall als Fehlschlag
            // gelten, käme die Mail über die Wiedervorlage ein zweites Mal
            // beim Empfänger an — schlimmer als der ursprüngliche Fehler.
            if (uebergeben)
            {
                _log.LogWarning(ex, "[EmailService] Mail wurde übergeben, nur das Trennen "
                                  + "der Verbindung schlug fehl — gilt als gesendet.");
                return new SmtpVersuch(true, false, null, null, null);
            }

            var (voruebergehend, code) = FehlerEinstufen(ex);
            return new SmtpVersuch(false, voruebergehend, ex.Message, code, ex);
        }
    }

    private static readonly System.Text.RegularExpressions.Regex ErweiterterCode =
        new(@"\b([245])\.(\d{1,3})\.(\d{1,3})\b",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Lohnt sich ein späterer Versuch? (Walter-Vorgabe 01.09.2026)
    ///
    /// Die Frage ist NICHT «war es ein 4er oder ein 5er», sondern «wird es
    /// von selbst wieder gehen» — und die beiden Antworten fallen bei
    /// Hostfactory auseinander.
    /// </summary>
    internal static (bool Voruebergehend, string? Code) FehlerEinstufen(Exception ex)
    {
        var text = ex.Message ?? "";
        var treffer = ErweiterterCode.Match(text);
        string? code = treffer.Success ? treffer.Value : null;

        int? antwort = ex is SmtpCommandException sce ? (int)sce.StatusCode : null;

        // 1) Alles 4.x ist per Definition vorübergehend — sowohl die
        //    dreistellige Antwort (4xx) als auch der erweiterte Code (4.x.x).
        if (antwort is >= 400 and < 500) return (true, code);
        if (code != null && code.StartsWith("4.")) return (true, code);

        // 2) Der Sonderfall, der am 01.09.2026 fünf Empfänger gekostet hat:
        //    Hostfactory beantwortet sein Stundenlimit von rund 275 Mails mit
        //    «5.7.0 The limit on the number of allowed outgoing messages was
        //    exceeded. Try again later.» — formal endgültig, fachlich das
        //    Gegenteil davon; «Try again later» steht sogar dabei.
        //    Geprüft wird der Text und nicht bloss der Code 5.7.0: derselbe
        //    Sachverhalt heisst anderswo 4.7.1, 452 oder «too many messages»,
        //    und 5.7.0 allein bedeutet sonst schlicht «nicht erlaubt».
        if (IstMengenGrenze(text)) return (true, code);

        // 3) Alles Übrige gilt als endgültig — vor allem 5.1.x «Adresse
        //    existiert nicht». Wiederholen erzeugt dort nur denselben Fehler
        //    ein zweites Mal; dafür gibt es die Rückläufer-Logik.
        return (false, code);
    }

    /// <summary>
    /// Formulierungen, die eine Grenze beschreiben, die sich von selbst
    /// wieder öffnet. Bewusst ganze Wendungen statt einzelner Wörter:
    /// «too many messages» ist eine Sendegrenze pro Stunde und geht später
    /// wieder, «too many recipients» ist die Empfängerzahl EINER Mail und
    /// geht nie wieder. Ein blosses «too many» würde beides gleich behandeln
    /// und den zweiten Fall dreimal vergeblich wiederholen.
    /// </summary>
    private static readonly string[] MengenGrenzePhrasen =
    {
        // Sendegrenze pro Zeitfenster — der Fall vom 01.09.2026.
        "limit on the number of allowed outgoing messages",
        "rate limit", "sending limit", "send limit", "message limit",
        "hourly limit", "daily limit", "submission rate",
        "too many messages", "too many mails", "too many emails",
        "too many connections", "throttl",
        // Postfach des Empfängers voll: formal 5.2.2 und damit endgültig,
        // fachlich vorübergehend. Genau dieselbe Einstufung nimmt die
        // Rückläufer-Logik vor (dort «WEICH»).
        "over quota", "quota exceeded", "mailbox full", "mailbox is full",
        "postfach voll", "insufficient system storage", "out of storage",
    };

    /// <summary>Redet der Server von einer Grenze, die sich wieder öffnet?</summary>
    private static bool IstMengenGrenze(string text)
    {
        var t = (text ?? "").ToLowerInvariant();

        foreach (var wendung in MengenGrenzePhrasen)
            if (t.Contains(wendung)) return true;

        // Auffangnetz für Formulierungen, die hier noch nicht stehen: von
        // einer Grenze ist die Rede, UND der Server sagt selbst, dass man es
        // später nochmals versuchen soll.
        var grenze = t.Contains("limit") || t.Contains("quota");
        var spaeter = t.Contains("try again") || t.Contains("try later")
                   || t.Contains("überschritten") || t.Contains("ueberschritten");
        return grenze && spaeter;
    }

    /// <summary>
    /// Die gescheiterte Mail für einen späteren Versuch merken — best effort:
    /// misslingt das Merken, bleibt es beim bisherigen Verhalten (Fehler im
    /// Protokoll), es geht also nichts verloren, was heute schon da wäre.
    /// </summary>
    private async Task TryWiedervorlageAsync(MimeMessage mime, string? kategorieCode, int? employeeId,
        int? gruppenMailLogId, string? to, string effektiveAdresse, string? redirectedTo,
        string? betreff, int anhaenge, string? fehler, string? code)
    {
        try
        {
            using var ms = new MemoryStream();
            await mime.WriteToAsync(ms);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MailWiedervorlagen.Add(new Models.MailWiedervorlage
            {
                ErstelltAm       = DateTime.Now,
                Kategorie        = kategorieCode,
                EmployeeId       = employeeId,
                GruppenMailLogId = gruppenMailLogId,
                ToEmail          = Kurz(to, 300),
                EffektiveAdresse = Kurz(effektiveAdresse, 300) ?? "",
                RedirectedTo     = Kurz(redirectedTo, 300),
                Betreff          = Kurz(betreff, 500),
                AnhangAnzahl     = anhaenge,
                Mime             = ms.ToArray(),
                Versuche         = 0,
                NaechsterVersuch = DateTime.Now.AddMinutes(MailWiedervorlageService.StaffelungMinuten[0]),
                LetzterFehler    = fehler,
                LetzterCode      = code,
                Status           = Models.MailWiedervorlage.StatusOffen,
            });
            await db.SaveChangesAsync();

            _log.LogWarning("[EmailService] Vorübergehender Fehler an {To} ({Code}) — Mail zur "
                          + "Wiedervorlage gemerkt, nächster Versuch in {Min} Minuten: {Fehler}",
                            effektiveAdresse, code, MailWiedervorlageService.StaffelungMinuten[0], fehler);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[EmailService] Wiedervorlage konnte nicht angelegt werden — "
                            + "die Mail an {To} ist damit endgültig verloren.", effektiveAdresse);
        }
    }

    /// <summary>
    /// Einen gemerkten Fall erneut übermitteln (aufgerufen aus
    /// <see cref="MailWiedervorlageService"/>). Geschickt wird die
    /// GESPEICHERTE Nachricht, nicht eine neu zusammengebaute — dieselbe
    /// Mail, dieselben Anhänge, dieselbe Message-ID.
    /// </summary>
    public async Task<SmtpVersuch> WiedervorlageUebermittelnAsync(Models.MailWiedervorlage eintrag)
    {
        var cfg = await GetEffectiveConfigAsync();
        if (string.IsNullOrWhiteSpace(cfg.Host) || string.IsNullOrWhiteSpace(cfg.FromAddress))
            return new SmtpVersuch(false, true, "SMTP nicht konfiguriert", null, null);

        // Ist die Adresse inzwischen hart zurückgekommen, ist der Fall
        // entschieden: nicht nochmals ins Leere senden.
        if (await IstGesperrtAsync(eintrag.EffektiveAdresse))
            return await WiedervorlageGescheitertAsync(eintrag, false,
                $"Adresse {eintrag.EffektiveAdresse} ist wegen eines offenen Rückläufers gesperrt.");

        MimeMessage mime;
        try
        {
            using var ms = new MemoryStream(eintrag.Mime ?? Array.Empty<byte>());
            mime = await MimeMessage.LoadAsync(ms);
        }
        catch (Exception ex)
        {
            return await WiedervorlageGescheitertAsync(eintrag, false,
                "Gespeicherte Nachricht ist unlesbar: " + ex.Message, ex);
        }

        var versuch = await UebermittelnAsync(cfg, mime);

        await TryWriteLogAsync(eintrag.Kategorie, eintrag.EmployeeId, eintrag.ToEmail,
            eintrag.RedirectedTo, eintrag.Betreff, eintrag.AnhangAnzahl,
            versuch.Ok, versuch.Fehler, eintrag.GruppenMailLogId, wiedervorlage: true);

        return versuch;
    }

    /// <summary>
    /// Ein Wiederholungsversuch, der schon vor dem SMTP-Gespräch scheitert —
    /// mit Protokolleintrag. Ohne den stünde der Fall später auf
    /// «aufgegeben», ohne dass im Versandprotokoll ein Grund nachzulesen wäre.
    /// </summary>
    private async Task<SmtpVersuch> WiedervorlageGescheitertAsync(
        Models.MailWiedervorlage eintrag, bool voruebergehend, string fehler, Exception? ex = null)
    {
        await TryWriteLogAsync(eintrag.Kategorie, eintrag.EmployeeId, eintrag.ToEmail,
            eintrag.RedirectedTo, eintrag.Betreff, eintrag.AnhangAnzahl,
            false, fehler, eintrag.GruppenMailLogId, wiedervorlage: true);

        return new SmtpVersuch(false, voruebergehend, fehler, null, ex);
    }

    /// <summary>
    /// MIME-Typ aus der Dateiendung (Walter 01.09.2026). Vorher war der Typ
    /// hart auf application/pdf verdrahtet — solange nur Arztbriefe verschickt
    /// wurden, fiel das nicht auf. Seit der Gruppen-E-Mail beliebige Dokumente
    /// anhängen kann, käme ein Word-Dokument als kaputtes PDF beim Empfänger
    /// an. Unbekanntes bleibt bewusst octet-stream: dann lädt der Empfänger
    /// die Datei herunter, statt dass sein Programm sie falsch öffnet.
    /// </summary>
    private static MimeKit.ContentType MimeTypVon(string dateiname)
    {
        var ext = System.IO.Path.GetExtension(dateiname ?? "").ToLowerInvariant();
        return ext switch
        {
            ".pdf"  => new MimeKit.ContentType("application", "pdf"),
            ".png"  => new MimeKit.ContentType("image", "png"),
            ".jpg" or ".jpeg" => new MimeKit.ContentType("image", "jpeg"),
            ".gif"  => new MimeKit.ContentType("image", "gif"),
            ".webp" => new MimeKit.ContentType("image", "webp"),
            ".txt"  => new MimeKit.ContentType("text", "plain"),
            ".csv"  => new MimeKit.ContentType("text", "csv"),
            ".doc"  => new MimeKit.ContentType("application", "msword"),
            ".docx" => new MimeKit.ContentType("application", "vnd.openxmlformats-officedocument.wordprocessingml.document"),
            ".xls"  => new MimeKit.ContentType("application", "vnd.ms-excel"),
            ".xlsx" => new MimeKit.ContentType("application", "vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            ".ppt"  => new MimeKit.ContentType("application", "vnd.ms-powerpoint"),
            ".pptx" => new MimeKit.ContentType("application", "vnd.openxmlformats-officedocument.presentationml.presentation"),
            ".zip"  => new MimeKit.ContentType("application", "zip"),
            _       => new MimeKit.ContentType("application", "octet-stream"),
        };
    }

    /// <summary>
    /// mail_log schreiben — best effort. Bewusst in einem EIGENEN DbContext
    /// (frischer Scope) statt im injizierten _db: Mails gehen mitten aus
    /// laufenden Abläufen raus (Lohnlauf!), und ein SaveChanges auf dem
    /// geteilten Context würde dort anhängige, noch nicht fertige Änderungen
    /// mit committen. Ein Protokolleintrag darf nie fremde Daten schreiben.
    /// </summary>
    private async Task TryWriteLogAsync(string? kategorie, int? employeeId, string? toEmail,
        string? redirectedTo, string? subject, int attachmentCount, bool ok, string? error,
        int? gruppenMailLogId = null, bool wiedervorlage = false)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MailLogs.Add(new Models.MailLog
            {
                CreatedAt       = DateTime.Now,
                Kategorie       = kategorie,
                EmployeeId      = employeeId,
                ToEmail         = Kurz(toEmail, 300),
                RedirectedTo    = Kurz(redirectedTo, 300),
                Subject         = Kurz(subject, 500),
                AttachmentCount = attachmentCount,
                Ok              = ok,
                Error           = error,
                GruppenMailLogId = gruppenMailLogId,
                Wiedervorlage   = wiedervorlage,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[EmailService] mail_log-Eintrag konnte nicht geschrieben werden");
        }
    }

    /// <summary>Auf die Spaltenbreite kürzen; leer wird zu NULL.</summary>
    private static string? Kurz(string? v, int max)
        => string.IsNullOrWhiteSpace(v) ? null : (v.Length <= max ? v : v[..max]);

    /// <summary>
    /// Zentrale HTML-Vorlage fuer OneCrew-Mails (Walter-Vorgabe 01.09.2026).
    /// Liefert den kompletten Rahmen: dunkler Kopf mit Organisation + Titel,
    /// weisse Karte, grauer Fuss. <paramref name="inhaltHtml"/> ist bereits
    /// fertiges HTML und wird NICHT mehr escaped — der Aufrufer muss selber
    /// HtmlEncode anwenden.
    /// Damit sehen Gruppen-Mails gleich aus wie die internen Hinweis-Mails.
    /// </summary>
    /// <param name="titel">Zeile unter dem Organisations-Namen im Kopf.</param>
    /// <param name="inhaltHtml">Body-HTML der Karte.</param>
    /// <param name="fusszeile">
    /// Text im grauen Fuss. null = Standardtext ("bitte nicht antworten").
    /// LEERSTRING = gar kein Fuss (Walter 01.09.2026: bei Gruppen-Mails soll
    /// die Karte nach dem Text aufhoeren, ohne Kleingedrucktes).
    /// </param>
    /// <param name="organisation">
    /// Kopfzeile. Vorerst fest "Schaub Restaurants GmbH"; sobald es weitere
    /// Lizenznehmer gibt, kommt der Wert aus dem Hauptsitz bzw. SMTP-FromName.
    /// </param>
    public static string HtmlRahmen(
        string titel,
        string inhaltHtml,
        string? fusszeile = null,
        string? organisation = null)
    {
        var org  = string.IsNullOrWhiteSpace(organisation) ? "Schaub Restaurants GmbH" : organisation.Trim();
        // Bewusst auf null pruefen, NICHT auf leer: der Leerstring ist die
        // ausdrueckliche Ansage "kein Fuss" und darf nicht zum Default werden.
        var fuss = fusszeile ?? "Diese Nachricht wurde automatisch von OneCrew versendet — bitte nicht antworten.";
        var titelZeile = string.IsNullOrWhiteSpace(titel)
            ? ""
            : $@"<div style=""color:#1a1a1a;font-size:20px;font-weight:700;line-height:1.3;margin-top:14px"">{System.Net.WebUtility.HtmlEncode(titel)}</div>";

        var fussZeile = string.IsNullOrWhiteSpace(fuss)
            ? ""
            : $@"        <tr><td bgcolor=""#f8fafc""
                style=""background-color:#f8fafc;padding:14px 28px;color:#94a3b8;font-size:11.5px;text-align:center;border-top:1px solid #e2e8f0"">
          {System.Net.WebUtility.HtmlEncode(fuss)}
        </td></tr>";

        // ── Warum Tabelle statt <div> und bgcolor statt CSS-Verlauf ─────────
        // Walter-Bug 01.09.2026: Der Kopf kam bei ihm ohne Hintergrundfarbe
        // an. Ursache: "background:linear-gradient(...)" ohne feste Farbe
        // darunter. Etliche Clients (Gmail-App, Outlook, Apple Mail im
        // Dunkelmodus) entfernen den Verlauf ersatzlos — dann hat der Kasten
        // gar keinen Hintergrund mehr, und weil der Grund plötzlich hell ist,
        // färben dieselben Clients den weissen Text automatisch dunkel ein.
        // Das Logo als Bild bleibt weiss: weisse Schrift auf hellgrau.
        // Robust ist nur das alte E-Mail-Handwerk:
        //   • Tabelle mit bgcolor-ATTRIBUT — ein HTML-Attribut kann kein
        //     CSS-Filter wegwerfen; der Verlauf liegt nur noch als Zugabe
        //     obendrauf und darf gefahrlos ignoriert werden.
        //   • Jede Textfarbe direkt am Element, nie geerbt.
        //   • color-scheme "light", damit Clients nicht selber umfärben.
        return $@"<!DOCTYPE html>
<html><head><meta charset=""UTF-8"">
<meta name=""color-scheme"" content=""light"">
<meta name=""supported-color-schemes"" content=""light"">
</head>
<body style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background-color:#f1f5f9;margin:0;padding:20px"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""border-collapse:collapse"">
    <tr><td align=""center"">
      <table role=""presentation"" width=""540"" cellpadding=""0"" cellspacing=""0"" border=""0""
             style=""width:540px;max-width:100%;border-collapse:collapse;background-color:#ffffff;border-radius:14px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.06)"">

        <tr><td bgcolor=""#e7e4db""
                style=""background-color:#e7e4db;background-image:linear-gradient(165deg,#eeece4 0%,#e7e4db 50%,#dfdcd1 100%);padding:26px 28px 24px;border-bottom:1px solid #d0c8b8"">
          <img src=""cid:{LogoCid}"" alt=""OneCrew"" width=""168"" height=""44""
               style=""display:block;width:168px;height:44px;border:0;outline:none;text-decoration:none"">
          <div style=""color:#6b6152;font-size:12.5px;font-weight:700;letter-spacing:0.02em;margin-top:12px"">{System.Net.WebUtility.HtmlEncode(org)}</div>{titelZeile}
        </td></tr>

        <tr><td bgcolor=""#ffffff"" style=""background-color:#ffffff;padding:24px 28px;color:#0f172a"">
{inhaltHtml}
        </td></tr>

{fussZeile}

      </table>
    </td></tr>
  </table>
</body></html>";
    }

    /// <summary>
    /// Spezial-Helper: "Dein Lohnzettel ist bereit"-Mail an einen MA.
    /// Site-URL wird aus der DB-Konfig genommen (oder Override-Param).
    /// </summary>
    public async Task SendLohnzettelNotificationAsync(string toEmail, string firstName, int year, int month, string? siteUrlOverride = null, int? employeeId = null)
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

        // Einheitlicher OneCrew-Rahmen (Walter-Vorgabe 01.09.2026) — vorher
        // eigener blauer "Schaub HR"-Kopf.
        var inhalt = $@"      <p style=""font-size:16px;margin:0 0 14px"">{greeting},</p>
      <p style=""font-size:14px;line-height:1.55;margin:0 0 18px"">
        dein Lohnzettel für <strong>{monatLabel}</strong> ist in deinem persönlichen Postfach bereit.
        Du kannst ihn jederzeit dort einsehen, herunterladen oder ausdrucken.
      </p>
      <p style=""text-align:center;margin:24px 0"">
        <a href=""{loginUrl}"" style=""display:inline-block;background:#1a1a1a;color:#fff;text-decoration:none;padding:12px 28px;border-radius:10px;font-weight:600;font-size:14px"">
          Zum Postfach →
        </a>
      </p>
      <p style=""font-size:12.5px;color:#64748b;line-height:1.5;margin:18px 0 0"">
        Login: deine Personalnummer + dein gewähltes Passwort. Falls du dein Passwort vergessen hast, melde dich bei deinem Geschäftsführer oder der HR-Verantwortlichen.
      </p>";

        var html = HtmlRahmen($"Lohnzettel {monatLabel}", inhalt);

        var text = $@"{greeting},

Dein Lohnzettel für {monatLabel} ist in deinem persönlichen Postfach bereit.

Zum Postfach: {loginUrl}

Login: deine Personalnummer + dein gewähltes Passwort.
Falls du dein Passwort vergessen hast, melde dich bei deinem Geschäftsführer oder der HR-Verantwortlichen.

—
OneCrew (automatisch versendet — bitte nicht antworten)";

        await SendAsync(toEmail, firstName, subject, html, text, VersandKategorie.Lohn, employeeId);
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
        string? sachbearbeiterName = null,
        int? employeeId = null)
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

        // Behörde = externer Dritter.
        await SendAsync(toEmail, behoerdeName, subject, html, text, VersandKategorie.Dritte, employeeId);
    }

    /// <summary>
    /// Hinweis-Mail an einen OneCrew-Benutzer, dass ein Dokument bei einem MA
    /// abgelegt wurde (Walter-Vorgabe 04.08.2026). KEIN Anhang, KEIN Link —
    /// reiner Hinweis. Du-Form, da Empfänger ein interner Benutzer ist.
    /// </summary>
    public async Task<bool> SendDokumentNotificationAsync(
        string toEmail,
        string? toName,
        string actorName,
        string employeeDisplayName,
        string employeeNumber,
        string? kategorieName,
        string? typName,
        string? dateiName,
        string? bemerkung,
        string? persoenlicheNachricht)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) return false;

        // ANONYMISIERUNG (Walter-Vorgabe 04.08.2026): E-Mail ist ein externer
        // Kanal — KEINE Personendaten des MA. Nur die Personalnummer; kein
        // Name, kein Dateiname, keine Dokument-Bemerkung. employeeDisplayName
        // wird bewusst NICHT verwendet (Parameter bleibt für Log/Zukunft).
        _ = employeeDisplayName;
        _ = dateiName;
        _ = bemerkung;
        var maLabel = string.IsNullOrWhiteSpace(employeeNumber)
            ? "einem Mitarbeiter"
            : $"MA {employeeNumber}";
        var subject = string.IsNullOrWhiteSpace(employeeNumber)
            ? "Neues Dokument"
            : $"Neues Dokument für {employeeNumber}";

        var katTyp = string.Join(" → ", new[] { kategorieName, typName }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        string Enc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

        // Detail-Zeilen (nur gefüllte Felder zeigen)
        var htmlRows = "";
        var textRows = "";
        void AddRow(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            htmlRows += $@"<tr>
              <td style=""padding:4px 12px 4px 0;color:#94a3b8;font-size:12.5px;white-space:nowrap;vertical-align:top"">{Enc(label)}</td>
              <td style=""padding:4px 0;color:#0f172a;font-size:13px"">{Enc(value)}</td>
            </tr>";
            textRows += $"{label}: {value}\n";
        }
        // Nur der Ablageort — Dateiname/Bemerkung koennten Namen enthalten.
        AddRow("Abgelegt unter", katTyp);

        var nachrichtHtml = string.IsNullOrWhiteSpace(persoenlicheNachricht) ? "" : $@"
      <div style=""margin:18px 0 0;padding:12px 16px;background:#f8fafc;border-left:3px solid #94a3b8;border-radius:8px;color:#334155;font-size:13.5px;line-height:1.5;white-space:pre-line"">{Enc(persoenlicheNachricht)}</div>";

        // Per-Du-Kultur (Walter 04.08.2026): Anrede nur mit dem Vornamen.
        var vorname = string.IsNullOrWhiteSpace(toName) ? "" : toName.Trim().Split(' ')[0];
        var anrede = string.IsNullOrWhiteSpace(vorname) ? "Hallo" : $"Hallo {vorname}";

        var inhalt = $@"      <p style=""font-size:15px;margin:0 0 14px"">{Enc(anrede)},</p>
      <p style=""font-size:14px;line-height:1.55;margin:0 0 14px"">
        <strong>{Enc(actorName)}</strong> hat dir ein Dokument bei
        <strong>{Enc(maLabel)}</strong> abgelegt.
      </p>
      <table style=""border-collapse:collapse;margin:6px 0 0"">{htmlRows}</table>{nachrichtHtml}
      <p style=""font-size:12.5px;color:#64748b;line-height:1.5;margin:18px 0 0"">
        Du findest das Dokument in OneCrew im Dokumente-Tab des Mitarbeiters.
        Aus Datenschutzgründen ist kein Anhang beigefügt.
      </p>";

        var html = HtmlRahmen("Neues Dokument", inhalt);

        var textNachricht = string.IsNullOrWhiteSpace(persoenlicheNachricht)
            ? ""
            : $"\nNachricht:\n{persoenlicheNachricht}\n";

        var text = $@"{anrede},

{actorName} hat dir ein Dokument bei {maLabel} abgelegt.

{textRows}{textNachricht}
Du findest das Dokument in OneCrew im Dokumente-Tab des Mitarbeiters.
Aus Datenschutzgründen ist kein Anhang beigefügt.

—
OneCrew (automatisch versendet — bitte nicht antworten)";

        // Interne Mitteilung an einen OneCrew-Benutzer.
        return await SendAsync(toEmail, toName, subject, html, text, VersandKategorie.Intern);
    }
}

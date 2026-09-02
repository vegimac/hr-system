using HrSystem.Data;
using HrSystem.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using Npgsql;

namespace HrSystem.Services;

/// <summary>
/// Holt die Rückläufer aus dem bounce@-Postfach und legt sie als
/// <see cref="MailBounce"/> ab (Walter-Vorgabe 01.09.2026).
///
/// Warum überhaupt: Beim ersten Massenversand an alle MA kamen zwei
/// Zustellmeldungen zurück, die im HR-Postfach landeten und von Hand
/// gedeutet werden mussten. Bei 300 Empfängern geht das unter — und
/// niemand merkt, dass eine Adresse seit Monaten tot ist.
///
/// Wie ein Rückläufer aussieht: Der fremde Server schickt eine Mail vom
/// Typ <c>multipart/report; report-type=delivery-status</c>. Darin steckt
/// ein maschinenlesbarer Teil (<c>message/delivery-status</c>) mit den
/// Feldern <c>Final-Recipient</c>, <c>Status</c> und <c>Diagnostic-Code</c>.
/// Genau die lesen wir aus — nicht den Fliesstext, der ist bei jedem
/// Anbieter anders formuliert und oft englisch.
///
/// Fällt dieser Teil weg (ältere oder schlampige Server), greift ein
/// Textmuster als Rückfallebene. Findet auch das nichts, bleibt die Mail
/// im Postfach ungelesen liegen, statt still verworfen zu werden — dann
/// kann ein Mensch nachschauen.
/// </summary>
public class BounceAbrufService
{
    private readonly AppDbContext _db;
    private readonly SimpleAesService _aes;
    private readonly ILogger<BounceAbrufService> _log;

    public BounceAbrufService(AppDbContext db, SimpleAesService aes, ILogger<BounceAbrufService> log)
    {
        _db  = db;
        _aes = aes;
        _log = log;
    }

    public record AbrufErgebnis(int Geprueft, int Erfasst, int Uebersprungen, int Unklar, string? Fehler)
    {
        public static AbrufErgebnis Leer(string fehler) => new(0, 0, 0, 0, fehler);
    }

    /// <summary>Was beim Speichern eines einzelnen Rückläufers herauskam.</summary>
    private enum Schreibergebnis { Erfasst, SchonBekannt, Fehler }

    /// <summary>
    /// Verbindet sich mit dem Postfach und verarbeitet alle ungelesenen
    /// Nachrichten. Verarbeitete werden als gelesen markiert — das ist die
    /// Merkhilfe, damit derselbe Rückläufer nicht bei jedem Lauf erneut
    /// durch die Auswertung geht. Zusätzlich verhindert ein eindeutiger
    /// Index auf Message-ID + Adresse Dubletten in der Datenbank.
    /// </summary>
    /// <param name="auchGelesene">
    /// true = auch bereits gelesene Nachrichten der letzten 14 Tage prüfen.
    /// Für den Fall, dass ein Lauf eine Mail zwar als gelesen markiert, aber
    /// nicht gespeichert hat — dann findet der normale Abruf sie nie wieder.
    /// Dubletten sind ausgeschlossen: es wird über die Message-ID geprüft.
    /// </param>
    public async Task<AbrufErgebnis> AbrufenAsync(CancellationToken ct = default, bool auchGelesene = false)
    {
        var cfg = await _db.SmtpSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (cfg == null || string.IsNullOrWhiteSpace(cfg.BounceImapHost) || string.IsNullOrWhiteSpace(cfg.BounceImapUser))
            return AbrufErgebnis.Leer(
                "Kein Rückläufer-Postfach hinterlegt. Falls die Felder oben ausgefüllt sind: "
                + "zuerst auf «Speichern» klicken — der Abruf liest die gespeicherten Werte, "
                + "nicht das Formular.");

        var pw = "";
        try { pw = _aes.Decrypt(cfg.BounceImapPasswordEncrypted ?? ""); }
        catch { return AbrufErgebnis.Leer("Passwort des Rückläufer-Postfachs kann nicht entschlüsselt werden."); }
        if (string.IsNullOrEmpty(pw))
            return AbrufErgebnis.Leer("Kein Passwort für das Rückläufer-Postfach hinterlegt.");

        int geprueft = 0, erfasst = 0, doppelt = 0, unklar = 0;
        string? letzterSchreibFehler = null;

        using var client = new ImapClient();
        try
        {
            var secure = cfg.BounceImapPort == 993
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;
            await client.ConnectAsync(cfg.BounceImapHost, cfg.BounceImapPort, secure, ct);
            await client.AuthenticateAsync(cfg.BounceImapUser, pw, ct);

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, ct);

            var suche = auchGelesene
                ? SearchQuery.DeliveredAfter(DateTime.Today.AddDays(-14))
                : SearchQuery.NotSeen;
            var uids = await inbox.SearchAsync(suche, ct);
            foreach (var uid in uids)
            {
                if (ct.IsCancellationRequested) break;
                geprueft++;

                MimeMessage msg;
                try { msg = await inbox.GetMessageAsync(uid, ct); }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "[Bounce] Nachricht {Uid} konnte nicht geladen werden.", uid);
                    continue;
                }

                var treffer = Auswerten(msg);
                if (treffer == null)
                {
                    // Nichts erkannt: NICHT als gelesen markieren. Die Mail
                    // bleibt sichtbar, damit ein Mensch nachschauen kann —
                    // besser eine unbeantwortete Mail als eine still
                    // verschluckte Zustellmeldung.
                    unklar++;
                    continue;
                }

                treffer.QuellUid = Kuerzen(uid.ToString(), 60);
                var (erg, schreibFehler) = await ErfassenAsync(treffer, ct);

                if (erg == Schreibergebnis.Fehler)
                {
                    // Schreiben gescheitert: Mail NICHT als gelesen markieren
                    // und den Grund nach oben durchreichen. Vorher wurde das
                    // als «schon bekannt» gezählt — dann stand im Ergebnis
                    // «1 schon bekannt», während die Liste leer blieb
                    // (Walter-Bug 01.09.2026).
                    letzterSchreibFehler ??= schreibFehler;
                    continue;
                }

                if (erg == Schreibergebnis.Erfasst) erfasst++; else doppelt++;

                try { await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, ct); }
                catch (Exception ex) { _log.LogWarning(ex, "[Bounce] Konnte {Uid} nicht als gelesen markieren.", uid); }
            }

            await client.DisconnectAsync(true, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[Bounce] Abruf fehlgeschlagen.");
            return new AbrufErgebnis(geprueft, erfasst, doppelt, unklar, ex.Message);
        }

        // Zeitstempel nur bei erfolgreichem Abruf — er ist die Kontrolle in
        // der Maske, ob die Verbindung wirklich noch steht.
        try
        {
            var live = await _db.SmtpSettings.FirstOrDefaultAsync(x => x.Id == 1, ct);
            if (live != null) { live.BounceLetzterAbruf = DateTime.Now; await _db.SaveChangesAsync(ct); }
        }
        catch (Exception ex) { _log.LogWarning(ex, "[Bounce] Zeitstempel konnte nicht geschrieben werden."); }

        _log.LogInformation("[Bounce] {Geprueft} geprüft, {Erfasst} erfasst, {Doppelt} doppelt, {Unklar} unklar.",
                            geprueft, erfasst, doppelt, unklar);
        return new AbrufErgebnis(geprueft, erfasst, doppelt, unklar,
            letzterSchreibFehler == null ? null : "Speichern fehlgeschlagen: " + letzterSchreibFehler);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Auswertung einer einzelnen Rückläufer-Mail
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Liest Adresse, Status-Code und Originaltext aus der Zustellmeldung.
    /// Gibt null zurück, wenn die Mail gar kein Rückläufer ist (z.B. eine
    /// Abwesenheitsmeldung oder Werbung, die im Postfach gelandet ist).
    /// </summary>
    private MailBounce? Auswerten(MimeMessage msg)
    {
        string? adresse = null, code = null, meldung = null;

        // ── Weg 1: der maschinenlesbare Teil ───────────────────────────
        var ds = msg.BodyParts.OfType<MessageDeliveryStatus>().FirstOrDefault();
        if (ds != null)
        {
            // StatusGroups[0] ist der Kopf über die ganze Meldung,
            // ab [1] folgt je ein Block pro Empfänger. Wir nehmen den
            // ersten Empfänger-Block — Massenmails gehen als Einzelmails
            // raus, es gibt also nie mehr als einen.
            foreach (var gruppe in ds.StatusGroups.Skip(1))
            {
                var fr = gruppe["Final-Recipient"] ?? gruppe["Original-Recipient"];
                if (!string.IsNullOrWhiteSpace(fr))
                {
                    // Format: «rfc822; name@domain.tld»
                    var teil = fr.Split(';');
                    adresse = (teil.Length > 1 ? teil[1] : teil[0]).Trim().Trim('<', '>');
                }
                code    = gruppe["Status"]?.Trim();
                meldung = gruppe["Diagnostic-Code"]?.Trim();
                if (!string.IsNullOrWhiteSpace(adresse)) break;
            }
        }

        // ── Weg 2: Rückfall auf den Text ───────────────────────────────
        if (string.IsNullOrWhiteSpace(adresse) || string.IsNullOrWhiteSpace(code))
        {
            var text = msg.TextBody ?? msg.HtmlBody ?? "";
            if (string.IsNullOrWhiteSpace(adresse))
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    text, @"(?:RCPT TO:\s*<|failed:\s*|<)([^\s<>@]+@[^\s<>]+?)>?[\s,;]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success) adresse = m.Groups[1].Value.Trim();
            }
            if (string.IsNullOrWhiteSpace(code))
            {
                var m = System.Text.RegularExpressions.Regex.Match(text, @"\b([45]\.\d{1,3}\.\d{1,3})\b");
                if (m.Success) code = m.Groups[1].Value;
            }
            if (string.IsNullOrWhiteSpace(meldung) && text.Length > 0)
                meldung = text.Length > 2000 ? text.Substring(0, 2000) : text;
        }

        if (string.IsNullOrWhiteSpace(adresse)) return null;
        if (!adresse.Contains('@')) return null;

        var (hart, grund) = Deuten(code, meldung);

        // Betreff und Message-ID der URSPRÜNGLICHEN Mail stecken im
        // angehängten Original (message/rfc822). Der Betreff des Rückläufers
        // selbst ist nur «Mail delivery failed» und sagt nichts.
        string? origBetreff = null, origMsgId = null;
        var original = msg.BodyParts.OfType<MessagePart>().FirstOrDefault();
        if (original?.Message != null)
        {
            origBetreff = original.Message.Subject;
            origMsgId   = original.Message.MessageId;
        }

        // JEDES Textfeld auf seine Spaltenlänge kürzen. Ohne das bricht der
        // Insert mit «value too long» ab — und der Rückläufer verschwindet
        // spurlos (Walter-Bug 01.09.2026: «1 schon bekannt», Liste leer).
        //
        // Der häufigste Übeltäter ist der Status: die Norm sieht «5.1.1» vor,
        // etliche Server schreiben aber «5.1.1 (bad destination mailbox
        // address)» hinein — das sprengt die 20 Zeichen der Spalte. Darum
        // schneiden wir den reinen Code heraus statt blind zu kürzen; alles
        // Weitere steht ohnehin in der Meldung.
        var codeKurz = code;
        if (!string.IsNullOrWhiteSpace(code))
        {
            var m = System.Text.RegularExpressions.Regex.Match(code, @"\b([245]\.\d{1,3}\.\d{1,3})\b");
            codeKurz = m.Success ? m.Groups[1].Value : Kuerzen(code.Trim(), 20);
        }

        return new MailBounce
        {
            EmpfangenAm       = msg.Date.LocalDateTime,
            Adresse           = Kuerzen(adresse.Trim().ToLowerInvariant(), 300)!,
            Hart              = hart,
            Code              = codeKurz,
            Grund             = Kuerzen(grund, 300) ?? "Zustellung fehlgeschlagen",
            Meldung           = Kuerzen(meldung, 4000),
            OriginalBetreff   = Kuerzen(origBetreff, 500),
            OriginalMessageId = Kuerzen(origMsgId, 300),
        };
    }

    /// <summary>
    /// Übersetzt den Status-Code in «hart oder weich» und einen Satz auf
    /// Deutsch. Die Codes sind in RFC 3463 genormt: die erste Ziffer sagt
    /// endgültig (5) oder vorübergehend (4), die beiden übrigen den Grund.
    ///
    /// Wichtige Ausnahme: 5.2.2 «Postfach voll» ist zwar formal endgültig,
    /// fachlich aber vorübergehend — morgen hat die Person vielleicht
    /// aufgeräumt. Genau dieser Fall kam beim ersten Versand vor
    /// (iCloud-Postfach von Stefania). Als hart zu werten hiesse, ihr nie
    /// wieder eine Mail zu schicken. Darum: weich.
    /// </summary>
    private static (bool Hart, string Grund) Deuten(string? code, string? meldung)
    {
        var c = (code ?? "").Trim();
        var m = (meldung ?? "").ToLowerInvariant();

        // Erst die Fälle, die am Code eindeutig sind.
        if (c.StartsWith("5.2.2")) return (false, "Postfach des Empfängers ist voll");
        if (c.StartsWith("4."))    return (false, "Vorübergehende Störung beim Empfänger-Server");

        if (c.StartsWith("5.1.1") || c.StartsWith("5.1.0") || c.StartsWith("5.1.6"))
            return (true, "Adresse existiert nicht");
        if (c.StartsWith("5.1.2")) return (true, "Domain der Adresse existiert nicht");
        if (c.StartsWith("5.1.3")) return (true, "Adresse ist formal ungültig");
        if (c.StartsWith("5.4.4")) return (true, "Domain der Adresse ist nicht erreichbar");
        if (c.StartsWith("5.7."))  return (false, "Vom Empfänger-Server abgewiesen (Spam-Verdacht oder Regel)");

        // Kein oder unbekannter Code: am Klartext festmachen. Die Formu-
        // lierungen unten sind die, die in der Praxis vorkommen.
        if (m.Contains("over quota") || m.Contains("mailbox full") || m.Contains("quota exceeded")
         || m.Contains("insufficient storage"))
            return (false, "Postfach des Empfängers ist voll");
        if (m.Contains("mailbox not found") || m.Contains("no such user") || m.Contains("user unknown")
         || m.Contains("does not exist") || m.Contains("recipient rejected") || m.Contains("unknown recipient"))
            return (true, "Adresse existiert nicht");
        if (m.Contains("spam") || m.Contains("blocked") || m.Contains("blacklist"))
            return (false, "Vom Empfänger-Server abgewiesen (Spam-Verdacht oder Regel)");

        // Im Zweifel WEICH. Eine Adresse fälschlich zu sperren ist der
        // teurere Fehler: der MA bekommt dann gar nichts mehr, und niemand
        // merkt warum.
        return (false, string.IsNullOrWhiteSpace(c) ? "Zustellung fehlgeschlagen" : $"Zustellung fehlgeschlagen ({c})");
    }

    /// <summary>
    /// Schreibt den Rückläufer und ordnet ihn einem MA zu. Liefert false,
    /// wenn es ihn schon gibt.
    /// </summary>
    private async Task<(Schreibergebnis Ergebnis, string? Fehler)> ErfassenAsync(MailBounce b, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(b.OriginalMessageId))
        {
            var schonDa = await _db.MailBounces.AsNoTracking().AnyAsync(
                x => x.OriginalMessageId == b.OriginalMessageId && x.Adresse == b.Adresse, ct);
            if (schonDa) return (Schreibergebnis.SchonBekannt, null);
        }

        // Zuordnung über die Adresse. Kleinschreibung auf beiden Seiten,
        // weil die Adresse im MA-Datensatz beliebig geschrieben sein kann
        // («Imajstorska@yahoo.com»).
        var adr = b.Adresse;
        b.EmployeeId = await _db.Employees
            .Where(e => e.Email != null && e.Email.ToLower() == adr)
            .Select(e => (int?)e.Id)
            .FirstOrDefaultAsync(ct);

        _db.MailBounces.Add(b);
        try
        {
            await _db.SaveChangesAsync(ct);
            return (Schreibergebnis.Erfasst, null);
        }
        catch (DbUpdateException ex)
        {
            // Der Kontext hängt am gescheiterten Eintrag — abhängen, sonst
            // schlägt auch jede weitere Nachricht dieses Laufs fehl.
            _db.Entry(b).State = EntityState.Detached;

            // NUR eine echte Schlüsselverletzung (Postgres 23505) heisst
            // «kennen wir schon». Alles andere ist ein echtes Problem und
            // darf nicht als Dublette getarnt werden — sonst meldet der
            // Abruf «1 schon bekannt», während die Liste leer bleibt.
            if (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
            {
                _log.LogDebug(ex, "[Bounce] Rückläufer war bereits erfasst: {Adresse}", b.Adresse);
                return (Schreibergebnis.SchonBekannt, null);
            }

            var text = ex.InnerException?.Message ?? ex.Message;
            _log.LogError(ex, "[Bounce] Rückläufer konnte nicht gespeichert werden: {Adresse}", b.Adresse);
            return (Schreibergebnis.Fehler, text);
        }
        catch (Exception ex)
        {
            _db.Entry(b).State = EntityState.Detached;
            _log.LogError(ex, "[Bounce] Rückläufer konnte nicht gespeichert werden: {Adresse}", b.Adresse);
            return (Schreibergebnis.Fehler, ex.Message);
        }
    }

    private static string? Kuerzen(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));
}

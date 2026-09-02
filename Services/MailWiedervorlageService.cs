using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Arbeitet die Wiedervorlage ab (Walter-Vorgabe 01.09.2026): Mails, die an
/// einem vorübergehenden Fehler gescheitert sind, werden gestaffelt erneut
/// versucht und — wenn es dann immer noch nicht klappt — als Pendenz gemeldet.
///
/// Warum gestaffelt und nicht «gleich nochmals»: Der Fehler, um den es geht,
/// ist eine Mengengrenze pro Stunde. Ein sofortiger zweiter Versuch liefe in
/// dieselbe Wand. 15 Minuten fangen die kurze Spitze am Ende eines Versands
/// ab, eine Stunde deckt das Stundenfenster von Hostfactory ab, und vier
/// Stunden sind die Reserve für eine längere Störung beim Empfänger-Server.
/// </summary>
public class MailWiedervorlageService
{
    /// <summary>
    /// Abstände zum jeweils vorherigen Fehlversuch, in Minuten. Die Länge
    /// bestimmt zugleich, wie oft wiederholt wird — danach wird aufgegeben.
    /// Bewusst als Konstante und nicht als Einstellung: eine falsch gesetzte
    /// Schraube fällt hier erst auf, wenn eine Mail nicht ankommt.
    /// </summary>
    public static readonly int[] StaffelungMinuten = { 15, 60, 240 };

    /// <summary>
    /// Wie viele Fälle ein Durchgang höchstens anfasst. Verhindert, dass ein
    /// grosser Rückstau in derselben Minute wieder gegen die Mengengrenze
    /// läuft, die ihn überhaupt erst erzeugt hat.
    /// </summary>
    private const int MaxProDurchgang = 40;

    /// <summary>Kurze Pause zwischen zwei Versuchen — aus demselben Grund.</summary>
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Nur EIN Durchgang gleichzeitig — prozessweit.
    ///
    /// Ohne diese Schleuse laden der Fünf-Minuten-Takt und der Knopf
    /// «Fällige jetzt abarbeiten» dieselben Zeilen (die Auswahl markiert
    /// nichts als «in Arbeit») und schicken dieselbe Mail zweimal an den
    /// Empfänger. Ein Durchgang dauert bis zu 40 × 2 Sekunden, die beiden
    /// überschneiden sich also mühelos.
    ///
    /// Prozessweit reicht: Test und Produktion sind getrennte Dienste mit
    /// getrennten Datenbanken, und je Instanz läuft genau ein Prozess.
    /// </summary>
    private static readonly SemaphoreSlim Schleuse = new(1, 1);

    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly ILogger<MailWiedervorlageService> _log;

    public MailWiedervorlageService(AppDbContext db, EmailService email,
                                    ILogger<MailWiedervorlageService> log)
    {
        _db = db;
        _email = email;
        _log = log;
    }

    public record Ergebnis(int Geprueft, int Gesendet, int Erneut, int Aufgegeben, string? Fehler = null);

    /// <summary>
    /// Alle fälligen Fälle abarbeiten. Ruft der Hintergrunddienst alle fünf
    /// Minuten; zusätzlich von Hand aus der Systemsteuerung auslösbar.
    /// </summary>
    public async Task<Ergebnis> FaelligeVerarbeitenAsync(CancellationToken ct = default)
    {
        // Ohne SMTP-Konfiguration gar nicht erst anfangen: sonst verbraucht
        // eine fehlende Einstellung die Versuche, ohne dass je eine Mail
        // unterwegs war.
        var cfg = await _email.GetEffectiveConfigAsync();
        if (string.IsNullOrWhiteSpace(cfg.Host) || string.IsNullOrWhiteSpace(cfg.FromAddress))
            return new Ergebnis(0, 0, 0, 0, "SMTP ist nicht konfiguriert — Wiedervorlage pausiert.");

        // Läuft schon einer? Dann NICHT mitlaufen — siehe Schleuse.
        if (!await Schleuse.WaitAsync(TimeSpan.FromSeconds(2), ct))
            return new Ergebnis(0, 0, 0, 0, "Ein Durchgang läuft bereits.");

        try
        {
            var jetzt = DateTime.Now;
            var faellig = await _db.MailWiedervorlagen
                .Where(w => w.Status == MailWiedervorlage.StatusOffen && w.NaechsterVersuch <= jetzt)
                .OrderBy(w => w.NaechsterVersuch)
                .Take(MaxProDurchgang)
                .ToListAsync(ct);

            int gesendet = 0, erneut = 0, aufgegeben = 0;

            foreach (var eintrag in faellig)
            {
                var stand = await EinenVersuchAsync(eintrag);

                // SOFORT speichern, nicht erst am Ende der Schleife: Zwischen
                // «der Server hat die Mail angenommen» und «das steht in der
                // Datenbank» darf so wenig wie möglich liegen. Bricht der
                // Durchgang danach ab — Abbruch, Deploy, Absturz — bliebe der
                // Eintrag sonst auf OFFEN und fällig stehen, und der nächste
                // Durchgang schickte dieselbe Mail ein zweites Mal an den
                // Empfänger. Ein Schreibvorgang je Mail ist der Preis dafür.
                try { await _db.SaveChangesAsync(CancellationToken.None); }
                catch (Exception ex)
                {
                    _log.LogError(ex, "[Wiedervorlage] Stand von Fall {Id} konnte nicht "
                                    + "gespeichert werden — Abbruch, damit die Mail nicht "
                                    + "erneut verschickt wird.", eintrag.Id);
                    break;
                }

                if (stand == MailWiedervorlage.StatusGesendet)        gesendet++;
                else if (stand == MailWiedervorlage.StatusAufgegeben) aufgegeben++;
                else                                                  erneut++;

                // Abbruch erst HIER prüfen, nach dem Speichern.
                if (ct.IsCancellationRequested) break;
                if (faellig.Count > 1)
                {
                    try { await Task.Delay(Pause, ct); }
                    catch (TaskCanceledException) { break; }
                }
            }

            if (faellig.Count > 0)
                _log.LogInformation("[Wiedervorlage] {Geprueft} fällig — {Gesendet} zugestellt, "
                                  + "{Erneut} erneut eingeplant, {Aufgegeben} aufgegeben.",
                                    faellig.Count, gesendet, erneut, aufgegeben);

            return new Ergebnis(faellig.Count, gesendet, erneut, aufgegeben);
        }
        finally { Schleuse.Release(); }
    }

    /// <summary>
    /// Einen bestimmten Fall sofort versuchen — der Knopf «jetzt versuchen»
    /// in der Systemsteuerung. Funktioniert auch für aufgegebene Fälle: wenn
    /// jemand weiss, dass die Störung vorbei ist, soll er nicht warten müssen.
    /// </summary>
    public async Task<(bool Gefunden, string? Status, string? Fehler)> JetztVersuchenAsync(int id)
    {
        var eintrag = await _db.MailWiedervorlagen.FirstOrDefaultAsync(w => w.Id == id);
        if (eintrag == null) return (false, null, null);

        if (eintrag.Status == MailWiedervorlage.StatusGesendet)
            return (true, eintrag.Status, "Diese Mail ist bereits zugestellt.");
        if (eintrag.Mime == null || eintrag.Mime.Length == 0)
            return (true, eintrag.Status, "Die gespeicherte Nachricht wurde bereits verworfen.");

        // Derselbe Konfig-Riegel wie oben: sonst verbrauchen drei Klicks bei
        // fehlender SMTP-Konfiguration alle Versuche, ohne dass je eine
        // Verbindung aufgebaut wurde.
        var cfg = await _email.GetEffectiveConfigAsync();
        if (string.IsNullOrWhiteSpace(cfg.Host) || string.IsNullOrWhiteSpace(cfg.FromAddress))
            return (true, eintrag.Status, "SMTP ist nicht konfiguriert.");

        // Nicht gleichzeitig mit einem laufenden Durchgang — sonst geht
        // dieselbe Mail zweimal raus.
        if (!await Schleuse.WaitAsync(TimeSpan.FromSeconds(2)))
            return (true, eintrag.Status, "Ein Durchgang läuft gerade — bitte gleich nochmals.");

        try
        {
            var stand = await EinenVersuchAsync(eintrag);
            await _db.SaveChangesAsync(CancellationToken.None);
            return (true, stand, stand == MailWiedervorlage.StatusGesendet ? null : eintrag.LetzterFehler);
        }
        finally { Schleuse.Release(); }
    }

    /// <summary>
    /// Einen Fall genau einmal versuchen und seinen neuen Zustand setzen.
    /// Speichert NICHT — der Aufrufer entscheidet, wann geschrieben wird.
    /// </summary>
    private async Task<string> EinenVersuchAsync(MailWiedervorlage eintrag)
    {
        EmailService.SmtpVersuch versuch;
        try
        {
            versuch = await _email.WiedervorlageUebermittelnAsync(eintrag);
        }
        catch (Exception ex)
        {
            // Sicherheitsnetz: der Dienst darf an einem einzelnen Fall nicht
            // hängenbleiben, sonst kommen die dahinter nie an die Reihe.
            versuch = new EmailService.SmtpVersuch(false, true, ex.Message, null, ex);
        }

        if (versuch.Ok)
        {
            eintrag.Status          = MailWiedervorlage.StatusGesendet;
            eintrag.AbgeschlossenAm = DateTime.Now;
            eintrag.LetzterFehler   = null;
            eintrag.LetzterCode     = null;
            // Die Kopie wird nicht mehr gebraucht — bei einem Massenversand
            // mit Anhang läge sonst je Empfänger eine ganze Mail in der
            // Datenbank, für immer.
            eintrag.Mime            = Array.Empty<byte>();

            await GruppenZaehlerHochAsync(eintrag.GruppenMailLogId);

            _log.LogInformation("[Wiedervorlage] Mail an {To} beim {Nr}. Versuch doch noch zugestellt.",
                                eintrag.EffektiveAdresse, eintrag.Versuche + 2);
            return eintrag.Status;
        }

        eintrag.LetzterFehler = versuch.Fehler;
        eintrag.LetzterCode   = versuch.Code;
        eintrag.Versuche++;

        // Unterwegs endgültig geworden (z.B. die Adresse wurde inzwischen
        // gesperrt, oder der Server antwortet jetzt «existiert nicht»):
        // dann hat Weiterprobieren keinen Zweck mehr.
        var endgueltig = !versuch.Voruebergehend;
        var verbraucht = eintrag.Versuche >= StaffelungMinuten.Length;

        if (endgueltig || verbraucht)
        {
            eintrag.Status          = MailWiedervorlage.StatusAufgegeben;
            eintrag.AbgeschlossenAm = DateTime.Now;
            _log.LogWarning("[Wiedervorlage] Mail an {To} nach {N} Versuchen aufgegeben ({Grund}): {Fehler}",
                            eintrag.EffektiveAdresse, eintrag.Versuche,
                            endgueltig ? "endgültiger Fehler" : "alle Versuche verbraucht",
                            versuch.Fehler);
            return eintrag.Status;
        }

        eintrag.NaechsterVersuch = DateTime.Now.AddMinutes(StaffelungMinuten[eintrag.Versuche]);
        eintrag.Status           = MailWiedervorlage.StatusOffen;
        return eintrag.Status;
    }

    /// <summary>
    /// «5 fehlgeschlagen» im Gruppen-Protokoll stehen lassen, aber daneben
    /// mitzählen, wie viele davon später doch ankamen.
    /// </summary>
    private async Task GruppenZaehlerHochAsync(int? gruppenMailLogId)
    {
        if (gruppenMailLogId == null) return;
        try
        {
            var kopf = await _db.GruppenMailLogs.FirstOrDefaultAsync(g => g.Id == gruppenMailLogId);
            if (kopf != null) kopf.AnzahlSpaeterZugestellt++;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[Wiedervorlage] Zähler im Gruppen-Protokoll nicht nachgeführt.");
        }
    }

    /// <summary>
    /// Zugestellte Fälle nach 90 Tagen wegräumen. Aufgegebene bleiben, bis
    /// sie jemand abhakt — sie sind die Pendenz.
    /// </summary>
    public async Task<int> AufraeumenAsync(CancellationToken ct = default)
    {
        var grenze = DateTime.Now.AddDays(-90);
        return await _db.MailWiedervorlagen
            .Where(w => w.Status == MailWiedervorlage.StatusGesendet
                     && w.AbgeschlossenAm != null && w.AbgeschlossenAm < grenze)
            .ExecuteDeleteAsync(ct);
    }
}

using System.Text.RegularExpressions;

namespace HrSystem.Services.Elm;

/// <summary>
/// Lohnmeldungen für den Benutzer (Walter 15.10.2026, Swissdec-Zertifizierung: «eine fertige
/// Kommunikations-Schnittstelle»): Stand je Empfänger in Alltagssprache, Ampel pro Meldung,
/// wann OneCrew selbst nachfragt und was der Benutzer tun muss. Rein, ohne DB/Netz.
/// </summary>
public static class ElmMeldungAnzeige
{
    public const string Ok = "ok", Warn = "warn", Rot = "rot", Laeuft = "laeuft", Grau = "grau";

    /// <summary>Statusabfrage, solange der Job nicht fertig ist — automatisch höchstens so oft.</summary>
    public static readonly TimeSpan AutoStatusAbstand = TimeSpan.FromMinutes(30);
    /// <summary>Synchronisieren offener Fälle (Quittungen, Rückfragen) — automatisch höchstens so oft.</summary>
    public static readonly TimeSpan AutoSyncAbstand = TimeSpan.FromMinutes(60);
    /// <summary>Liegt eine empfangene Nachricht noch unquittiert da, schneller: der Empfänger wartet darauf.</summary>
    public static readonly TimeSpan AutoQuittungAbstand = TimeSpan.FromMinutes(10);
    /// <summary>Ältere Meldungen fragt die Automatik nicht mehr nach (nur noch per Knopf).</summary>
    public const int AutoHoechstensTage = 90;

    public record Stand(string Stufe, string Text, bool AktionNoetig);

    /// <summary>Fall ist bei diesem Empfänger erledigt — nichts mehr abzufragen.</summary>
    public static bool Erledigt(ElmVorgangAdressat a)
        => a.Zustand is "Error" or "Ignored"
           || a.State is "Finished" or "Rejected" or "closed"
           || a.FallUnbekannt || a.FallAbgebrochen;

    /// <summary>
    /// Quittung ist da, und danach kennt Swissdec den Job nicht mehr («JobState not found»): Der Fall ist
    /// erledigt, kein Fehler (Walter 05.11.2026, Quality Tool). Gilt auch für schon gespeicherte Fälle.
    /// </summary>
    public static bool FallNachQuittungWeg(ElmVorgangAdressat a, string? fehler)
        => a.Stories.Any(s => s.Art.Contains("Quittance", StringComparison.OrdinalIgnoreCase))
           && (fehler ?? "").Contains("JobState not found", StringComparison.OrdinalIgnoreCase);

    /// <summary>Der Empfänger hat den Fall wirklich mit einem Fehler beendet.</summary>
    public static bool Abgebrochen(ElmVorgangAdressat a) => a.FallAbgebrochen && !FallNachQuittungWeg(a, a.Fehler);

    public static Stand Empfaenger(ElmVorgangAdressat a)
    {
        if (a.FallAbgebrochen && !Abgebrochen(a)) return new(Ok, "Abgeschlossen", false);
        if (a.FallAbgebrochen)
            return new(Rot, "Fehler beim Synchronisieren" + (string.IsNullOrWhiteSpace(a.Fehler) ? "" : ": " + a.Fehler.Trim()), true);
        if (a.FallUnbekannt) return new(Warn, "Empfänger kennt diesen Fall nicht mehr, OneCrew fragt nicht weiter nach", false);
        switch (a.Zustand)
        {
            case "Error":
                return new(Rot, "Nicht angenommen" + (string.IsNullOrWhiteSpace(a.Fehler) ? "" : ": " + a.Fehler.Trim()), true);
            case "Ignored":
                return new(Grau, "Nicht gesendet (abgewählt)", false);
            case "Success":
                break;
            default:
                return new(Laeuft, "Swissdec verteilt die Meldung", false);
        }
        return a.State switch
        {
            "Finished" => new(Ok, "Abgeschlossen", false),
            "Accepted" => new(Ok, "Angenommen", false),
            "Rejected" => new(Rot, "Vom Empfänger abgelehnt", true),
            "DialogMessagePending" => OffeneRueckfrage(a)
                ? new(Warn, "Rückfrage vom Empfänger — bitte beantworten", true)
                : Haengt(a)
                    ? new(Warn, $"Empfänger geht nicht weiter, obwohl OneCrew die Mitteilung schon {Wiederholungen(a)}× bestätigt hat — beim Empfänger nachfragen", true)
                    : new(Laeuft, "Nachricht vom Empfänger erhalten — OneCrew bestätigt den Empfang selbst", false),
            "CompletionReleaseMissing" => new(Warn, "Freigabe beim Empfänger fehlt — «Beim Empfänger freigeben»", true),
            "Processing" => new(Laeuft, "In Bearbeitung beim Empfänger", false),
            null => new(Laeuft, "Angekommen, Ergebnis folgt", false),
            _ => new(Laeuft, a.State, false),
        };
    }

    /// <summary>
    /// Wartet eine Rückfrage mit Eingabefeldern auf Antwort? Nachrichten ohne Felder sind nur zur
    /// Kenntnis und werden quittiert (Anhang D.2.5). Unlesbares XML zählt als offen — lieber einmal
    /// zu viel nachsehen lassen.
    /// </summary>
    public static bool OffeneRueckfrage(ElmVorgangAdressat a)
        => a.Stories.Any(s => s.Art == "DialogMessage" && !s.Beantwortet && Beantwortbar(s.Xml));

    /// <summary>Ab so vielen Empfängen derselben Mitteilung gilt der Empfänger als hängend.</summary>
    public const int HaengtAbEmpfang = 3;

    /// <summary>
    /// Der Empfänger schickt dieselbe Mitteilung trotz Quittung immer wieder und bleibt auf DialogMessagePending
    /// (so verhält sich der Refapps-Testempfänger mit «Random Dialog»). Weiter bestätigen bringt nichts mehr.
    /// </summary>
    public static bool Haengt(ElmVorgangAdressat a)
        => a.State == "DialogMessagePending" && Wiederholungen(a) >= HaengtAbEmpfang;

    private static int Wiederholungen(ElmVorgangAdressat a)
        => a.Stories.Where(s => s.Art == "DialogMessage").Select(s => s.Empfangszaehler).DefaultIfEmpty(0).Max();

    private static bool Beantwortbar(string xml)
    {
        try { return ElmDialog.Lies(System.Xml.Linq.XElement.Parse(xml)).Beantwortbar; }
        catch { return true; }
    }

    /// <summary>
    /// Gesendet vor der Benutzerseite (ohne gespeicherte Adresse): Prüfpunkt-Fälle aus der Zertifizierung,
    /// oft mit absichtlich gestörtem Empfänger. Die Automatik fragt sie nicht nach — sie bleiben stehen.
    /// </summary>
    public static bool Zertifizierungstest(ElmVorgang v) => v.Url == null;

    /// <summary>
    /// Meldung ans Quality Tool, älter als eine halbe Stunde: das Tool wertet die Meldung beim Empfang aus,
    /// spätere Abfragen bringen nichts. Ohne diese Grenze fragte die Testinstanz nach 16 Monaten Lohntest
    /// rund um die Uhr nach und blockierte das Senden (Walter 04.11.2026, über 90'000 Abfragen).
    /// </summary>
    public static bool LohntestVorbei(ElmVorgang v, DateTime jetzt)
        => (v.Url ?? "").Contains("/qualitytool/", StringComparison.OrdinalIgnoreCase)
           && jetzt - v.Gesendet > TimeSpan.FromMinutes(30);

    /// <summary>Ampel einer Meldung: rot vor «Handlung nötig» vor «läuft» vor grün.</summary>
    public static Stand Meldung(ElmVorgang v)
    {
        var teile = v.Adressaten.Select(Empfaenger).ToList();
        if (teile.Count == 0) return new(Grau, "Keine Empfänger", false);
        if (Zertifizierungstest(v) && !(v.JobFinished && v.Adressaten.All(Erledigt)))
            return new(Grau, "Test aus der Zertifizierung — wird nicht mehr abgefragt", false);
        var rot = teile.Count(t => t.Stufe == Rot);
        if (rot > 0) return new(Rot, rot == 1 ? "1 Empfänger hat nicht angenommen" : $"{rot} Empfänger haben nicht angenommen", true);
        var warn = teile.Count(t => t.Stufe == Warn);
        if (warn > 0) return new(Warn, warn == 1 ? "1 Empfänger wartet auf Sie" : $"{warn} Empfänger warten auf Sie", true);
        if (teile.Any(t => t.Stufe == Laeuft)) return new(Laeuft, "In Bearbeitung", false);
        return v.Adressaten.Any(a => a.State == "Accepted") ? new(Ok, "Angenommen", false) : new(Ok, "Abgeschlossen", false);
    }

    /// <summary>
    /// Stand so, wie der Empfänger ihn liefert (System Tests 02_01 bis 02_07: der ReceivedState muss
    /// klar ersichtlich sein). Der Text von <see cref="Empfaenger"/> erklärt ihn in Alltagssprache.
    /// </summary>
    public static string Empfangsstatus(ElmVorgangAdressat a)
    {
        var code = string.IsNullOrWhiteSpace(a.FehlerCode) ? "" : ": " + a.FehlerCode.Trim();
        if (Abgebrochen(a)) return "SwissdecFault" + code;
        if (a.Zustand == "Error") return "Error" + code;
        if (a.Zustand != "Success") return a.Zustand == "offen" ? "Processing" : a.Zustand;
        return a.State != null ? "ReceivedState: " + a.State : "Success";
    }

    /// <summary>Was jetzt gilt und was der Benutzer tun kann. Null: nichts Besonderes.</summary>
    public static string? NaechsterSchritt(ElmVorgangAdressat a, DateTime jetzt)
    {
        const string fertig = "Der Prozess ist abgeschlossen. Der Fall kann nicht weiter bearbeitet werden.";
        const string korrigieren = " Daten korrigieren und die Meldung neu senden.";
        if (a.FallAbgebrochen && !Abgebrochen(a)) return fertig;
        if (a.FallAbgebrochen) return fertig + " Die Fehlermeldung prüfen, die Daten korrigieren und die Meldung neu senden. Bei Fragen den Empfänger kontaktieren.";
        if (a.FallUnbekannt) return fertig;
        if (!string.IsNullOrWhiteSpace(a.Wartung)) return $"Der Empfänger ist nicht verfügbar, geplante Wartung {a.Wartung.Trim()}. Danach geht es weiter.";
        if (a.Zustand == "Error") return "Die Meldung wurde von diesem Empfänger nicht angenommen." + korrigieren;
        if (a.Zustand != "Success") return null;
        return a.State switch
        {
            "Finished" => fertig,
            "Rejected" => fertig + korrigieren,
            "Accepted" => "Der Empfänger hat die Meldung angenommen. OneCrew fragt weiter nach, bis er den Fall abschliesst.",
            "CompletionReleaseMissing" when a.Completion == null => "Der Link zur Freigabe kommt mit der nächsten Abfrage.",
            "CompletionReleaseMissing" when a.Completion.Ablauf is DateTime b && b <= jetzt => "Der Freigabe-Link ist abgelaufen. Die Meldung neu senden.",
            _ => null,
        };
    }

    /// <summary>Warnung oder Information in einer Zeile, damit die Art sichtbar bleibt. Null: nichts anzuzeigen.</summary>
    public static string? HinweisText(ElmHinweis h)
    {
        var text = !string.IsNullOrWhiteSpace(h.Text) ? h.Text.Trim() : h.Code?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        var art = h.Art switch { "Warning" => "Warnung", "Error" => "Fehler", _ => "Information" };
        return !string.IsNullOrWhiteSpace(h.Text) && !string.IsNullOrWhiteSpace(h.Code)
            ? $"{art}: {text} ({h.Code.Trim()})" : $"{art}: {text}";
    }

    /// <summary>Text bei einer abgebrochenen Übermittlung: was passiert ist und wie es weitergeht.</summary>
    public static string AbbruchText(string grund, bool swissdecFault)
        => $"Übermittlung abgebrochen, die Meldung wurde nicht angenommen. {grund.Trim()} "
           + (swissdecFault ? "Bitte die Daten korrigieren und die Meldung neu senden." : "Bitte später nochmals senden.");

    /// <summary>Empfänger in Alltagssprache: Institution, sonst Zweig + Kennung.</summary>
    public static string EmpfaengerName(string? domain, string identification, string? institution)
    {
        if (!string.IsNullOrWhiteSpace(institution)) return institution.Trim();
        var id = (identification ?? "").Trim();
        return (domain ?? "") switch
        {
            "AHV-AVS" => $"AHV-Ausgleichskasse {id}",
            "TaxAtSource" => $"Quellensteuer Kanton {id}",
            "Statistic" => "Bundesamt für Statistik",
            "UVG-LAA" => $"Unfallversicherung {id}",
            "UVGZ-LAAC" => $"Unfall-Zusatzversicherung {id}",
            "KTG-AMC" => $"Krankentaggeld-Versicherung {id}",
            "BVG-LPP" => $"Pensionskasse {id}",
            "FAK-CAF" => $"Familienausgleichskasse {id}",
            "TaxCrossborder" => $"Grenzgänger Kanton {id}",
            _ => string.IsNullOrWhiteSpace(domain) ? id : $"{domain} {id}",
        };
    }

    /// <summary>Art einer empfangenen Nachricht in Alltagssprache.</summary>
    public static string NachrichtArt(string art)
    {
        if (art == "DialogMessage") return "Rückfrage";
        if (art.Contains("Completion", StringComparison.OrdinalIgnoreCase)) return "Freigabe-Aufforderung";
        if (art.Contains("Quittance", StringComparison.OrdinalIgnoreCase)) return "Quittung";
        if (art.Contains("Result", StringComparison.OrdinalIgnoreCase)) return "Ergebnis";
        if (art.EndsWith("-Profile", StringComparison.Ordinal)) return "Versicherungsprofil";
        return art;
    }

    /// <summary>Mitteilungsübersicht: success, pending oder error. Deutsch daneben über <see cref="CockpitStatusText"/>.</summary>
    public static string CockpitStatus(ElmVorgang v)
    {
        if (v.Adressaten.Any(a => a.Zustand == "Error" || Abgebrochen(a) || a.State == "Rejected")) return "error";
        if (!v.JobFinished || v.Adressaten.Any(a => a.Zustand is "offen" or "Processing")) return "pending";
        return "success";
    }

    public static string CockpitStatusText(string status) => status switch
    {
        "success" => "Erfolg",
        "error" => "Fehler",
        _ => "In Bearbeitung",
    };

    /// <summary>DeclarationID der Meldung (alle Empfänger eines Jobs teilen sie) — Grundlage der Ersatzmeldung.</summary>
    public static string? DeclarationId(ElmVorgang v)
        => v.Adressaten.FirstOrDefault(a => a.Zustand == "Success" && !string.IsNullOrWhiteSpace(a.FallId))?.FallId;

    /// <summary>Monats-, Jahres- und die drei Grenzgängermeldungen. Alles andere bleibt in der Entwicklung.</summary>
    public static bool IstAnwender(string? art)
        => art is "monthly" or "annual" or "grenz-it" or "grenz-fr" or "grenz-sfn" or "ema" or "prospective" or "rectificate";

    /// <summary>Grenzgänger steckt in DeclareAnnualSalary, nicht in einer eigenen SOAP-Operation.</summary>
    public static bool IstJahresSoap(string? art)
        => art is "annual" or "grenz-it" or "grenz-fr" or "grenz-sfn" or "prospective" or "rectificate";

    /// <summary>Unbekanntes wird Monatsmeldung. Die drei Grenzgänger-Arten bleiben erhalten.</summary>
    public static string NormalisiereArt(string? art)
        => art is "annual" or "grenz-it" or "grenz-fr" or "grenz-sfn" or "ema" or "prospective" or "rectificate" ? art : "monthly";

    public static string Titel(ElmVorgang v)
    {
        var (jahr, monat) = Periode(v);
        return v.Art switch
        {
            "annual" => $"Jahresmeldung {jahr}",
            "grenz-it" => $"Grenzgänger Italien {jahr}",
            "grenz-fr" => $"Grenzgänger Frankreich {jahr}",
            "grenz-sfn" => $"Grenzgänger SFN {jahr}",
            "ema" => $"EMA {monat:00}.{jahr}",
            "prospective" => $"Voraussichtliche BVG-Jahresmeldung {jahr}",
            "rectificate" => string.IsNullOrWhiteSpace(v.Titel) ? $"Rektifikat Lohnausweis {jahr}" : v.Titel,
            _ => monat != null ? $"Monatsmeldung {monat:00}.{jahr}" : (string.IsNullOrWhiteSpace(v.Titel) ? "Lohnmeldung" : v.Titel),
        };
    }

    /// <summary>
    /// Je Meldung (Art, Lohnjahr, Monat, Filiale) nur die neuste Übermittlung (Walter 02.11.2026):
    /// eine spätere Übermittlung oder Ersatzmeldung löst die früheren ab — To-dos und Aufgaben
    /// kommen nur von ihr, sonst steht dieselbe Meldung mehrfach auf der Liste.
    /// </summary>
    public static List<ElmVorgang> NeusteJeMeldung(IEnumerable<ElmVorgang> vorgaenge)
        => vorgaenge
            .GroupBy(v => { var (j, m) = Periode(v); return (v.Art, j, m, v.CompanyProfileId); })
            .Select(g => g.OrderByDescending(v => v.Gesendet).First())
            .ToList();

    /// <summary>Ersetzen geht, sobald der Job fertig ist und mindestens ein Empfänger angenommen hat.</summary>
    public static bool Ersetzbar(ElmVorgang v)
        => IstAnwender(v.Art) && v.Art is not ("ema" or "rectificate") && v.JobFinished && DeclarationId(v) != null;

    /// <summary>Lohnjahr und -monat: gespeichert, sonst aus dem Titel («Monatsmeldung 01.2025», «Jahresmeldung AHV 2025»).</summary>
    public static (int? Jahr, int? Monat) Periode(ElmVorgang v)
    {
        if (v.Jahr != null) return (v.Jahr, v.Art is "monthly" or "ema" ? v.Monat : null);
        var m = Regex.Match(v.Titel ?? "", @"Monatsmeldung (\d{2})\.(\d{4})");
        if (m.Success) return (int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value));
        var j = Regex.Match(v.Titel ?? "", @"Jahresmeldung \D*(\d{4})");
        if (j.Success) return (int.Parse(j.Groups[1].Value), null);
        return (null, null);
    }

    /// <summary>
    /// Soll die Automatik jetzt den Status abfragen? Nur Meldungen mit gespeicherter Adresse (ab 15.10.2026) —
    /// die älteren Prüfpunkt-Fälle aus der Zertifizierung fragt sie nicht nach.
    /// </summary>
    public static bool StatusFaellig(ElmVorgang v, DateTime jetzt)
        => !LohntestVorbei(v, jetzt) && IstAnwender(v.Art) && !v.JobFinished && !string.IsNullOrEmpty(v.JobKey)
           && v.Url != null && (jetzt - v.Gesendet).TotalDays <= AutoHoechstensTage
           && (v.LetzteStatusAbfrage is not DateTime t || jetzt - t >= AutoStatusAbstand);

    /// <summary>
    /// Hat dieser Empfänger einen Fall, der noch etwas zu holen oder zu quittieren hat? Die EMA kommt nach
    /// Schema 6.0 ohne Credentials aus, erst ELMv6.0.1 liefert sie (Patch Nr. 5).
    /// </summary>
    public static bool Synchronisierbar(ElmVorgang v, ElmVorgangAdressat a)
        => a.Zustand == "Success" && !string.IsNullOrEmpty(a.FallId) && !a.FallAbgebrochen && !a.FallUnbekannt
           && (v.Art == "ema" || (!string.IsNullOrEmpty(a.Key) && !string.IsNullOrEmpty(a.Password)))
           && (!Erledigt(a) || ElmUebermittlungXml.ZuQuittieren(a).Count > 0);

    /// <summary>Empfänger, bei denen die Automatik jetzt synchronisieren soll (Quittung/Rückfrage abholen).</summary>
    public static List<ElmVorgangAdressat> SyncFaellig(ElmVorgang v, DateTime jetzt)
    {
        if (LohntestVorbei(v, jetzt) || !IstAnwender(v.Art) || !v.JobFinished || v.Url == null
            || (jetzt - v.Gesendet).TotalDays > AutoHoechstensTage)
            return new();
        return v.Adressaten.Where(a => Synchronisierbar(v, a)
                                       && (a.LetzteSynchronisierung is not DateTime t
                                           || jetzt - t >= (WartetAufQuittung(a) ? AutoQuittungAbstand : AutoSyncAbstand)))
            .ToList();
    }

    /// <summary>
    /// Der Empfänger wartet auf unsere Bestätigung: Nachricht noch unquittiert, oder quittiert, aber der
    /// Empfänger meldet weiter DialogMessagePending ohne offene Frage (er geht erst beim nächsten Abfragen weiter).
    /// </summary>
    private static bool WartetAufQuittung(ElmVorgangAdressat a)
        => !Haengt(a)
           && (ElmUebermittlungXml.ZuQuittieren(a).Count > 0
               || a.State == "DialogMessagePending" && !OffeneRueckfrage(a));
}

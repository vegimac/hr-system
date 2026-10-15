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
    /// <summary>Ältere Meldungen fragt die Automatik nicht mehr nach (nur noch per Knopf).</summary>
    public const int AutoHoechstensTage = 90;

    public record Stand(string Stufe, string Text, bool AktionNoetig);

    /// <summary>Fall ist bei diesem Empfänger erledigt — nichts mehr abzufragen.</summary>
    public static bool Erledigt(ElmVorgangAdressat a)
        => a.Zustand is "Error" or "Ignored"
           || a.State is "Finished" or "Rejected" or "closed";

    public static Stand Empfaenger(ElmVorgangAdressat a)
    {
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
            "DialogMessagePending" => new(Warn, "Rückfrage vom Empfänger — bitte beantworten", true),
            "CompletionReleaseMissing" => new(Warn, "Freigabe beim Empfänger fehlt — «Beim Empfänger freigeben»", true),
            "Processing" => new(Laeuft, "In Bearbeitung beim Empfänger", false),
            null => new(Laeuft, "Angekommen, Ergebnis folgt", false),
            _ => new(Laeuft, a.State, false),
        };
    }

    /// <summary>Ampel einer Meldung: rot vor «Handlung nötig» vor «läuft» vor grün.</summary>
    public static Stand Meldung(ElmVorgang v)
    {
        var teile = v.Adressaten.Select(Empfaenger).ToList();
        if (teile.Count == 0) return new(Grau, "Keine Empfänger", false);
        var rot = teile.Count(t => t.Stufe == Rot);
        if (rot > 0) return new(Rot, rot == 1 ? "1 Empfänger hat nicht angenommen" : $"{rot} Empfänger haben nicht angenommen", true);
        var warn = teile.Count(t => t.Stufe == Warn);
        if (warn > 0) return new(Warn, warn == 1 ? "1 Empfänger wartet auf Sie" : $"{warn} Empfänger warten auf Sie", true);
        if (teile.Any(t => t.Stufe == Laeuft)) return new(Laeuft, "In Bearbeitung", false);
        return new(Ok, "Abgeschlossen", false);
    }

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
        return art;
    }

    /// <summary>DeclarationID der Meldung (alle Empfänger eines Jobs teilen sie) — Grundlage der Ersatzmeldung.</summary>
    public static string? DeclarationId(ElmVorgang v)
        => v.Adressaten.FirstOrDefault(a => a.Zustand == "Success" && !string.IsNullOrWhiteSpace(a.FallId))?.FallId;

    /// <summary>Ersetzen geht, sobald der Job fertig ist und mindestens ein Empfänger angenommen hat.</summary>
    public static bool Ersetzbar(ElmVorgang v)
        => v.Art is "monthly" or "annual" && v.JobFinished && DeclarationId(v) != null;

    /// <summary>Lohnjahr und -monat: gespeichert, sonst aus dem Titel («Monatsmeldung 01.2025», «Jahresmeldung AHV 2025»).</summary>
    public static (int? Jahr, int? Monat) Periode(ElmVorgang v)
    {
        if (v.Jahr != null) return (v.Jahr, v.Art == "monthly" ? v.Monat : null);
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
        => v.Art is "monthly" or "annual" && !v.JobFinished && !string.IsNullOrEmpty(v.JobKey)
           && v.Url != null && (jetzt - v.Gesendet).TotalDays <= AutoHoechstensTage
           && (v.LetzteStatusAbfrage is not DateTime t || jetzt - t >= AutoStatusAbstand);

    /// <summary>Empfänger, bei denen die Automatik jetzt synchronisieren soll (Quittung/Rückfrage abholen).</summary>
    public static List<ElmVorgangAdressat> SyncFaellig(ElmVorgang v, DateTime jetzt)
    {
        if (v.Art is not ("monthly" or "annual") || !v.JobFinished || v.Url == null
            || (jetzt - v.Gesendet).TotalDays > AutoHoechstensTage)
            return new();
        return v.Adressaten.Where(a => a.Zustand == "Success" && !string.IsNullOrEmpty(a.FallId)
                                       && !string.IsNullOrEmpty(a.Key) && !string.IsNullOrEmpty(a.Password)
                                       && (!Erledigt(a) || ElmUebermittlungXml.ZuQuittieren(a).Count > 0)
                                       && (a.LetzteSynchronisierung is not DateTime t || jetzt - t >= AutoSyncAbstand))
            .ToList();
    }
}

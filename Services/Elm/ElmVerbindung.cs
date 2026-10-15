using System.Text.RegularExpressions;
using static HrSystem.Services.Elm.ElmTransmitterClient;

namespace HrSystem.Services.Elm;

/// <summary>
/// Bereich «Verbindung» auf der Seite Lohnmeldungen (Walter 15.10.2026): drei Ampeln für den
/// Benutzer — Swissdec erreichbar (Ping), Uhrzeit des Servers, sichere Übermittlung
/// (CheckInteroperability: unterschreiben, verschlüsseln, Testrechnung). Läuft NUR auf Knopfdruck
/// (Richtlinien: Ping nie automatisieren); der letzte Stand wird gemerkt und beim Öffnen gezeigt.
/// Die Fachbegriffe stehen im Detail, die Ampel selbst spricht Klartext.
/// </summary>
public static class ElmVerbindung
{
    public const string Ok = "ok";
    public const string Warn = "warn";
    public const string Rot = "rot";
    public const string Grau = "grau";

    /// <summary>Zwischen zwei Prüfungen — schützt die Testinfrastruktur vor Dauerklicks.</summary>
    public const int MindestabstandSekunden = 30;

    public static ElmVerbindungAmpel Erreichbar(ElmCallResult r)
    {
        if (r.Ok)
            return new(Ok, $"Swissdec antwortet ({r.DauerMs} ms).");
        if (r.HttpStatus == 0)
            return new(Rot, "Swissdec antwortet nicht.",
                "Internetverbindung des Servers prüfen. Hält es länger an, bei Swissdec nachfragen.",
                r.Error);
        return new(Rot, "Swissdec antwortet mit einem Fehler.",
            "Später nochmals prüfen. Hält es länger an, bei Swissdec nachfragen.",
            (r.FaultCode + " " + (r.FaultText ?? r.Error ?? "HTTP " + r.HttpStatus)).Trim());
    }

    /// <summary>
    /// Zeitabgleich aus der Antwort des Empfängers (Foundation F01_03: ab einer Minute Abweichung
    /// lehnt Swissdec ab). Der Ping liefert die Zeit; fehlt sie, zählt die Testnachricht.
    /// </summary>
    public static ElmVerbindungAmpel Uhrzeit(ElmCallResult ping, ElmCallResult? interop)
    {
        var diff = ping.DiffSekunden ?? interop?.DiffSekunden;
        if (diff == null)
            return ping.Ok
                ? new(Grau, "Nicht ermittelt — Swissdec hat keine Uhrzeit mitgeschickt.")
                : new(Grau, "Nicht geprüft, weil Swissdec nicht erreichbar ist.");
        var betrag = Math.Abs(diff.Value);
        var dauer = betrag < 1 ? "weniger als 1 Sekunde" : $"{betrag:0} Sekunde{(betrag >= 1.5 ? "n" : "")}";
        if (betrag <= ZeitToleranzSekunden)
            return new(Ok, $"Stimmt mit Swissdec überein (Abweichung {dauer}).");
        var richtung = diff.Value > 0 ? "vor" : "nach";
        return new(Rot, $"Die Uhr des Servers geht {dauer} {richtung} — Swissdec lehnt Meldungen ab einer Minute Abweichung ab.",
            "Zeitabgleich (NTP) auf dem Server prüfen lassen.");
    }

    /// <summary>
    /// Sichere Übermittlung: dieselbe Prüfung wie im Bereitschafts-Check
    /// (<see cref="ElmBereitschaft.Interop"/>), hier mit Klartext für den Benutzer.
    /// </summary>
    public static ElmVerbindungAmpel Sicher(ElmCallResult? r, bool zertifikatDa, bool suaDa, bool erreichbar)
    {
        if (!erreichbar)
            return new(Grau, "Nicht geprüft, weil Swissdec nicht erreichbar ist.");
        if (!zertifikatDa)
            return new(Rot, "Nicht geprüft — OneCrew hat kein Zertifikat zum Unterschreiben der Meldungen.",
                "Bitte dem Support melden: das Swissdec-Zertifikat der Software fehlt.");
        if (r == null)
            return new(Grau, "Nicht geprüft.");
        var befund = ElmBereitschaft.Interop(r, suaDa);
        return befund.Stufe switch
        {
            ElmBereitschaft.Ok => new(Ok, "Unterschrift, Verschlüsselung und Testrechnung stimmen.", null, befund.Text),
            ElmBereitschaft.Warn => new(Warn, "Die Testnachricht kam an, aber mit einem Hinweis.",
                "Bitte dem Support melden, falls es so bleibt.", befund.Text),
            _ => new(Rot, "Swissdec hat die Testnachricht nicht angenommen — Meldungen würden ebenfalls abgelehnt.",
                "Bitte dem Support melden.", befund.Text),
        };
    }

    /// <summary>Noch so viele Sekunden bis zur nächsten erlaubten Prüfung (0 = jetzt).</summary>
    public static int WartenSekunden(ElmVerbindungStand? letzter, DateTime jetzt)
        => letzter == null ? 0 : Math.Max(0, MindestabstandSekunden - (int)(jetzt - letzter.GeprueftAm).TotalSeconds);

    // ── Archiv-Kopie für den Download ─────────────────────────────────────────

    private static readonly Regex Zugangsdaten = new(
        @"<(?<p>[A-Za-z_][\w.\-]*:)?(?<n>Key|Password)(?<a>\s[^>]*)?>[^<]*</\k<p>?\k<n>\s*>",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Zugangsdaten der Freigabe (Key/Password) für den Download ausblenden — das Archiv auf dem
    /// Server bleibt unverändert. Die Meldung selbst enthält keine, sie bleibt Byte für Byte gleich.
    /// </summary>
    public static string OhneZugangsdaten(string xml)
        => Zugangsdaten.Replace(xml ?? "", m => $"<{m.Groups["p"].Value}{m.Groups["n"].Value}{m.Groups["a"].Value}>***</{m.Groups["p"].Value}{m.Groups["n"].Value}>");

    /// <summary>Protokoll-Schritt in Worten.</summary>
    public static string Schritt(string? schritt) => schritt switch
    {
        "declare" => "Gesendet",
        "status" => "Stand abgefragt",
        "sync" => "Nachrichten abgeholt",
        "subscribe" => "Angemeldet",
        _ => schritt ?? "",
    };
}

public class ElmVerbindungAmpel
{
    public ElmVerbindungAmpel() { }
    public ElmVerbindungAmpel(string stufe, string text, string? tipp = null, string? detail = null)
    {
        Stufe = stufe; Text = text; Tipp = tipp; Detail = detail;
    }
    public string Stufe { get; set; } = ElmVerbindung.Grau;
    public string Text { get; set; } = "";
    public string? Tipp { get; set; }
    /// <summary>Technische Fassung für den Support.</summary>
    public string? Detail { get; set; }
}

/// <summary>Letzte Verbindungsprüfung — gemerkt, damit die Ampeln beim Öffnen der Seite stehen.</summary>
public class ElmVerbindungStand
{
    public DateTime GeprueftAm { get; set; } = DateTime.Now;
    public string Ziel { get; set; } = "";
    public string? GeprueftVon { get; set; }
    public ElmVerbindungAmpel Erreichbar { get; set; } = new();
    public ElmVerbindungAmpel Uhrzeit { get; set; } = new();
    public ElmVerbindungAmpel Sicher { get; set; } = new();
}

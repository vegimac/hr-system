using System.Xml;

namespace HrSystem.Services.Elm;

/// <summary>
/// Foundation F04_02 — SignatureConfirmation prüfen (Walter 29.09.2026).
///
/// Sicherheitsrichtlinie Kap. 4.3.4 «Nichtabstreitbarkeit des Empfangs»: Der Empfänger
/// bestätigt eine verifizierte Nachricht mit einer signierten Antwort, die die Signatur
/// der ursprünglichen Nachricht enthält (WS-Security 1.1 <c>SignatureConfirmation</c>).
/// Belegt ist der Empfang erst, wenn
///  • jede unserer Signaturen (ERP, bei Doppelsignatur auch SUA) mit ihrem
///    <c>SignatureValue</c> in einer SignatureConfirmation zurückkommt und
///  • diese SignatureConfirmation von der Signatur der Antwort mit abgedeckt ist —
///    sonst könnte sie unterwegs eingefügt worden sein.
/// Ob die Signatur der Antwort selbst gültig ist, prüft <see cref="ElmWsSecurity"/>.
/// </summary>
public static class ElmSignaturBestaetigung
{
    public enum Stand
    {
        /// <summary>Alle eigenen Signaturen bestätigt und von der Antwort-Signatur abgedeckt.</summary>
        Bestaetigt,
        /// <summary>Anfrage war nicht signiert (Ping) — nichts zu bestätigen.</summary>
        NichtVerlangt,
        /// <summary>Anfrage signiert, Antwort ohne SignatureConfirmation.</summary>
        Fehlt,
        /// <summary>SignatureConfirmation vorhanden, Werte passen nicht zu unseren Signaturen.</summary>
        Abweichend,
        /// <summary>Werte passen, aber die Antwort-Signatur deckt die SignatureConfirmation nicht ab.</summary>
        NichtSigniert,
    }

    public record Ergebnis(Stand Stand, int Erwartet, int Gefunden, int Passend, string Meldung)
    {
        public bool Ok => Stand == Stand.Bestaetigt;
        /// <summary>Stand als Text fürs UI (das Enum geht sonst als Zahl über die Leitung).</summary>
        public string Art => Stand.ToString();
    }

    public static Ergebnis Pruefe(string? anfrageXml, string? antwortXml)
    {
        var anfrage = Lade(anfrageXml);
        var antwort = Lade(antwortXml);
        if (anfrage == null || antwort == null)
            return new Ergebnis(Stand.Fehlt, 0, 0, 0, "Anfrage oder Antwort nicht lesbar.");
        return Pruefe(anfrage, antwort);
    }

    public static Ergebnis Pruefe(XmlDocument anfrage, XmlDocument antwort)
    {
        var eigene = SignaturWerte(anfrage);
        var bestaetigungen = Bestaetigungen(antwort);

        if (eigene.Count == 0)
            return new Ergebnis(Stand.NichtVerlangt, 0, bestaetigungen.Count, 0,
                "Anfrage war nicht signiert — keine SignatureConfirmation nötig.");
        if (bestaetigungen.Count == 0)
            return new Ergebnis(Stand.Fehlt, eigene.Count, 0, 0,
                "Die Antwort enthält keine SignatureConfirmation — der Empfang unserer signierten Anfrage ist nicht bestätigt.");

        var werte = bestaetigungen.Select(b => b.Wert).ToHashSet(StringComparer.Ordinal);
        var passend = eigene.Count(werte.Contains);
        var fremde = bestaetigungen.Count(b => !eigene.Contains(b.Wert));
        if (passend < eigene.Count || fremde > 0)
            return new Ergebnis(Stand.Abweichend, eigene.Count, bestaetigungen.Count, passend,
                $"SignatureConfirmation passt nicht zu unserer Anfrage: {passend} von {eigene.Count} eigenen Signaturen bestätigt"
                + (fremde > 0 ? $", {fremde} fremde Wert(e)" : "") + ".");

        var abgedeckt = SignierteIds(antwort);
        var ungedeckt = bestaetigungen.Count(b => b.Id == null || !abgedeckt.Contains(b.Id));
        if (ungedeckt > 0)
            return new Ergebnis(Stand.NichtSigniert, eigene.Count, bestaetigungen.Count, passend,
                $"Die Werte passen, aber {ungedeckt} SignatureConfirmation ist nicht von der Signatur der Antwort abgedeckt.");

        var wer = eigene.Count == 2 ? "beide Signaturen (ERP + SUA)" : eigene.Count == 1 ? "unsere Signatur" : $"alle {eigene.Count} Signaturen";
        return new Ergebnis(Stand.Bestaetigt, eigene.Count, bestaetigungen.Count, passend,
            $"Empfang bestätigt: {wer} kommen als SignatureConfirmation zurück, von der Antwort mitsigniert.");
    }

    /// <summary>SignatureValue jeder Signatur im Security-Header, ohne Leerraum.</summary>
    public static List<string> SignaturWerte(XmlDocument doc) =>
        doc.GetElementsByTagName("SignatureValue", ElmWsSecurity.NsDs).Cast<XmlElement>()
            .Where(e => ImSecurityHeader(e))
            .Select(e => OhneLeerraum(e.InnerText))
            .Where(w => w.Length > 0)
            .ToList();

    /// <summary>SignatureConfirmation (WS-Security 1.1) mit Wert und wsu:Id.</summary>
    public static List<(string Wert, string? Id)> Bestaetigungen(XmlDocument doc) =>
        doc.GetElementsByTagName("*").Cast<XmlElement>()
            .Where(e => e.LocalName == "SignatureConfirmation")
            .Select(e =>
            {
                var id = e.GetAttribute("Id", ElmWsSecurity.NsWsu);
                return (OhneLeerraum(e.GetAttribute("Value")), string.IsNullOrEmpty(id) ? (string?)null : id);
            })
            .ToList();

    /// <summary>Kennungen, die eine Signatur der Antwort über «Reference URI="#…"» abdeckt.</summary>
    public static HashSet<string> SignierteIds(XmlDocument doc) =>
        doc.GetElementsByTagName("Reference", ElmWsSecurity.NsDs).Cast<XmlElement>()
            .Where(r => r.ParentNode is XmlElement { LocalName: "SignedInfo" })
            .Select(r => r.GetAttribute("URI"))
            .Where(u => u.StartsWith('#'))
            .Select(u => u[1..])
            .ToHashSet(StringComparer.Ordinal);

    private static bool ImSecurityHeader(XmlElement e)
    {
        for (var n = e.ParentNode; n != null; n = n.ParentNode)
            if (n is XmlElement { LocalName: "Security" } s && s.NamespaceURI == ElmWsSecurity.NsWsse) return true;
        return false;
    }

    private static string OhneLeerraum(string? s) =>
        new((s ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static XmlDocument? Lade(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        try
        {
            var d = new XmlDocument { PreserveWhitespace = true };
            d.LoadXml(xml);
            return d;
        }
        catch { return null; }
    }
}

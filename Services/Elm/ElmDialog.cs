using System.Globalization;
using System.Xml.Linq;

namespace HrSystem.Services.Elm;

/// <summary>
/// DialogMessages nach Transmitter-Richtlinien ELM 6.0, Anhang D (Foundation F08_04–08).
///
/// Lesen: alles, was dargestellt werden muss (Titel, Beschreibung, Abschnitte,
/// Absätze mit Wert oder Frage samt Default und «optional»).
/// Antworten: die empfangene Nachricht wird gespiegelt — bis auf Creation, StoryID
/// und Previous. Neu gesetzt werden eine eigene Creation/StoryID und
/// <c>Previous/ResponseStoryID</c> = StoryID der empfangenen Nachricht (im
/// Transmitter immer ResponseStoryID, D.2). In jede Frage kommt ein Value: die
/// Eingabe, sonst der Default. Fragen ohne «optional» brauchen einen Wert.
/// </summary>
public static class ElmDialog
{
    public const string SimpleMessage = "0000.0001.0001-001";
    public const string TaskWithDeadline = "0000.0001.0001-002";
    public const string NichtStandard = "notStandard";

    public record Abschnitt(string Id, string? Titel, string? Beschreibung);

    public record Absatz(
        short Id, string Label, string? AbschnittRef,
        string? WertTyp, string? Wert,
        string? FrageTyp, string? Default, string? Antwort, bool Optional, string? Problem)
    {
        public bool IstFrage => FrageTyp != null;
    }

    public record Nachricht(
        string StoryId, string? Erstellt, string StandardDialogId,
        string? VorherAnfrage, string? VorherAntwort,
        string? Titel, string? Beschreibung,
        List<Abschnitt> Abschnitte, List<Absatz> Absaetze)
    {
        /// <summary>Enthält Fragen ⇒ verlangt eine Antwort; sonst reine Anzeige (nur quittieren).</summary>
        public bool Beantwortbar => Absaetze.Any(a => a.IstFrage);

        public string Art => StandardDialogId switch
        {
            SimpleMessage => "SimpleMessage",
            TaskWithDeadline => "TaskWithDeadline",
            NichtStandard => "frei (notStandard)",
            _ => "Standard " + StandardDialogId,
        };
    }

    private static XElement? Kind(XElement e, string name)
        => e.Elements().FirstOrDefault(k => k.Name.LocalName == name);

    private static string? Text(XElement e, string name)
    {
        var k = Kind(e, name);
        return k == null ? null : k.Value.Trim();
    }

    public static Nachricht Lies(XElement dm)
    {
        var previous = Kind(dm, "Previous");
        var abschnitte = dm.Elements().Where(e => e.Name.LocalName == "Section")
            .Select(s => new Abschnitt((string?)s.Attribute("sectionID") ?? "", Text(s, "Heading"), Text(s, "Description")))
            .ToList();
        var absaetze = new List<Absatz>();
        foreach (var p in dm.Elements().Where(e => e.Name.LocalName == "Paragraph"))
        {
            short.TryParse(Text(p, "ID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id);
            var label = Text(p, "Label") ?? "";
            var sektion = (string?)p.Attribute("sectionIDRef");
            string? wertTyp = null, wert = null, frageTyp = null, def = null, antwort = null, problem = null;
            var optional = false;
            var value = Kind(p, "Value");
            if (value?.Elements().FirstOrDefault() is XElement wv)
            {
                wertTyp = wv.Name.LocalName;
                wert = wv.Value.Trim();
            }
            var answer = Kind(p, "Answer");
            if (answer != null)
            {
                var typEl = answer.Elements().FirstOrDefault(e => e.Name.LocalName != "Problem");
                frageTyp = typEl?.Name.LocalName ?? "String";
                if (typEl != null)
                {
                    def = Text(typEl, "Default");
                    antwort = Text(typEl, "Value");
                }
                problem = Text(answer, "Problem");
                optional = string.Equals((string?)answer.Attribute("optional"), "true", StringComparison.OrdinalIgnoreCase)
                        || (string?)answer.Attribute("optional") == "1";
            }
            absaetze.Add(new Absatz(id, label, sektion, wertTyp, wert, frageTyp, def, antwort, optional, problem));
        }
        return new Nachricht(
            Text(dm, "StoryID") ?? "",
            Text(dm, "Creation"),
            Text(dm, "StandardDialogID") ?? NichtStandard,
            previous == null ? null : Text(previous, "RequestStoryID"),
            previous == null ? null : Text(previous, "ResponseStoryID"),
            Text(dm, "Title"),
            Text(dm, "Description"),
            abschnitte, absaetze);
    }

    /// <summary>
    /// Eingabe für einen Fragetyp prüfen und in die Schemaform bringen.
    /// Gibt eine Fehlermeldung zurück oder NULL, wenn der Wert passt.
    /// </summary>
    public static string? PruefeWert(string typ, string roh, out string normal)
    {
        var s = (roh ?? "").Trim();
        normal = s;
        switch (typ)
        {
            case "String":
                return null;
            case "Integer":
                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                { normal = l.ToString(CultureInfo.InvariantCulture); return null; }
                return "ganze Zahl erwartet";
            case "Double":
            case "Amount":
                var d = s.Replace("'", "").Replace("’", "").Replace(',', '.');
                if (!decimal.TryParse(d, NumberStyles.Number, CultureInfo.InvariantCulture, out var m))
                    return typ == "Amount" ? "Betrag erwartet (z.B. 1234.50)" : "Zahl erwartet";
                normal = typ == "Amount" ? m.ToString("0.00", CultureInfo.InvariantCulture)
                                         : m.ToString(CultureInfo.InvariantCulture);
                return null;
            case "Boolean":
                var b = s.ToLowerInvariant();
                if (b is "true" or "ja" or "1") { normal = "true"; return null; }
                if (b is "false" or "nein" or "0") { normal = "false"; return null; }
                return "ja oder nein erwartet";
            case "YesNoUnknown":
                var y = s.ToLowerInvariant();
                normal = y switch { "ja" => "yes", "nein" => "no", "unbekannt" => "unknown", _ => y };
                return normal is "yes" or "no" or "unknown" ? null : "ja, nein oder unbekannt erwartet";
            case "Date":
                // xs:date darf eine Zeitzone tragen («2026-09-30+02:00», «2026-09-30Z»).
                var zone = System.Text.RegularExpressions.Regex.Match(s, @"^(\d{4}-\d{2}-\d{2})(Z|[+-]\d{2}:\d{2})$");
                if (zone.Success) s = zone.Groups[1].Value;
                if (DateOnly.TryParseExact(s, new[] { "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                { normal = dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); return null; }
                return "Datum erwartet (TT.MM.JJJJ)";
            case "DateTime":
                if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
                { normal = dto.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture); return null; }
                if (DateTime.TryParseExact(s, new[] { "dd.MM.yyyy HH:mm", "dd.MM.yyyy HH:mm:ss" },
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dtl))
                { normal = new DateTimeOffset(dtl).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture); return null; }
                return "Datum und Zeit erwartet (TT.MM.JJJJ HH:MM)";
            default:
                return null;
        }
    }

    /// <summary>
    /// Antwort auf eine empfangene DialogMessage. <paramref name="eingaben"/> je
    /// Absatz-ID; leer ⇒ Default. Fehler (Pflichtfeld leer, Wert passt nicht zum
    /// Typ) landen in <paramref name="fehler"/> — dann nicht senden.
    /// </summary>
    public static XElement BaueAntwort(XElement empfangen, IReadOnlyDictionary<short, string?> eingaben,
        List<string> fehler, DateTime jetzt, string neueStoryId)
    {
        var alteStoryId = Text(empfangen, "StoryID") ?? "";
        var std = Kind(empfangen, "StandardDialogID");
        var ns = (std ?? empfangen.Elements().First()).Name.Namespace;

        var antwort = new XElement(empfangen.Name,
            new XElement(ns + "Creation", jetzt.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture)),
            new XElement(ns + "StoryID", neueStoryId));

        foreach (var k in empfangen.Elements())
        {
            switch (k.Name.LocalName)
            {
                case "Creation":
                case "StoryID":
                case "Previous":
                    continue;
                case "StandardDialogID":
                    antwort.Add(new XElement(k));
                    antwort.Add(new XElement(ns + "Previous", new XElement(ns + "ResponseStoryID", alteStoryId)));
                    continue;
                case "Paragraph":
                    antwort.Add(Beantworte(new XElement(k), eingaben, fehler));
                    continue;
                default:
                    antwort.Add(new XElement(k));
                    continue;
            }
        }
        return antwort;
    }

    private static XElement Beantworte(XElement p, IReadOnlyDictionary<short, string?> eingaben, List<string> fehler)
    {
        var answer = Kind(p, "Answer");
        if (answer == null) return p;
        var typEl = answer.Elements().FirstOrDefault(e => e.Name.LocalName != "Problem");
        if (typEl == null) return p;

        short.TryParse(Text(p, "ID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id);
        var label = Text(p, "Label") ?? $"Feld {id}";
        var optional = string.Equals((string?)answer.Attribute("optional"), "true", StringComparison.OrdinalIgnoreCase);
        var ns = typEl.Name.Namespace;

        eingaben.TryGetValue(id, out var eingabe);
        var wert = string.IsNullOrWhiteSpace(eingabe) ? Text(typEl, "Default") : eingabe;
        if (string.IsNullOrWhiteSpace(wert)) wert = Text(typEl, "Value");

        Kind(typEl, "Value")?.Remove();
        if (string.IsNullOrWhiteSpace(wert))
        {
            if (!optional) fehler.Add($"«{label}» ist ein Pflichtfeld.");
            return p;
        }
        var problem = PruefeWert(typEl.Name.LocalName, wert!, out var normal);
        if (problem != null)
        {
            fehler.Add($"«{label}»: {problem}.");
            return p;
        }
        // Value steht nach Default (Schema-Reihenfolge).
        typEl.Add(new XElement(ns + "Value", normal));
        return p;
    }
}

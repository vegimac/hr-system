using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HrSystem.Services.Vorsystem;

/// <summary>
/// Liest die Mirus-Auswertung «Lohnkonto» (Mitarbeiterbezogen, als Word gespeichert —
/// Mirus nennt die Datei .doc, sie ist aber ein Word-2007-Dokument). Excel bietet
/// Mirus an, liefert aber keine brauchbare Datei (Walter 04.10.2026).
///
/// Aufbau: pro Person ein Kopf (Name / Versicherten Nr. / Eintritt / Austritt,
/// Personal Nr., Geburtsdatum), dann Zeilen «Nr | Sub | Bezeichnung | Monat 1..n | Total».
/// Ab «Arbeitgeber Betrag» folgen AG-Beiträge, Tage und Saldi. Folgeseiten wiederholen
/// die Name-Zeile. Die Total-Seite der Filiale hat eine «Nr.»-Kopfzeile ohne Name-Zeile
/// auf derselben Seite.
/// </summary>
public static class MirusLohnkontoParser
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly Regex Zahl = new(@"^-?\d+(\.\d+)?$", RegexOptions.Compiled);
    private static readonly Regex NurZiffern = new(@"^\d+$", RegexOptions.Compiled);

    public sealed class Zeile
    {
        public string Sektion { get; init; } = "AN";     // AN = Lohnzettel, AG = Arbeitgeber/Tage/Saldi
        public string Nr { get; init; } = "";
        public string Sub { get; init; } = "";
        public string Bezeichnung { get; init; } = "";
        public List<(int Jahr, int Monat, decimal Betrag)> Werte { get; } = new();
        public decimal? Total { get; init; }
        public string Code => $"{Nr}.{Sub}";
    }

    public sealed class Person
    {
        public string Filiale { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Versichertennummer { get; set; }
        public string? Eintritt { get; set; }
        public string? Austritt { get; set; }
        public string? Personalnummer { get; set; }
        public string? Geburtsdatum { get; set; }
        public bool IstTotal { get; set; }
        public List<Zeile> Zeilen { get; } = new();
        internal HashSet<(string, string, string)> Schluessel { get; } = new();

        /// <summary>Eindeutiger Schlüssel innerhalb der Datei (Arbeitsverhältnis).</summary>
        public string Key => $"{Personalnummer}|{Eintritt}|{Name}";
    }

    public sealed class Ergebnis
    {
        public List<Person> Personen { get; } = new();
        public List<(int Jahr, int Monat)> Monate { get; } = new();
        public List<string> Warnungen { get; } = new();
        public string? RestaurantCode { get; set; }
    }

    public static Ergebnis Lies(Stream docx)
    {
        XDocument doc;
        using (var zip = new ZipArchive(docx, ZipArchiveMode.Read, leaveOpen: true))
        {
            var entry = zip.GetEntry("word/document.xml")
                ?? throw new InvalidDataException("Keine Word-Datei (word/document.xml fehlt). Bitte in Mirus als Word speichern.");
            using var s = entry.Open();
            doc = XDocument.Load(s);
        }

        var erg = new Ergebnis();
        Person? cur = null;
        string sektion = "AN", filiale = "";
        var monate = new List<(int, int)>();
        bool nameAufSeite = false;

        foreach (var tr in doc.Descendants(W + "tr"))
        {
            var cells = tr.Elements(W + "tc")
                .Select(tc => string.Concat(tc.Descendants(W + "t").Select(t => t.Value)).Trim())
                .ToList();
            var ne = cells.Where(c => c.Length > 0).ToList();
            if (ne.Count == 0) continue;

            if (ne[0] == "Name" && ne.Count >= 2)
            {
                nameAufSeite = true;
                var name = ne[1];
                string? Feld(string k)
                {
                    int i = ne.IndexOf(k);
                    if (i < 0) return null;
                    if (i + 1 < ne.Count && ne[i + 1] is not ("Eintritt" or "Austritt" or "Versicherten Nr.")) return ne[i + 1];
                    return "";
                }
                var eintritt = Feld("Eintritt");
                if (cur != null && !cur.IstTotal && cur.Name == name && cur.Eintritt == eintritt) continue; // Folgeseite
                cur = new Person
                {
                    Filiale = filiale, Name = name, Eintritt = eintritt,
                    Austritt = Feld("Austritt"), Versichertennummer = Feld("Versicherten Nr.")
                };
                erg.Personen.Add(cur);
                sektion = "AN";
                continue;
            }

            if (ne.Count >= 2 && ne.Contains("Lohnkonto"))
            {
                filiale = ne[0];
                nameAufSeite = false;
                erg.RestaurantCode ??= Regex.Match(filiale, @"^\d+").Value is { Length: > 0 } rc ? rc : null;
            }

            if (ne[0] == "Personal Nr." && ne.Count >= 2 && cur != null) cur.Personalnummer = ne[1];
            int gi = ne.IndexOf("Geburtsdatum");
            if (gi >= 0 && gi + 1 < ne.Count && cur != null) cur.Geburtsdatum = ne[gi + 1];

            if (ne[0] == "Nr." && ne.Count > 3)
            {
                var kopf = ne[^1] == "Total" ? ne.Skip(2).Take(ne.Count - 3) : ne.Skip(2);
                monate = kopf.Select(ParseMonat).ToList();
                foreach (var m in monate) if (!erg.Monate.Contains(m)) erg.Monate.Add(m);
                if (!nameAufSeite && (cur == null || !cur.IstTotal))
                {
                    cur = new Person { Filiale = filiale, Name = "Total Filiale", IstTotal = true };
                    erg.Personen.Add(cur);
                    sektion = "AN";
                }
                continue;
            }

            if (ne[0] == "Arbeitgeber Betrag") { sektion = "AG"; continue; }

            if (cur != null && ne.Count >= 3 && NurZiffern.IsMatch(ne[0]) && NurZiffern.IsMatch(ne[1]))
            {
                var werte = ne.Skip(3).Select(ParseZahl).ToList();
                if (werte.Any(v => v == null))
                {
                    erg.Warnungen.Add($"Zeile nicht lesbar bei {cur.Name}: {string.Join(" | ", ne)}");
                    continue;
                }
                var key = (sektion, ne[0], ne[1]);
                if (cur.Schluessel.Contains(key))
                {
                    // Gleiche Zeile zum zweiten Mal ohne neuen Namen = Total-Seite beginnt.
                    cur = new Person { Filiale = filiale, Name = "Total Filiale", IstTotal = true };
                    erg.Personen.Add(cur);
                    sektion = "AN";
                    key = (sektion, ne[0], ne[1]);
                }
                cur.Schluessel.Add(key);
                var z = new Zeile
                {
                    Sektion = sektion, Nr = ne[0], Sub = ne[1], Bezeichnung = ne[2],
                    Total = werte.Count > monate.Count ? werte[monate.Count] : null
                };
                for (int i = 0; i < monate.Count && i < werte.Count; i++)
                    z.Werte.Add((monate[i].Item1, monate[i].Item2, werte[i]!.Value));
                cur.Zeilen.Add(z);
            }
        }

        // Summen-Kontrolle pro Zeile (Monate = Total)
        foreach (var p in erg.Personen)
            foreach (var z in p.Zeilen.Where(z => z.Total.HasValue))
                if (Math.Abs(z.Werte.Sum(w => w.Betrag) - z.Total!.Value) > 0.005m)
                    erg.Warnungen.Add($"Summe stimmt nicht: {p.Name} {z.Code} {z.Bezeichnung}");
        return erg;
    }

    private static decimal? ParseZahl(string s)
    {
        s = s.Replace("'", "").Replace("’", "");
        return Zahl.IsMatch(s) ? decimal.Parse(s, CultureInfo.InvariantCulture) : null;
    }

    private static readonly string[] MonatsKuerzel = { "jan", "feb", "mär", "apr", "mai", "jun", "jul", "aug", "sep", "okt", "nov", "dez" };

    public static (int Jahr, int Monat) ParseMonat(string s)
    {
        var teile = s.Replace(".", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (teile.Length < 2 || !int.TryParse(teile[^1], out var jahr))
            throw new InvalidDataException($"Monat im Kopf nicht lesbar: «{s}»");
        var k = teile[0].ToLowerInvariant().Replace("mar", "mär");
        int idx = Array.FindIndex(MonatsKuerzel, m => k.StartsWith(m));
        if (idx < 0) throw new InvalidDataException($"Monat im Kopf nicht lesbar: «{s}»");
        return (jahr, idx + 1);
    }

    /// <summary>
    /// Werte einer Person pro (Sektion, Code, Jahr, Monat), Nullen weggelassen.
    /// Mehrere Arbeitsverhältnisse derselben Person werden vom Aufrufer zusammengezählt.
    /// </summary>
    public static IEnumerable<(string Sektion, string Code, string Bezeichnung, int Jahr, int Monat, decimal Betrag)> Werte(Person p)
        => p.Zeilen.SelectMany(z => z.Werte
            .Where(w => w.Betrag != 0)
            .Select(w => (z.Sektion, z.Code, z.Bezeichnung, w.Jahr, w.Monat, w.Betrag)));
}

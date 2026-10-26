using System.IO.Compression;
using System.Xml.Linq;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Italienische Grenzgemeinden (20-km-Zone) von der Seite der Eidgenössischen
/// Steuerverwaltung. Die Datei ist für Lohnprogramme veröffentlicht. Ort ist
/// die italienische Bezeichnung (Spalte Comune), damit sie zur schon
/// geladenen Liste passt. Region ist Provinz und Region, z.B. «Como, Lombardia».
/// </summary>
public static class GrenzregionEstv
{
    public const string SeitenUrl = "https://www.estv.admin.ch/de/quellensteuer";
    public const int Mindestzahl = 400;

    public readonly record struct Bestand(int Id, string Plz, string Ort, string Region);
    public readonly record struct Aktualisierung(int Id, string Ort, string Region);

    public sealed record Plan(
        IReadOnlyList<GrenzregionKatalog.Zeile> Neu,
        IReadOnlyList<Aktualisierung> Aktualisiert,
        IReadOnlyList<int> Entfernt);

    /// <summary>Die href-Adresse der XLSX-Datei auf der Quellensteuer-Seite.</summary>
    public static string FindeDateiUrl(string html)
    {
        if (string.IsNullOrEmpty(html))
            throw new InvalidOperationException("Die ESTV-Seite ist leer.");
        var marker = html.IndexOf("comuni-italiani", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            throw new InvalidOperationException(
                "Auf der ESTV-Seite fehlt die Liste der italienischen Grenzgemeinden.");
        var start = html.LastIndexOf("https://", marker, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException(
                "Auf der ESTV-Seite fehlt die Adresse der Grenzgemeinden-Datei.");
        var end = html.IndexOf(".xlsx", marker, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
            throw new InvalidOperationException(
                "Auf der ESTV-Seite fehlt die Excel-Datei der Grenzgemeinden.");
        return html.Substring(start, end + 5 - start);
    }

    public static List<GrenzregionKatalog.Zeile> LiesXlsx(byte[] datei)
    {
        using var stream = new MemoryStream(datei);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var ss = LiesSharedStrings(zip);
        var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")
            ?? zip.Entries.FirstOrDefault(e =>
                e.FullName.Replace('\\', '/').StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                && !e.FullName.Contains("_rels", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Die ESTV-Datei enthält kein Arbeitsblatt.");

        using var sheetStream = sheet.Open();
        var doc = XDocument.Load(sheetStream);
        var rows = doc.Descendants().Where(e => e.Name.LocalName == "row").ToList();

        int? cap = null, comune = null, provincia = null, regione = null;
        var headerIndex = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            foreach (var (col, wert) in Zellen(rows[i], ss))
            {
                if (!wert.Trim().Equals("CAP", StringComparison.OrdinalIgnoreCase)) continue;
                headerIndex = i;
                foreach (var (c, w) in Zellen(rows[i], ss))
                {
                    var name = w.Trim();
                    if (name.Equals("CAP", StringComparison.OrdinalIgnoreCase)) cap = c;
                    else if (name.Equals("Comune", StringComparison.OrdinalIgnoreCase)) comune = c;
                    else if (name.Equals("Provincia", StringComparison.OrdinalIgnoreCase)) provincia = c;
                    else if (name.Equals("Regione", StringComparison.OrdinalIgnoreCase)) regione = c;
                }
                break;
            }
            if (headerIndex >= 0) break;
        }
        if (cap is null || comune is null)
            throw new InvalidOperationException("Die ESTV-Datei hat keine Spalten CAP und Comune.");

        var list = new List<GrenzregionKatalog.Zeile>();
        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var map = Zellen(rows[i], ss).ToDictionary(z => z.Col, z => z.Wert.Trim());
            var plz = map.GetValueOrDefault(cap.Value) ?? "";
            var ort = map.GetValueOrDefault(comune.Value) ?? "";
            if (plz.Length < 3 || plz.Length > 10 || plz.Any(c => !char.IsDigit(c))) continue;
            if (ort.Length < 2) continue;
            var prov = provincia is int p ? map.GetValueOrDefault(p) ?? "" : "";
            var reg = regione is int g ? map.GetValueOrDefault(g) ?? "" : "";
            var region = RegionText(prov, reg);
            if (!gesehen.Add(plz + "|" + ort)) continue;
            list.Add(new GrenzregionKatalog.Zeile(plz, ort, region, "IT"));
        }
        if (list.Count == 0)
            throw new InvalidOperationException("Die ESTV-Datei enthält keine Gemeinden.");
        return list;
    }

    /// <summary>
    /// Italienische Zeilen an die neue Liste angleichen. Andere Länder bleiben.
    /// Unter <see cref="Mindestzahl"/> wird nichts verändert.
    /// </summary>
    public static Plan Planen(IReadOnlyList<Bestand> vorhanden, IReadOnlyList<GrenzregionKatalog.Zeile> liste)
    {
        if (liste.Count < Mindestzahl)
            throw new InvalidOperationException(
                $"Die ESTV-Datei enthält nur {liste.Count} Gemeinden. Erwartet sind mindestens {Mindestzahl}. Die bestehende Liste bleibt.");

        var soll = new Dictionary<string, GrenzregionKatalog.Zeile>(StringComparer.OrdinalIgnoreCase);
        foreach (var z in liste)
            soll.TryAdd(Schluessel(z.Plz, z.Ort), z);

        var neu = new List<GrenzregionKatalog.Zeile>();
        var aktualisiert = new List<Aktualisierung>();
        var entfernt = new List<int>();
        var getroffen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var alt in vorhanden)
        {
            var key = Schluessel(alt.Plz, alt.Ort);
            if (!soll.TryGetValue(key, out var z))
            {
                entfernt.Add(alt.Id);
                continue;
            }
            getroffen.Add(key);
            if (alt.Ort != z.Ort || alt.Region != z.Region)
                aktualisiert.Add(new Aktualisierung(alt.Id, z.Ort, z.Region));
        }
        foreach (var (key, z) in soll)
        {
            if (!getroffen.Contains(key))
                neu.Add(z);
        }
        return new Plan(neu, aktualisiert, entfernt);
    }

    public static async Task<(int Neu, int Aktualisiert, int Entfernt, int Anzahl)> AnwendenAsync(
        AppDbContext db, IReadOnlyList<GrenzregionKatalog.Zeile> liste, CancellationToken ct)
    {
        var rows = await db.Grenzregionen.Where(g => g.LandIso == "IT").ToListAsync(ct);
        var plan = Planen(rows.Select(r => new Bestand(r.Id, r.Plz, r.Ort, r.Region)).ToList(), liste);
        var byId = rows.ToDictionary(r => r.Id);
        foreach (var a in plan.Aktualisiert)
        {
            var row = byId[a.Id];
            row.Ort = a.Ort;
            row.Region = a.Region;
        }
        foreach (var id in plan.Entfernt)
            db.Grenzregionen.Remove(byId[id]);
        foreach (var z in plan.Neu)
        {
            db.Grenzregionen.Add(new Grenzregion
            {
                Plz = z.Plz,
                Ort = z.Ort,
                Region = z.Region,
                LandIso = "IT",
            });
        }
        await db.SaveChangesAsync(ct);
        return (plan.Neu.Count, plan.Aktualisiert.Count, plan.Entfernt.Count, liste.Count);
    }

    static string Schluessel(string plz, string ort)
        => plz.Trim() + "|" + ort.Trim().ToLowerInvariant();

    static string RegionText(string provincia, string regione)
    {
        provincia = provincia.Trim();
        regione = regione.Trim();
        if (provincia.Length == 0) return regione;
        if (regione.Length == 0) return provincia;
        return provincia + ", " + regione;
    }

    static List<string> LiesSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return new List<string>();
        using var s = entry.Open();
        var doc = XDocument.Load(s);
        return doc.Descendants()
            .Where(e => e.Name.LocalName == "si")
            .Select(si => si.Value)
            .ToList();
    }

    readonly record struct Zelle(int Col, string Wert);

    static IEnumerable<Zelle> Zellen(XElement row, List<string> ss)
    {
        foreach (var c in row.Elements().Where(e => e.Name.LocalName == "c"))
        {
            var referenz = c.Attribute("r")?.Value ?? "";
            var col = Spalte(referenz);
            if (col <= 0) continue;
            yield return new Zelle(col, Wert(c, ss));
        }
    }

    static string Wert(XElement c, List<string> ss)
    {
        var art = c.Attribute("t")?.Value;
        if (art == "inlineStr")
            return string.Concat(c.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
        var v = c.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? "";
        if (art == "s" && int.TryParse(v, out var i) && i >= 0 && i < ss.Count)
            return ss[i];
        return v;
    }

    static int Spalte(string referenz)
    {
        var n = 0;
        foreach (var ch in referenz)
        {
            if (ch is >= 'A' and <= 'Z') n = n * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') n = n * 26 + (ch - 'a' + 1);
            else break;
        }
        return n;
    }
}

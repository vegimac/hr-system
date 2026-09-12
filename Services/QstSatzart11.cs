using HrSystem.Data;
using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// ESTV Satzart 11 — lineare Sätze der Swissdec-Sonderkategorien
/// (HEN/HEY, MEN/MEY, NON/NOY, SFN). Steht in derselben Tarifdatei wie
/// Satzart 06, wird aber nicht über die Stufen-Tabelle gelesen.
///
/// Zeile (Festbreite, Latin-1), Beispiel tar25be:
///   1101BEHEY       20250101…  …02300
///   Pos 4–5 Kanton, 6–8 Code, 16–23 Gültig-ab, 54–58 Satz × 100
///   (02300 = 23.00 %).
///
/// Ablage: <c>qst_sonderkategorie_satz</c> mit gültig von/bis — wie SV-Sätze.
/// Neue Jahresdatei (tar27be) schliesst die vorige Version per Vortag.
/// Von Hand gepflegte Zeilen (Quelle nicht «ESTV …») bleiben unangetastet.
/// Walter 12.09.2026.
/// </summary>
public static class QstSatzart11
{
    public const string QuellePrefix = "ESTV ";

    public readonly record struct Zeile(
        string Code, string Kanton, DateOnly ValidFrom, decimal SatzPct, string Dateiname);

    public static IReadOnlyList<Zeile> LiesDatei(string path)
    {
        if (!File.Exists(path)) return [];
        var name = Path.GetFileName(path);
        var list = new List<Zeile>();
        foreach (var raw in File.ReadLines(path, System.Text.Encoding.Latin1))
        {
            var z = ParseZeile(raw, name);
            if (z != null) list.Add(z.Value);
        }
        return list;
    }

    public static IReadOnlyList<Zeile> LiesVerzeichnis(string verzeichnis)
    {
        if (!Directory.Exists(verzeichnis)) return [];
        return Directory.GetFiles(verzeichnis, "tar*.txt")
            .SelectMany(LiesDatei)
            .ToList();
    }

    /// <summary>Eine Zeile. null = keine Satzart 11 / unlesbar / unbekannter Code.</summary>
    public static Zeile? ParseZeile(string rawLine, string dateiname = "")
    {
        var padded = rawLine.TrimEnd('\r', '\n').PadRight(62);
        if (padded.Length < 59 || !padded.StartsWith("11")) return null;
        var kanton = padded[4..6].ToUpperInvariant();
        var code = padded[6..9].Trim().ToUpperInvariant();
        if (QstVordefinierteKategorie.Parse(code) == null) return null;
        if (kanton.Length != 2 || !kanton.All(char.IsLetter)) return null;
        if (!DateOnly.TryParseExact(padded[16..24], "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var von))
            return null;
        if (!int.TryParse(padded.AsSpan(54, 5), out var satzBp)) return null;
        return new Zeile(code, kanton, von, satzBp / 100m, dateiname);
    }

    /// <summary>
    /// Schliesst ESTV-Zeilen derselben Kombination Code+Kanton:
    /// ältere endet am Vortag der nächsten, die jüngste bleibt offen.
    /// </summary>
    public static void SchliesseGueltigkeit(IEnumerable<QstSonderkategorieSatz> estvZeilen)
    {
        foreach (var grp in estvZeilen
            .Where(s => (s.Quelle ?? "").StartsWith(QuellePrefix, StringComparison.Ordinal))
            .GroupBy(s => (s.Code, s.Kanton)))
        {
            var ordered = grp.OrderBy(s => s.ValidFrom).ToList();
            for (var i = 0; i < ordered.Count; i++)
                ordered[i].ValidTo = i + 1 < ordered.Count
                    ? ordered[i + 1].ValidFrom.AddDays(-1)
                    : null;
        }
    }

    /// <summary>
    /// True, wenn für jede Tarifdatei schon mindestens eine ESTV-Zeile
    /// in der Tabelle steht. Dann darf der Start die ~75 MB / 1.2 Mio.
    /// Zeilen nicht noch einmal lesen (Walter 12.09.2026: Start nach
    /// Deploy wieder Minuten, weil Sync bei jedem Start lief).
    /// </summary>
    public static bool VerzeichnisBereitsEingelesen(
        IEnumerable<string?> quellen, IEnumerable<string> dateinamen)
    {
        var estv = quellen
            .Where(s => !string.IsNullOrWhiteSpace(s)
                        && s.StartsWith(QuellePrefix, StringComparison.Ordinal))
            .ToList();
        if (estv.Count == 0) return false;
        return dateinamen.All(n =>
            !string.IsNullOrWhiteSpace(n)
            && estv.Any(q => q!.Contains(n, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Liest alle tar*.txt und schreibt Satzart 11 in die Tabelle.
    /// Bestehende ESTV-Zeilen werden nachgezogen; Handpflege bleibt.
    /// Ohne <paramref name="force"/>: übersprungen, wenn alle Dateien
    /// schon einmal eingelesen sind (neuer Jahresfile → läuft wieder).
    /// </summary>
    public static int SyncAusVerzeichnis(AppDbContext db, string verzeichnis, bool force = false)
    {
        if (!Directory.Exists(verzeichnis)) return 0;
        var dateien = Directory.GetFiles(verzeichnis, "tar*.txt");
        if (dateien.Length == 0) return 0;

        if (!force)
        {
            var namen = dateien.Select(f => Path.GetFileName(f) ?? "");
            var quellen = db.QstSonderkategorieSaetze
                .Where(s => s.Quelle != null && s.Quelle.StartsWith(QuellePrefix))
                .Select(s => s.Quelle)
                .Distinct()
                .ToList();
            if (VerzeichnisBereitsEingelesen(quellen, namen))
                return 0;
        }

        var zeilen = dateien.SelectMany(LiesDatei).ToList();
        if (zeilen.Count == 0) return 0;

        var bestehend = db.QstSonderkategorieSaetze.ToList();
        var geaendert = 0;

        foreach (var z in zeilen)
        {
            var quelle = QuellePrefix + (string.IsNullOrWhiteSpace(z.Dateiname) ? "Satzart 11" : z.Dateiname + " Satzart 11");
            var da = bestehend.FirstOrDefault(s =>
                string.Equals(s.Code, z.Code, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.Kanton, z.Kanton, StringComparison.OrdinalIgnoreCase)
                && s.ValidFrom == z.ValidFrom);
            if (da != null)
            {
                if (!(da.Quelle ?? "").StartsWith(QuellePrefix, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(da.Quelle))
                    continue;
                if (da.SatzPct != z.SatzPct || da.Quelle != quelle
                    || !string.Equals(da.Code, z.Code, StringComparison.Ordinal))
                {
                    da.SatzPct = z.SatzPct;
                    da.Quelle = quelle;
                    da.Code = z.Code;
                    da.Gruppe = Gruppe(z.Code);
                    geaendert++;
                }
            }
            else
            {
                var neu = new QstSonderkategorieSatz
                {
                    Code = z.Code,
                    Gruppe = Gruppe(z.Code),
                    Kanton = z.Kanton,
                    SatzPct = z.SatzPct,
                    Quelle = quelle,
                    ValidFrom = z.ValidFrom,
                    CreatedAt = DateTime.Now,
                };
                db.QstSonderkategorieSaetze.Add(neu);
                bestehend.Add(neu);
                geaendert++;
            }
        }

        SchliesseGueltigkeit(bestehend);
        db.SaveChanges();
        return geaendert;
    }

    private static string Gruppe(string code)
    {
        var e = QstVordefinierteKategorie.Parse(code);
        return e == null ? "" : QstVordefinierteKategorie.GruppeVon(e.Value.Art);
    }
}

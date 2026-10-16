using System.Globalization;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Lädt <c>swiss_location</c> aus <c>data_swiss_locations.csv</c> (AMTOVZ:
/// PLZ + Ortschaftsname + politische Gemeinde).
///
/// Walter 29.07.2026: Der frühere Import (<c>import_swiss_locations.sql</c>)
/// mischte Gemeinden unter falsche PLZ (z.B. Thörigen unter 3360 statt 3367)
/// und kannte keine Ortschaft ≠ Gemeinde (Bützberg/4922). Ein reines
/// <c>ALTER + UPDATE ortschaftsname = gemeindename</c> repariert das NICHT —
/// die Tabelle muss neu geladen werden.
/// </summary>
public static class SwissLocationReimportService
{
    public const string CsvFileName = "data_swiss_locations.csv";

    /// <summary>
    /// Sentinel für den alten, falschen Stand: Thörigen hing an PLZ 3360.
    /// Nach korrektem AMTOVZ-Import gehört Thörigen nur zu 3367.
    /// </summary>
    public static async Task<bool> IsStaleAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!await db.SwissLocations.AnyAsync(ct))
            return true;

        var thorigenUnder3360 = await db.SwissLocations.AnyAsync(l =>
            l.Plz4 == "3360"
            && (l.Ortschaftsname == "Thörigen" || l.Gemeindename == "Thörigen"), ct);
        if (thorigenUnder3360)
            return true;

        // Bützberg ist der klassische Ortschaft≠Gemeinde-Fall (PLZ 4922).
        var has4922 = await db.SwissLocations.AnyAsync(l => l.Plz4 == "4922", ct);
        if (has4922)
        {
            var hasBuetzberg = await db.SwissLocations.AnyAsync(l =>
                l.Plz4 == "4922" && l.Ortschaftsname == "Bützberg", ct);
            if (!hasBuetzberg)
                return true;
        }

        return false;
    }

    public static string? FindCsvPath(string? contentRoot = null)
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, CsvFileName),
            Path.Combine(Directory.GetCurrentDirectory(), CsvFileName),
        };
        if (!string.IsNullOrWhiteSpace(contentRoot))
            candidates.Insert(0, Path.Combine(contentRoot, CsvFileName));

        foreach (var p in candidates)
        {
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>Offizielle swisstopo-Download-Seite (AMTOVZ CSV) — Walter 16.10.2026.</summary>
    public const string AmtovzDownloadUrl =
        "https://www.swisstopo.admin.ch/de/amtliches-ortschaftenverzeichnis#Download";

    public static List<SwissLocation> ParseCsv(string path)
    {
        var raw = File.ReadAllText(path);
        // BOM entfernen
        if (raw.Length > 0 && raw[0] == '\uFEFF') raw = raw[1..];
        var lines = raw.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        return ParseCsvLines(lines);
    }

    /// <summary>
    /// CSV aus Upload oder Repo. Zwei Formate:
    /// <list type="bullet">
    /// <item>OneCrew: plz4;ortschaftsname;gemeindename;bfs_nr;kantonskuerzel</item>
    /// <item>swisstopo AMTOVZ: Ortschaftsname;PLZ4;…;Gemeindename;BFS-Nr;Kantonskürzel;Adressenanteil;…</item>
    /// </list>
    /// Bei mehreren Gemeinden pro Ortschaft (AMTOVZ) gewinnt der höchste Adressenanteil.
    /// </summary>
    public static List<SwissLocation> ParseCsvLines(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return new List<SwissLocation>();
        var header = (lines[0] ?? "").Trim().TrimStart('\uFEFF');
        // AMTOVZ (swisstopo): beginnt mit Ortschaftsname;PLZ4;…;BFS-Nr;Kantonskürzel
        // OneCrew: plz4;ortschaftsname;gemeindename;bfs_nr;kantonskuerzel
        // Nicht über «plz4»/«bfs» matchen — sonst greift AMTOVZ fälschlich auf OneCrew.
        if (header.StartsWith("Ortschaftsname", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Adressenanteil", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Kantonskürzel", StringComparison.OrdinalIgnoreCase)
            || header.Contains("BFS-Nr", StringComparison.OrdinalIgnoreCase))
            return ParseAmtovzLines(lines);
        return ParseOneCrewLines(lines);
    }

    static List<SwissLocation> ParseOneCrewLines(IReadOnlyList<string> lines)
    {
        var list = new List<SwissLocation>(lines.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim().TrimStart('\uFEFF');
            if (line.Length == 0) continue;
            if (i == 0 && line.StartsWith("plz4", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split(';');
            if (parts.Length < 5) continue;

            var plz = parts[0].Trim().Trim('"');
            var ort = parts[1].Trim().Trim('"');
            var gem = parts[2].Trim().Trim('"');
            if (plz.Length < 4 || ort.Length == 0) continue;
            if (!int.TryParse(parts[3].Trim().Trim('"'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bfs))
                continue;
            var kt = parts[4].Trim().Trim('"').ToUpperInvariant();
            if (kt.Length != 2) continue;
            if (string.IsNullOrWhiteSpace(gem)) gem = ort;

            var key = plz + "|" + ort;
            if (!seen.Add(key)) continue;

            list.Add(new SwissLocation
            {
                Plz4 = plz,
                Ortschaftsname = ort,
                Gemeindename = gem,
                BfsNr = bfs,
                Kantonskuerzel = kt
            });
        }
        return list;
    }

    /// <summary>
    /// Offizielles AMTOVZ (swisstopo), Stand ab Jan 2026 inkl. ZIP_ID / Adressenanteil.
    /// </summary>
    static List<SwissLocation> ParseAmtovzLines(IReadOnlyList<string> lines)
    {
        // Header: Ortschaftsname;PLZ4;Zusatzziffer;ZIP_ID;Gemeindename;BFS-Nr;Kantonskürzel;Adressenanteil;…
        var best = new Dictionary<string, (SwissLocation Loc, decimal Anteil)>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim().TrimStart('\uFEFF');
            if (line.Length == 0) continue;
            if (i == 0 && line.StartsWith("Ortschaftsname", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split(';');
            if (parts.Length < 7) continue;

            var ort = parts[0].Trim().Trim('"');
            var plz = parts[1].Trim().Trim('"');
            var gem = parts[4].Trim().Trim('"');
            if (plz.Length < 4 || ort.Length == 0) continue;
            if (!int.TryParse(parts[5].Trim().Trim('"'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bfs))
                continue;
            var kt = parts[6].Trim().Trim('"').ToUpperInvariant();
            // Liechtenstein: AMTOVZ liefert oft leeres Kantonskürzel (BFS 7001–7011).
            if (kt.Length == 0 && bfs is >= 7001 and <= 7011) kt = "FL";
            if (kt.Length != 2) continue;
            if (string.IsNullOrWhiteSpace(gem)) gem = ort;

            var anteil = 0m;
            if (parts.Length > 7)
            {
                var raw = parts[7].Trim().Trim('"').Replace("%", "").Replace(',', '.').Trim();
                decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out anteil);
            }

            var key = plz + "|" + ort;
            var loc = new SwissLocation
            {
                Plz4 = plz,
                Ortschaftsname = ort,
                Gemeindename = gem,
                BfsNr = bfs,
                Kantonskuerzel = kt
            };
            if (!best.TryGetValue(key, out var prev) || anteil > prev.Anteil)
                best[key] = (loc, anteil);
        }

        return best.Values.Select(v => v.Loc).OrderBy(l => l.Plz4).ThenBy(l => l.Ortschaftsname).ToList();
    }

    /// <summary>Schreibt den OneCrew-CSV-Stand (für Repo / «Mitgelieferte CSV»).</summary>
    public static string ToOneCrewCsv(IEnumerable<SwissLocation> rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("plz4;ortschaftsname;gemeindename;bfs_nr;kantonskuerzel");
        foreach (var r in rows.OrderBy(x => x.Plz4).ThenBy(x => x.Ortschaftsname))
            sb.AppendLine($"{r.Plz4};{r.Ortschaftsname};{r.Gemeindename};{r.BfsNr};{r.Kantonskuerzel}");
        return sb.ToString();
    }

    /// <summary>
    /// Truncate + Neu-Insert aus der mitgelieferten AMTOVZ-CSV.
    /// </summary>
    public static async Task<(int Count, string CsvPath)> ReimportAsync(
        AppDbContext db, string? contentRoot = null, CancellationToken ct = default)
    {
        var path = FindCsvPath(contentRoot)
            ?? throw new FileNotFoundException(
                $"«{CsvFileName}» nicht gefunden (weder neben der DLL noch im ContentRoot).");

        var rows = ParseCsv(path);
        await ReplaceAllAsync(db, rows, path, ct);
        return (rows.Count, path);
    }

    /// <summary>
    /// Truncate + Neu-Insert aus einem hochgeladenen AMTOVZ-CSV (Aktualisierung).
    /// Speichert die Datei zusätzlich als <c>data_swiss_locations.csv</c> im ContentRoot,
    /// damit «AMTOVZ neu laden» denselben Stand trifft (bis zum nächsten Deploy).
    /// </summary>
    public static async Task<(int Count, string SavedAs)> ImportUploadAsync(
        AppDbContext db, Stream csvStream, string? contentRoot = null, CancellationToken ct = default)
    {
        using var reader = new StreamReader(csvStream, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(ct);
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        var rows = ParseCsvLines(lines);
        if (rows.Count < 1000)
            throw new InvalidOperationException(
                $"CSV liefert nur {rows.Count} Zeilen — Abbruch (erwartet ~4000). "
                + "Akzeptiert: OneCrew-CSV oder offizielles AMTOVZ (swisstopo).");

        string? savedAs = null;
        if (!string.IsNullOrWhiteSpace(contentRoot))
        {
            // Immer im OneCrew-Format speichern — egal ob AMTOVZ oder vereinfacht hochgeladen.
            savedAs = Path.Combine(contentRoot, CsvFileName);
            await File.WriteAllTextAsync(savedAs, ToOneCrewCsv(rows), ct);
        }

        await ReplaceAllAsync(db, rows, savedAs ?? "(Upload)", ct);
        return (rows.Count, savedAs ?? "(nur DB)");
    }

    static async Task ReplaceAllAsync(
        AppDbContext db, List<SwissLocation> rows, string sourceLabel, CancellationToken ct)
    {
        if (rows.Count < 1000)
            throw new InvalidOperationException(
                $"CSV «{sourceLabel}» liefert nur {rows.Count} Zeilen — Abbruch (erwartet ~4000).");

        // Schema-Härtung vor Truncate (idempotent).
        await db.Database.ExecuteSqlRawAsync(@"
            ALTER TABLE swiss_location ADD COLUMN IF NOT EXISTS ortschaftsname VARCHAR(80);
            UPDATE swiss_location SET ortschaftsname = gemeindename
             WHERE ortschaftsname IS NULL OR btrim(ortschaftsname) = '';
            ALTER TABLE swiss_location DROP CONSTRAINT IF EXISTS swiss_location_plz_bfs_unique;
            DROP INDEX IF EXISTS swiss_location_plz_bfs_unique;
        ", ct);

        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE swiss_location RESTART IDENTITY;", ct);

        const int batch = 500;
        for (var i = 0; i < rows.Count; i += batch)
        {
            var slice = rows.Skip(i).Take(batch).ToList();
            db.SwissLocations.AddRange(slice);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        await db.Database.ExecuteSqlRawAsync(@"
            CREATE UNIQUE INDEX IF NOT EXISTS swiss_location_plz_ortschaft_unique
                ON swiss_location (plz4, ortschaftsname);
            CREATE INDEX IF NOT EXISTS idx_swiss_location_plz
                ON swiss_location (plz4);
        ", ct);
    }

    /// <summary>
    /// Beim App-Start: nur neu laden wenn der alte/falsche Stand erkannt wird.
    /// </summary>
    public static async Task EnsureFreshAsync(AppDbContext db, string? contentRoot = null)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE swiss_location ADD COLUMN IF NOT EXISTS ortschaftsname VARCHAR(80);
                UPDATE swiss_location SET ortschaftsname = gemeindename
                 WHERE ortschaftsname IS NULL OR btrim(ortschaftsname) = '';
            ");
        }
        catch (Exception ex)
        {
            Console.WriteLine("WARN: swiss_location Spalte ortschaftsname: " + ex.Message);
        }

        if (!await IsStaleAsync(db))
        {
            var n = await db.SwissLocations.CountAsync();
            Console.WriteLine($"swiss_location: aktuell ({n} Ortschaften).");
            return;
        }

        try
        {
            var (count, path) = await ReimportAsync(db, contentRoot);
            Console.WriteLine($"swiss_location: Re-Import aus «{path}» — {count} Ortschaften geladen.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("WARN: swiss_location Re-Import fehlgeschlagen: " + ex.Message);
            Console.WriteLine("      Bitte data_swiss_locations.csv deployen oder admin/reimport auslösen.");
        }
    }
}

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

    public static List<SwissLocation> ParseCsv(string path)
        => ParseCsvLines(File.ReadAllLines(path));

    /// <summary>CSV aus Upload (UTF-8, Semikolon: plz4;ortschaftsname;gemeindename;bfs_nr;kantonskuerzel).</summary>
    public static List<SwissLocation> ParseCsvLines(IReadOnlyList<string> lines)
    {
        var list = new List<SwissLocation>(lines.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            // Header
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
        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        var rows = ParseCsvLines(lines);
        if (rows.Count < 1000)
            throw new InvalidOperationException(
                $"CSV liefert nur {rows.Count} Zeilen — Abbruch (erwartet ~4000). Format: plz4;ortschaftsname;gemeindename;bfs_nr;kantonskuerzel");

        string? savedAs = null;
        if (!string.IsNullOrWhiteSpace(contentRoot))
        {
            savedAs = Path.Combine(contentRoot, CsvFileName);
            await File.WriteAllTextAsync(savedAs, text, ct);
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

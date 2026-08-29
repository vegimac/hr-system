using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace HrSystem.Services;

/// <summary>
/// Lädt die offiziellen kantonalen Quellensteuer-Tarifdateien (ESTV-Format)
/// und stellt eine schnelle Lookup-Methode bereit.
///
/// Dateiformat: ESTV Recordart 06 (siehe «Aufbau und Recordformate …»):
///   Dateinamen: tar{JJ}{kanton}.txt  z.B. tar26ag.txt = 2026 Aargau
///
/// Pro Stufe:
///   Pos 46–54 — Mindeststeuer in Fr. (Rappen, 000000200 = CHF 2.00)
///   Pos 55–59 — Steuer %-Satz (00715 = 7.15 %)
///
/// Regel ESTV 4.4:
///   wenn IST-Brutto × Satz &lt; Mindeststeuer → Mindeststeuer, sonst % × IST-Brutto.
/// </summary>
public class QuellensteuerTarifService
{
    // Key: "2026|LU|A|0|N"  Value: SortedList<Bruttolohn_CHF/10, Stufe>
    private readonly ConcurrentDictionary<string, SortedList<int, QstTarifStufe>> _tarife = new();

    // Metadaten je geladener Datei
    private readonly ConcurrentBag<QstDateiStatus> _dateienStatus = new();

    private bool _loaded;
    private readonly object _loadLock = new();
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<QuellensteuerTarifService> _logger;

    public QuellensteuerTarifService(
        IWebHostEnvironment env,
        ILogger<QuellensteuerTarifService> logger)
    {
        _env = env;
        _logger = logger;
    }

    // ── Öffentliche API ───────────────────────────────────────────────────

    /// <summary>
    /// Gibt den Steuersatz in Prozent zurück (z.B. 21.81m für 21.81%).
    /// Gibt null zurück wenn Kanton/Tarif/Jahr nicht gefunden.
    /// </summary>
    public decimal? GetSteuersatzProzent(
        string kanton, string tarifCode, int kinder, bool kirchensteuer,
        decimal bruttolohnCHF, int? jahr = null)
    {
        var stufe = FindStufe(kanton, tarifCode, kinder, kirchensteuer, bruttolohnCHF, ResolveJahr(jahr));
        return stufe is null ? null : stufe.Value.SatzBasispunkte / 100m;
    }

    /// <summary>
    /// Berechnet den monatlichen Quellensteuer-Betrag in CHF
    /// (Satz-Lookup und Bemessung auf demselben Brutto).
    /// </summary>
    public decimal? GetSteuerBetrag(
        string kanton, string tarifCode, int kinder, bool kirchensteuer,
        decimal bruttolohnCHF, int? jahr = null)
        => Berechne(kanton, tarifCode, kinder, kirchensteuer, bruttolohnCHF, bruttolohnCHF, jahr)
            ?.SteuerbetragCHF;

    /// <summary>
    /// Volle QST-Berechnung nach ESTV: Satz aus satzbestimmendem Brutto,
    /// Betrag = max(IST × Satz, Mindeststeuer der Stufe).
    /// </summary>
    public QstTarifBerechnung? Berechne(
        string kanton, string tarifCode, int kinder, bool kirchensteuer,
        decimal satzbestimmenderBruttoCHF,
        decimal istBruttoCHF,
        int? jahr = null)
    {
        EnsureLoaded();
        var stufe = FindStufe(kanton, tarifCode, kinder, kirchensteuer,
            satzbestimmenderBruttoCHF, ResolveJahr(jahr));
        if (stufe is null) return null;

        decimal satzPct = stufe.Value.SatzBasispunkte / 100m;
        decimal betrag = Math.Round(istBruttoCHF * satzPct / 100m, 2, MidpointRounding.AwayFromZero);
        bool mindestAngewendet = false;

        // ESTV 4.4: wenn % × Einkommen &lt; Mindeststeuer → Mindeststeuer
        if (stufe.Value.MindeststeuerChf > 0
            && istBruttoCHF > 0
            && betrag < stufe.Value.MindeststeuerChf)
        {
            betrag = stufe.Value.MindeststeuerChf;
            mindestAngewendet = true;
        }

        if (betrag < 0) betrag = 0;

        return new QstTarifBerechnung(
            SteuerbetragCHF: betrag,
            SteuersatzPct: satzPct,
            MindeststeuerCHF: stufe.Value.MindeststeuerChf,
            MindeststeuerAngewendet: mindestAngewendet
        );
    }

    /// <summary>Gibt alle verfügbaren Kantone zurück.</summary>
    public IReadOnlyList<string> GetVerfuegbareKantone(int? jahr = null)
    {
        EnsureLoaded();
        string prefix = $"{ResolveJahr(jahr)}|";
        return _tarife.Keys
            .Where(k => k.StartsWith(prefix))
            .Select(k => k.Split('|')[1])
            .Distinct().OrderBy(k => k).ToList();
    }

    /// <summary>Gibt alle Tarifkombinationen eines Kantons zurück.</summary>
    public IReadOnlyList<QstTarifInfo> GetTarifKombinationen(string kanton, int? jahr = null)
    {
        EnsureLoaded();
        string prefix = $"{ResolveJahr(jahr)}|{kanton.ToUpper()}|";
        return _tarife.Keys
            .Where(k => k.StartsWith(prefix))
            .Select(k =>
            {
                var parts = k.Split('|');
                // Key format: "2026|LU|A|0|N"
                return new QstTarifInfo(
                    Kanton:        parts[1],
                    Tarif:         parts[2],
                    Kinder:        int.Parse(parts[3]),
                    Kirchensteuer: parts[4] == "Y"
                );
            })
            .OrderBy(t => t.Tarif).ThenBy(t => t.Kinder).ThenBy(t => t.Kirchensteuer)
            .ToList();
    }

    /// <summary>
    /// K4.4 (Walter 29.08.2026): enthält die geladene ESTV-Tarifdatei des
    /// Kantons Y-Tarife (Kirchensteuer)? null = keine Datei geladen (kein
    /// Urteil möglich — Aufrufer darf daraus NICHT «kein Y» ableiten).
    /// </summary>
    public bool? HatYTarife(string kanton, int? jahr = null)
    {
        if (string.IsNullOrWhiteSpace(kanton)) return null;
        var kombis = GetTarifKombinationen(kanton.Trim(), jahr);
        return kombis.Count == 0 ? (bool?)null : kombis.Any(t => t.Kirchensteuer);
    }

    /// <summary>Gibt Status aller geladenen Tarifdateien zurück.</summary>
    public IReadOnlyList<QstDateiStatus> GetDateienStatus()
    {
        EnsureLoaded();
        return _dateienStatus.OrderBy(d => d.Jahr).ThenBy(d => d.Kanton).ToList();
    }

    /// <summary>
    /// Importiert eine Tarifdatei (.txt oder .zip) und lädt den Cache neu.
    /// Gibt den Kanton und Jahr der importierten Datei zurück.
    /// </summary>
    public async Task<QstImportErgebnis> ImportiereAsync(Stream fileStream, string fileName)
    {
        string tarifDir = TarifVerzeichnis;
        Directory.CreateDirectory(tarifDir);

        var ergebnis = new QstImportErgebnis();

        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = new ZipArchive(fileStream, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries.Where(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
            {
                using var entryStream = entry.Open();
                var r = await SpeichereTarifDateiAsync(entryStream, entry.Name, tarifDir);
                if (r != null)
                {
                    ergebnis.ImportierteDateien.Add(r);
                    _logger.LogInformation("QST Import aus ZIP: {File} → {Kanton} {Jahr}", entry.Name, r.Kanton, r.Jahr);
                }
            }
        }
        else
        {
            var r = await SpeichereTarifDateiAsync(fileStream, fileName, tarifDir);
            if (r != null)
                ergebnis.ImportierteDateien.Add(r);
        }

        if (ergebnis.ImportierteDateien.Count > 0)
            Reload();

        return ergebnis;
    }

    /// <summary>Setzt den Cache zurück und lädt alle Dateien neu.</summary>
    public void Reload()
    {
        lock (_loadLock)
        {
            _tarife.Clear();
            _dateienStatus.Clear();
            _loaded = false;
            LoadAllTarifFiles();
            _loaded = true;
        }
        _logger.LogInformation("QST-Tarife neu geladen: {Count} Kombinationen", _tarife.Count);
    }

    // ── Internes ─────────────────────────────────────────────────────────

    private string TarifVerzeichnis => Path.Combine(_env.ContentRootPath, "Assets", "Quellensteuer");

    private int ResolveJahr(int? jahr) => jahr ?? DateTime.Now.Year;

    private QstTarifStufe? FindStufe(
        string kanton, string tarifCode, int kinder, bool kirchensteuer,
        decimal bruttolohnCHF, int jahr)
    {
        EnsureLoaded();
        string key = $"{jahr}|{kanton.ToUpper()}|{tarifCode.ToUpper()}|{kinder}|{(kirchensteuer ? 'Y' : 'N')}";

        if (!_tarife.TryGetValue(key, out var lookup))
            return null;

        // Lookup-Schlüssel = Monatseinkommen in CHF/10
        // (bestehende Parser-Konvention: Stufe 135 = ab ca. CHF 1'350)
        int lohn = (int)Math.Floor(bruttolohnCHF / 10m);
        if (lookup.Count == 0) return new QstTarifStufe(0, 0m);

        int idx = BinarySearchFloor(lookup.Keys, lohn);
        return idx < 0 ? lookup.Values[0] : lookup.Values[idx];
    }

    private static int BinarySearchFloor(IList<int> keys, int target)
    {
        int lo = 0, hi = keys.Count - 1, result = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (keys[mid] <= target) { result = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return result;
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_loadLock)
        {
            if (_loaded) return;
            LoadAllTarifFiles();
            _loaded = true;
        }
    }

    private void LoadAllTarifFiles()
    {
        string tarifDir = TarifVerzeichnis;
        if (!Directory.Exists(tarifDir))
        {
            _logger.LogWarning("Quellensteuer-Tarifverzeichnis nicht gefunden: {Path}", tarifDir);
            return;
        }

        // Alle tar{JJ}{kanton}.txt Dateien laden (z.B. tar26lu.txt, tar27zh.txt)
        var files = Directory.GetFiles(tarifDir, "tar*.txt");
        foreach (var file in files)
            ParseTarifFile(file);

        _logger.LogInformation("QST-Tarife bereit: {Comb} Kombinationen", _tarife.Count);
    }

    private void ParseTarifFile(string filePath)
    {
        string fname = Path.GetFileNameWithoutExtension(filePath).ToLower();

        // Dateiname: tar26lu → Jahr=2026, Kanton=LU
        var match = Regex.Match(fname, @"^tar(\d{2})([a-z]{2})$");
        if (!match.Success)
        {
            _logger.LogWarning("Unbekanntes Dateinamenformat, übersprungen: {File}", fname);
            return;
        }

        int jahr = 2000 + int.Parse(match.Groups[1].Value);
        string kantonAusDateiname = match.Groups[2].Value.ToUpper();

        int count = 0;
        int maxEinkommen = 0;
        var kombinationen = new HashSet<string>();

        foreach (string rawLine in File.ReadLines(filePath, System.Text.Encoding.Latin1))
        {
            // Festbreite ESTV: Pos 46–54 Mindeststeuer, 55–59 %-Satz
            string padded = rawLine.TrimEnd('\r', '\n').PadRight(62);
            if (padded.Length < 59 || !padded.StartsWith("06")) continue;

            string kanton  = padded[4..6];
            char   tarif   = padded[6];
            int    kinder  = padded[7] - '0';
            char   konfess = padded[8];

            // Einkommen-Schlüssel: bisherige Konvention (CHF/10) beibehalten,
            // damit Lookup-Grenzen unverändert bleiben.
            int spaceIdx = padded.IndexOf(' ', 9);
            if (spaceIdx < 0) continue;

            string block1 = padded[(spaceIdx + 1)..].TrimStart();
            int space2 = block1.IndexOf(' ');
            if (space2 < 0) continue;

            string b1 = block1[..space2];
            if (b1.Length < 14) continue;
            if (!int.TryParse(b1[8..14], out int einkommen)) continue;

            // ESTV Pos 55–59 (1-basiert) = Index 54..59 — %-Satz × 100
            if (!int.TryParse(padded.AsSpan(54, 5), out int satzBP)) continue;

            // ESTV Pos 46–54 — Mindeststeuer in Fr. mit 2 Dezimalstellen
            // (000000200 = CHF 2.00, 000001300 = CHF 13.00)
            decimal mindestChf = 0m;
            if (int.TryParse(padded.AsSpan(45, 9), out int mindestRappen))
                mindestChf = mindestRappen / 100m;

            string key = $"{jahr}|{kanton}|{tarif}|{kinder}|{konfess}";

            var lookup = _tarife.GetOrAdd(key, _ => new SortedList<int, QstTarifStufe>());
            lock (lookup)
            {
                if (!lookup.ContainsKey(einkommen))
                    lookup[einkommen] = new QstTarifStufe(satzBP, mindestChf);
            }

            kombinationen.Add($"{kanton}|{tarif}|{kinder}|{konfess}");
            if (einkommen > maxEinkommen) maxEinkommen = einkommen;
            count++;
        }

        if (count > 0)
        {
            _dateienStatus.Add(new QstDateiStatus(
                Jahr: jahr,
                Kanton: kantonAusDateiname,
                Dateiname: Path.GetFileName(filePath),
                AnzahlKombinationen: kombinationen.Count,
                AnzahlEintraege: count,
                MaxEinkommen: maxEinkommen,
                GeladenAm: DateTime.Now
            ));
            _logger.LogInformation("QST {Jahr} {Kanton}: {Count} Einträge, {Comb} Kombinationen",
                jahr, kantonAusDateiname, count, kombinationen.Count);
        }
    }

    /// <summary>
    /// Liest eine Tarifdatei, erkennt den Kanton aus dem Inhalt,
    /// und speichert sie mit korrektem Dateinamen.
    /// </summary>
    private static async Task<QstImportErgebnis.DateiInfo?> SpeichereTarifDateiAsync(
        Stream stream, string originalName, string tarifDir)
    {
        // Inhalt in Memory lesen (um zweimal zu lesen: einmal für Kanton, einmal speichern)
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ms.Position = 0;

        // Kanton und Jahr aus Header-Zeile lesen
        string? kanton = null;
        int? datumJahr = null;
        using (var reader = new StreamReader(ms, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            while (!reader.EndOfStream)
            {
                string? line = await reader.ReadLineAsync();
                if (line == null) break;
                line = line.Trim();
                if (line.StartsWith("00") && line.Length >= 10)
                {
                    kanton = line[2..4].ToUpper();
                    if (int.TryParse(line.Substring(line.Length - 8, 4), out int y))
                        datumJahr = y;
                    break;
                }
            }
        }

        if (kanton == null || kanton.Length != 2 || !kanton.All(char.IsLetter))
            return null;

        int jahr = datumJahr ?? DateTime.Now.Year;
        string jahresKuerzel = (jahr % 100).ToString("00");
        string zieldatei = Path.Combine(tarifDir, $"tar{jahresKuerzel}{kanton.ToLower()}.txt");

        ms.Position = 0;
        using var outFile = File.Create(zieldatei);
        await ms.CopyToAsync(outFile);

        return new QstImportErgebnis.DateiInfo(kanton, jahr, Path.GetFileName(zieldatei));
    }

    /// <summary>Eine Tarifstufe: %-Satz (Basispunkte) + Mindeststeuer CHF.</summary>
    private readonly record struct QstTarifStufe(int SatzBasispunkte, decimal MindeststeuerChf);
}

// ── Records ───────────────────────────────────────────────────────────────────

/// <summary>Ergebnis einer QST-Berechnung inkl. Mindeststeuer-Anwendung.</summary>
public record QstTarifBerechnung(
    decimal SteuerbetragCHF,
    decimal SteuersatzPct,
    decimal MindeststeuerCHF,
    bool    MindeststeuerAngewendet
);

/// <summary>Status einer geladenen Tarifdatei.</summary>
public record QstDateiStatus(
    int      Jahr,
    string   Kanton,
    string   Dateiname,
    int      AnzahlKombinationen,
    int      AnzahlEintraege,
    int      MaxEinkommen,
    DateTime GeladenAm
);

/// <summary>Ergebnis eines Import-Vorgangs.</summary>
public class QstImportErgebnis
{
    public List<DateiInfo> ImportierteDateien { get; } = new();
    public bool Erfolg => ImportierteDateien.Count > 0;

    public record DateiInfo(string Kanton, int Jahr, string Dateiname);
}

/// <summary>Info-Record für eine Tarifkombination.</summary>
public record QstTarifInfo(string Kanton, string Tarif, int Kinder, bool Kirchensteuer)
{
    public string QstCode => $"{Tarif}{Kinder}{(Kirchensteuer ? 'Y' : 'N')}";
}

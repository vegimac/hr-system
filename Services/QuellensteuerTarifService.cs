using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace HrSystem.Services;

/// <summary>
/// Lädt die offiziellen kantonalen Quellensteuer-Tarifdateien (ESTV-Format)
/// und stellt eine schnelle Lookup-Methode bereit.
///
/// Dateiformat: Swissdec-Standardformat, Satzart 06
///   Dateinamen: tar{JJ}{kanton}.txt  z.B. tar26lu.txt = 2026 Luzern
///
/// Steuersatz: Block2 letzte 7 Stellen / 100 = Satz in Prozent
///   Beispiel: 2181 → 21.81 % → Steuer = Bruttolohn × 0.2181
/// </summary>
public class QuellensteuerTarifService
{
    // Key: "2026|LU|A|0|N"  Value: SortedList<Bruttolohn_CHF, Satz_Basispunkte>
    private readonly ConcurrentDictionary<string, SortedList<int, int>> _tarife = new();

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
        EnsureLoaded();
        int bp = GetSatzBasispunkte(kanton, tarifCode, kinder, kirchensteuer, bruttolohnCHF, ResolveJahr(jahr));
        return bp < 0 ? null : bp / 100m;
    }

    /// <summary>
    /// Berechnet den monatlichen Quellensteuer-Betrag in CHF.
    /// </summary>
    public decimal? GetSteuerBetrag(
        string kanton, string tarifCode, int kinder, bool kirchensteuer,
        decimal bruttolohnCHF, int? jahr = null)
    {
        EnsureLoaded();
        int bp = GetSatzBasispunkte(kanton, tarifCode, kinder, kirchensteuer, bruttolohnCHF, ResolveJahr(jahr));
        if (bp < 0) return null;
        return Math.Round(bruttolohnCHF * bp / 10000m, 2, MidpointRounding.AwayFromZero);
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

    private int GetSatzBasispunkte(
        string kanton, string tarifCode, int kinder, bool kirchensteuer,
        decimal bruttolohnCHF, int jahr)
    {
        string key = $"{jahr}|{kanton.ToUpper()}|{tarifCode.ToUpper()}|{kinder}|{(kirchensteuer ? 'Y' : 'N')}";

        if (!_tarife.TryGetValue(key, out var lookup))
            return -1;

        // Die ESTV-Tarifdatei kodiert das Monatseinkommen in CHF/10
        // (z.B. Eintrag 320 = CHF 3'200/Monat, Eintrag 295 = CHF 2'950/Monat)
        int lohn = (int)Math.Floor(bruttolohnCHF / 10m);
        if (lookup.Count == 0) return 0;

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
            string line = rawLine.Trim();
            if (line.Length < 30 || !line.StartsWith("06")) continue;

            string kanton  = line[4..6];
            char   tarif   = line[6];
            int    kinder  = line[7] - '0';
            char   konfess = line[8];

            int spaceIdx = line.IndexOf(' ', 9);
            if (spaceIdx < 0) continue;

            string block1 = line[(spaceIdx + 1)..].TrimStart();
            int space2 = block1.IndexOf(' ');
            if (space2 < 0) continue;

            string b1 = block1[..space2];
            string b2 = block1[(space2 + 1)..].Trim();

            if (b1.Length < 14 || b2.Length < 16) continue;
            if (!int.TryParse(b1[8..14], out int einkommen)) continue;
            if (!int.TryParse(b2[9..],   out int satzBP))    continue;

            string key = $"{jahr}|{kanton}|{tarif}|{kinder}|{konfess}";

            var lookup = _tarife.GetOrAdd(key, _ => new SortedList<int, int>());
            lock (lookup)
            {
                if (!lookup.ContainsKey(einkommen))
                    lookup[einkommen] = satzBP;
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
}

// ── Records ───────────────────────────────────────────────────────────────────

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

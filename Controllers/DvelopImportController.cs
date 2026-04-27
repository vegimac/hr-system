using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;

namespace HrSystem.Controllers;

/// <summary>
/// Importiert Dokumente aus einem d.velop-Export (CSV-Metadaten + ZIP mit PDFs).
/// Match-Logik:
///   - Mitarbeiter via Vorname + Nachname + Geburtsdatum (matcht "alt"-suffixed)
///   - Filiale via "Mandant"-Spalte (z.B. "58 McDonald's Restaurant Oftringen" → 058)
///   - Kategorie via "Kategorie"-Spalte (Prefix "HR: " entfernen)
///   - Typ via passende Sub-Spalte ("Dokumenttyp Absenzen" etc.)
///   - File via XG-ID aus "Dokument-ID" → Match in ZIP-Filenames
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/documents/import-dvelop")]
public class DvelopImportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly string _storagePath;

    public DvelopImportController(AppDbContext db, IConfiguration config, IWebHostEnvironment env)
    {
        _db = db;
        var configured = config["Documents:StoragePath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine(env.ContentRootPath, "data", "documents");
        _storagePath = configured;
    }

    public class DvelopResult
    {
        public bool DryRun { get; set; }
        public int TotalRows { get; set; }
        public int Imported { get; set; }
        public int SkippedNoEmployee { get; set; }
        public int SkippedNoBranch { get; set; }
        public int SkippedNoCategory { get; set; }
        public int SkippedNoFile { get; set; }
        public int SkippedDuplicate { get; set; }
        public List<DvelopRow> Preview { get; set; } = new();
    }

    public class DvelopRow
    {
        public int RowNum { get; set; }
        public string XgId { get; set; } = "";
        public string Filename { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public DateOnly? DateOfBirth { get; set; }
        public int? EmployeeId { get; set; }
        public string? BranchCode { get; set; }
        public string? KategorieName { get; set; }
        public string? TypName { get; set; }
        public int? DokumentTypId { get; set; }
        public string? Bemerkung { get; set; }
        public DateOnly? GueltigVon { get; set; }
        public string Action { get; set; } = "";
        public string? Reason { get; set; }
    }

    [HttpPost]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 2_000_000_000)]
    public async Task<IActionResult> Import(
        [FromForm] IFormFile csvFile,
        [FromForm] IFormFile zipFile,
        [FromForm] int employeeId,
        [FromForm] bool dryRun = true)
    {
        if (employeeId <= 0) return BadRequest("Mitarbeiter muss vor dem Import ausgewählt werden.");
        var selectedEmp = await _db.Employees.FindAsync(employeeId);
        if (selectedEmp == null) return BadRequest("Gewählter Mitarbeiter nicht gefunden.");
        if (csvFile == null || csvFile.Length == 0) return BadRequest("CSV fehlt.");
        if (zipFile == null || zipFile.Length == 0) return BadRequest("ZIP fehlt.");

        var result = new DvelopResult { DryRun = dryRun };

        // CSV einlesen
        using var sr = new StreamReader(csvFile.OpenReadStream(), Encoding.UTF8);
        var csvLines = new List<string>();
        string? cl;
        while ((cl = await sr.ReadLineAsync()) != null) csvLines.Add(cl);
        if (csvLines.Count < 2) return BadRequest("CSV ist leer oder unvollständig.");

        // CSV verwendet Semikolon
        var headers = ParseCsvLine(csvLines[0], ';');
        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++) idx[headers[i].Trim('﻿', '"', ' ')] = i;

        int Get(string name) => idx.TryGetValue(name, out var i) ? i : -1;

        int colDokId       = Get("Dokument-ID");
        int colDateiname   = Get("Dateiname");
        int colKategorie   = Get("Kategorie");
        int colMandant     = Get("Mandant");
        int colVorname     = Get("Vorname");
        int colNachname    = Get("Nachname");
        int colGebDatum    = Get("Geburtsdatum");
        int colMaNummer    = Get("Mitarbeiter Nummer");
        int colMaCombined  = Get("Mitarbeiter (Name / Geb.-Datum)");
        int colBeschr      = Get("Beschreibung Dokument");
        int colErstelltAm  = Get("Erstellt am");
        int colTypAbs      = Get("Dokumenttyp Absenzen");
        int colTypAem      = Get("Dokumenttyp Ämter & Behörden");
        int colTypPers     = Get("Dokumenttyp Persönliche Angaben");
        int colTypVert     = Get("Dokumenttyp Vertragsunterlagen");
        int colMime        = Get("MIME-Typ");
        int colGroesse     = Get("Größe");

        if (colDokId < 0 || colVorname < 0 || colNachname < 0 || colGebDatum < 0 || colKategorie < 0)
            return BadRequest("CSV hat nicht die erwarteten Spalten (Dokument-ID, Vorname, Nachname, Geburtsdatum, Kategorie).");

        // ZIP indexieren: XG-ID → Entry. Endung egal (PDF, DOC, DOCX, JPG, …).
        // Wichtig: Manche d.velop-Filenames enthalten MEHRERE XG-IDs (z.B.
        // "Foo (XG00007923).PDF (XG00008082).PDF" — alte Dokument-Verlinkung).
        // Daher ALLE XG-Matches als Schlüssel anlegen, nicht nur den ersten.
        using var zipStream = zipFile.OpenReadStream();
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        var xgRegex = new Regex(@"XG(\d+)", RegexOptions.IgnoreCase);
        var zipByXg = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        var zipEntryNames = new List<string>(); // für Diagnose
        foreach (var entry in zip.Entries)
        {
            zipEntryNames.Add(entry.FullName);
            // Alle XG-IDs im Filename sammeln (Name + FullName als Fallback)
            var matches = xgRegex.Matches(entry.Name);
            if (matches.Count == 0) matches = xgRegex.Matches(entry.FullName);
            foreach (Match m in matches)
            {
                var key = "XG" + m.Groups[1].Value;
                // Erstes Match pro Key gewinnt (falls dieselbe ID in mehreren Files steht)
                if (!zipByXg.ContainsKey(key)) zipByXg[key] = entry;
            }
        }

        // Cache: Branches, Mitarbeiter, Taxonomie
        var branches = await _db.CompanyProfiles
            .Select(c => new { c.Id, c.RestaurantCode, c.BranchName })
            .ToListAsync();
        var employees = await _db.Employees
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.DateOfBirth })
            .ToListAsync();
        var kategorien = await _db.DokumentKategorien.Where(k => k.Aktiv).ToListAsync();
        var typen = await _db.DokumentTypen.Where(t => t.Aktiv).ToListAsync();

        // Vorhandene Documents pro Employee zur Duplikat-Erkennung
        var existingDocs = await _db.EmployeeDokumente
            .Select(d => new { d.EmployeeId, d.FilenameOriginal })
            .ToListAsync();

        result.TotalRows = csvLines.Count - 1;

        for (int i = 1; i < csvLines.Count; i++)
        {
            var fields = ParseCsvLine(csvLines[i], ';');
            string F(int c) => c >= 0 && c < fields.Count ? fields[c].Trim() : "";

            var row = new DvelopRow {
                RowNum = i,
                XgId = F(colDokId).Trim('"'),
                Filename = F(colDateiname).Trim('"'),
                EmployeeName = $"{F(colVorname)} {F(colNachname)}".Trim(),
                Bemerkung = F(colBeschr),
                GueltigVon = ParseDate(F(colErstelltAm))
            };
            // Geburtsdatum: erst aus eigener Spalte, sonst aus Combined-Feld
            row.DateOfBirth = ParseDate(F(colGebDatum));
            if (row.DateOfBirth is null)
            {
                var combined = F(colMaCombined);
                var dateMatch = Regex.Match(combined, @"(\d{4}-\d{2}-\d{2}|\d{2}\.\d{2}\.\d{4})");
                if (dateMatch.Success) row.DateOfBirth = ParseDate(dateMatch.Value);
            }

            // Alle Zeilen gehen zum gewählten MA (Walter importiert pro Mitarbeiter).
            // Sanity-Check: stimmen Name-Felder im CSV mit dem ausgewählten MA überein?
            row.EmployeeId = selectedEmp.Id;
            var csvVorname = F(colVorname);
            var csvNachname = F(colNachname);
            bool sanityWarning = !string.IsNullOrEmpty(csvVorname) &&
                                 !selectedEmp.FirstName.Equals(csvVorname, StringComparison.OrdinalIgnoreCase);

            // 2) Filiale aus Mandant ("58 McDonald's Restaurant Oftringen" → 058)
            var mandant = F(colMandant);
            var mandantNum = Regex.Match(mandant, @"^\s*(\d+)").Groups[1].Value;
            if (!string.IsNullOrEmpty(mandantNum))
            {
                var rc = mandantNum.PadLeft(3, '0');
                var br = branches.FirstOrDefault(b => b.RestaurantCode == rc);
                if (br != null) row.BranchCode = rc;
            }
            if (row.BranchCode == null)
            {
                row.Action = "skip-no-branch";
                row.Reason = $"Filiale aus '{mandant}' nicht gefunden";
                result.SkippedNoBranch++;
                result.Preview.Add(row);
                continue;
            }

            // 3) Kategorie + Typ matchen
            var kategorieRaw = F(colKategorie).Replace("HR:", "").Trim();
            var kat = MatchKategorie(kategorieRaw, kategorien);
            if (kat == null)
            {
                row.Action = "skip-no-category";
                row.Reason = $"Kategorie '{kategorieRaw}' nicht in unserer Taxonomie";
                result.SkippedNoCategory++;
                result.Preview.Add(row);
                continue;
            }
            row.KategorieName = kat.Name;

            // Sub-Typ aus passender Spalte
            string typRaw = kat.Name switch {
                "Absenzen"             => F(colTypAbs),
                "Ämter & Behörden"     => F(colTypAem),
                "Persönliche Angaben"  => F(colTypPers),
                "Vertragsunterlagen"   => F(colTypVert),
                _ => ""
            };
            var typ = MatchTyp(typRaw, kat.Id, typen);
            // Falls Typ leer oder nicht gefunden: Fallback auf "Diverses" der Kategorie
            if (typ == null)
            {
                typ = typen.FirstOrDefault(t => t.KategorieId == kat.Id && t.Name == "Diverses");
            }
            if (typ == null)
            {
                row.Action = "skip-no-category";
                row.Reason = $"Typ '{typRaw}' nicht gefunden, kein 'Diverses' als Fallback";
                result.SkippedNoCategory++;
                result.Preview.Add(row);
                continue;
            }
            row.TypName = typ.Name;
            row.DokumentTypId = typ.Id;

            // 4) ZIP-Entry finden via XG-ID
            if (!zipByXg.TryGetValue(row.XgId, out var entry))
            {
                // Diagnose: such ähnliche Filenames im ZIP (mit denselben letzten 4 Ziffern)
                var idTail = row.XgId.Length >= 4 ? row.XgId.Substring(row.XgId.Length - 4) : row.XgId;
                var similar = zipEntryNames
                    .Where(n => n.Contains(idTail, StringComparison.OrdinalIgnoreCase))
                    .Take(3)
                    .ToList();
                row.Action = "skip-no-file";
                row.Reason = similar.Any()
                    ? $"Datei mit ID {row.XgId} nicht erkannt. Ähnliche im ZIP: {string.Join(" | ", similar)}"
                    : $"Datei mit ID {row.XgId} nicht im ZIP (kein Filename mit {idTail} gefunden)";
                result.SkippedNoFile++;
                result.Preview.Add(row);
                continue;
            }

            // 5) Duplikat-Check (Employee + Original-Filename)
            var fnOrig = string.IsNullOrEmpty(row.Filename) ? entry.Name : row.Filename;
            if (existingDocs.Any(d => d.EmployeeId == selectedEmp.Id && d.FilenameOriginal == fnOrig))
            {
                row.Action = "skip-duplicate";
                row.Reason = $"Schon vorhanden: {fnOrig}";
                result.SkippedDuplicate++;
                result.Preview.Add(row);
                continue;
            }

            // 6) Importieren!
            row.Action = "create";
            if (sanityWarning)
                row.Reason = $"Hinweis: CSV-Name ({csvVorname} {csvNachname}) ≠ ausgewählter MA ({selectedEmp.FirstName} {selectedEmp.LastName})";
            if (!dryRun)
            {
                var ext = Path.GetExtension(fnOrig);
                if (string.IsNullOrEmpty(ext)) ext = ".pdf";
                var storageName = Guid.NewGuid().ToString("N") + ext;

                var empDir = Path.Combine(_storagePath, row.BranchCode!, selectedEmp.Id.ToString());
                Directory.CreateDirectory(empDir);
                var fullPath = Path.Combine(empDir, storageName);

                await using (var inS = entry.Open())
                await using (var outS = System.IO.File.Create(fullPath))
                {
                    await inS.CopyToAsync(outS);
                }

                var doc = new EmployeeDokument {
                    EmployeeId = selectedEmp.Id,
                    DokumentTypId = typ.Id,
                    BranchCode = row.BranchCode,
                    FilenameOriginal = fnOrig,
                    FilenameStorage = storageName,
                    MimeType = string.IsNullOrEmpty(F(colMime)) ? "application/pdf" : F(colMime),
                    GroesseBytes = long.TryParse(F(colGroesse), out var sz) ? sz : new FileInfo(fullPath).Length,
                    Bemerkung = string.IsNullOrWhiteSpace(row.Bemerkung) ? null : row.Bemerkung,
                    GueltigVon = row.GueltigVon,
                    HochgeladenVon = GetCurrentUserId(),
                    HochgeladenAm = DateTime.UtcNow
                };
                _db.EmployeeDokumente.Add(doc);
                await _db.SaveChangesAsync();

                // In existing-Cache aufnehmen für nachfolgende Zeilen
                existingDocs.Add(new { EmployeeId = selectedEmp.Id, FilenameOriginal = fnOrig });
            }
            result.Imported++;
            result.Preview.Add(row);
        }

        return Ok(result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────
    private static List<string> ParseCsvLine(string line, char sep)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else sb.Append(c);
            }
            else
            {
                if (c == sep) { result.Add(sb.ToString()); sb.Clear(); }
                else if (c == '"') inQuotes = true;
                else sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }

    private static DateOnly? ParseDate(string s)
    {
        s = s.Trim('"', ' ');
        if (string.IsNullOrEmpty(s)) return null;
        // ISO 1995-01-01 oder mit Zeit
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1)) return d1;
        // CH 01.01.1995
        if (DateOnly.TryParseExact(s, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) return d2;
        // CH mit Zeit "16.02.2026 09:12:51"
        if (DateTime.TryParseExact(s, new[] { "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy H:mm:ss", "yyyy-MM-dd HH:mm:ss" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt1)) return DateOnly.FromDateTime(dt1);
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }

    /// <summary>Match Kategorie via Name (toleriert Whitespace + Case).</summary>
    private static DokumentKategorie? MatchKategorie(string name, List<DokumentKategorie> all)
    {
        name = name.Trim();
        return all.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Match Typ-Name innerhalb einer Kategorie. Toleriert Plural/Singular.
    /// "Aufenthaltsbewilligungen" matcht "Aufenthaltsbewilligung".
    /// </summary>
    private static DokumentTyp? MatchTyp(string name, int kategorieId, List<DokumentTyp> all)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        var nameLower = name.ToLowerInvariant();

        var inKat = all.Where(t => t.KategorieId == kategorieId).ToList();

        // Direkter Match
        var exact = inKat.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // Plural-Toleranz: DB "Aufenthaltsbewilligung" matcht File "Aufenthaltsbewilligungen"
        var plural = inKat.FirstOrDefault(t => string.Equals(t.Name + "en", name, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(t.Name + "n",  name, StringComparison.OrdinalIgnoreCase));
        if (plural != null) return plural;

        // Singular-Toleranz: DB "Bewilligungen" matcht File "Bewilligung"
        var singular = inKat.FirstOrDefault(t =>
            (t.Name.EndsWith("en", StringComparison.OrdinalIgnoreCase) && string.Equals(t.Name[..^2], name, StringComparison.OrdinalIgnoreCase)) ||
            (t.Name.EndsWith("n",  StringComparison.OrdinalIgnoreCase) && string.Equals(t.Name[..^1], name, StringComparison.OrdinalIgnoreCase)));
        return singular;
    }

    private int? GetCurrentUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }
}

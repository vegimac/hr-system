using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Globalization;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;

namespace HrSystem.Controllers;

/// <summary>
/// Importiert Dokumente aus einem d.velop-Export (Metadaten als CSV oder XLSX
/// + ZIP mit den eigentlichen Dokumenten).
/// Match-Logik:
///   - Mitarbeiter via Vorname + Nachname + Geburtsdatum (matcht "alt"-suffixed)
///   - Filiale via "Mandant"-Spalte (z.B. "58 McDonald's Restaurant Oftringen" → 058)
///   - Kategorie via "Kategorie"-Spalte (Prefix "HR: " entfernen)
///   - Typ via passende Sub-Spalte ("Dokumenttyp Absenzen" etc.)
///   - File via XG-ID aus "Dokument-ID" → Match in ZIP-Filenames
///
/// Format-Erkennung: CSV (Semikolon-getrennt, UTF-8) ODER XLSX (Sheet
/// "ExcelExport" oder erstes Sheet) — automatisch via Datei-Endung. Beide
/// liefern dieselben Spalten-Header, der Parser baut intern eine einheitliche
/// rows-Struktur, der Rest der Logik ist Format-agnostisch.
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
        [FromForm] bool dryRun = true,
        [FromForm] string? rowOverrides = null)
    {
        if (employeeId <= 0) return BadRequest("Mitarbeiter muss vor dem Import ausgewählt werden.");
        var selectedEmp = await _db.Employees.FindAsync(employeeId);
        if (selectedEmp == null) return BadRequest("Gewählter Mitarbeiter nicht gefunden.");
        if (csvFile == null || csvFile.Length == 0) return BadRequest("Metadaten-Datei (CSV oder XLSX) fehlt.");
        if (zipFile == null || zipFile.Length == 0) return BadRequest("ZIP fehlt.");

        // Per-Row MA-Overrides aus dem Frontend: Walter kann im Preview pro
        // Datei einen anderen MA wählen als den global ausgewählten. Format:
        // JSON-Map { "XG00010269": 1379, "XG00003684": 1387, … }.
        // Kein Override für eine Zeile → Fallback auf selectedEmp.
        Dictionary<string, int> overrides = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(rowOverrides))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(rowOverrides!);
                if (parsed != null) overrides = new Dictionary<string, int>(parsed, StringComparer.OrdinalIgnoreCase);
            }
            catch { /* ignorieren — defekte JSON heisst einfach „keine Overrides" */ }
        }
        // Alle übergebenen Override-MAs einmal vorab laden, damit wir pro Row
        // nicht einzeln in die DB müssen. Plus Cache für BranchCode-Lookup.
        var overrideEmps = overrides.Values.Distinct().ToList();
        var overrideEmpsById = overrideEmps.Count > 0
            ? await _db.Employees
                .Include(e => e.Employments).ThenInclude(em => em.CompanyProfile)
                .Where(e => overrideEmps.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id)
            : new Dictionary<int, Employee>();

        var result = new DvelopResult { DryRun = dryRun };

        // Metadaten einlesen — CSV (Semikolon) oder XLSX. Endung entscheidet,
        // bei .xlsx fällt der Parser zurück auf NPOI-XSSFWorkbook, sonst CSV.
        List<string> headers;
        List<List<string>> dataRows;
        try
        {
            (headers, dataRows) = ReadMetadataFile(csvFile);
        }
        catch (Exception ex)
        {
            return BadRequest($"Metadaten-Datei konnte nicht gelesen werden: {ex.Message}");
        }
        if (headers.Count == 0 || dataRows.Count == 0)
            return BadRequest("Metadaten-Datei ist leer oder unvollständig.");

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
            return BadRequest("Metadaten-Datei hat nicht die erwarteten Spalten (Dokument-ID, Vorname, Nachname, Geburtsdatum, Kategorie).");

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

        result.TotalRows = dataRows.Count;

        for (int i = 0; i < dataRows.Count; i++)
        {
            var fields = dataRows[i];
            string F(int c) => c >= 0 && c < fields.Count ? (fields[c] ?? "").Trim() : "";

            var row = new DvelopRow {
                RowNum = i + 1,   // 1-basiert für UI-Anzeige (Zeile 1 = erste Datenzeile)
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

            // Ziel-MA bestimmen: per-Row-Override aus dem Preview > globaler
            // selectedEmp. Walter kann in der Preview-Tabelle pro Datei einen
            // anderen MA wählen, alle anderen gehen wie gewohnt zu selectedEmp.
            Employee targetEmp = selectedEmp;
            if (overrides.TryGetValue(row.XgId, out var overrideEmpId)
                && overrideEmpsById.TryGetValue(overrideEmpId, out var ovEmp))
            {
                targetEmp = ovEmp;
            }
            row.EmployeeId = targetEmp.Id;
            var csvVorname = F(colVorname);
            var csvNachname = F(colNachname);
            bool sanityWarning = !string.IsNullOrEmpty(csvVorname) &&
                                 !targetEmp.FirstName.Equals(csvVorname, StringComparison.OrdinalIgnoreCase);

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

            // 5) Duplikat-Check (Employee + Original-Filename) — pro tatsächlichem Ziel-MA
            var fnOrig = string.IsNullOrEmpty(row.Filename) ? entry.Name : row.Filename;
            if (existingDocs.Any(d => d.EmployeeId == targetEmp.Id && d.FilenameOriginal == fnOrig))
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
                row.Reason = $"Hinweis: CSV-Name ({csvVorname} {csvNachname}) ≠ Ziel-MA ({targetEmp.FirstName} {targetEmp.LastName})";
            if (!dryRun)
            {
                var ext = Path.GetExtension(fnOrig);
                if (string.IsNullOrEmpty(ext)) ext = ".pdf";
                var storageName = Guid.NewGuid().ToString("N") + ext;

                var empDir = Path.Combine(_storagePath, row.BranchCode!, targetEmp.Id.ToString());
                Directory.CreateDirectory(empDir);
                var fullPath = Path.Combine(empDir, storageName);

                await using (var inS = entry.Open())
                await using (var outS = System.IO.File.Create(fullPath))
                {
                    await inS.CopyToAsync(outS);
                }

                var doc = new EmployeeDokument {
                    EmployeeId = targetEmp.Id,
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
                existingDocs.Add(new { EmployeeId = targetEmp.Id, FilenameOriginal = fnOrig });
            }
            result.Imported++;
            result.Preview.Add(row);
        }

        return Ok(result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Liest die Metadaten-Datei (CSV oder XLSX) und liefert (Header-Zeile,
    /// Daten-Zeilen). Format wird via Datei-Endung erkannt — .xlsx geht
    /// durch NPOI-XSSFWorkbook (erstes Sheet, idR. „ExcelExport"), alles
    /// andere wird als CSV mit Semikolon-Separator gelesen (UTF-8).
    /// </summary>
    private static (List<string> Headers, List<List<string>> Rows) ReadMetadataFile(IFormFile file)
    {
        var name = (file.FileName ?? "").ToLowerInvariant();
        if (name.EndsWith(".xlsx") || name.EndsWith(".xlsm"))
            return ReadXlsx(file);
        return ReadCsv(file);
    }

    private static (List<string> Headers, List<List<string>> Rows) ReadCsv(IFormFile file)
    {
        using var sr = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var lines = new List<string>();
        string? cl;
        while ((cl = sr.ReadLine()) != null) lines.Add(cl);

        var headers = lines.Count > 0 ? ParseCsvLine(lines[0], ';') : new List<string>();
        var rows    = new List<List<string>>();
        for (int i = 1; i < lines.Count; i++)
            rows.Add(ParseCsvLine(lines[i], ';'));
        return (headers, rows);
    }

    private static (List<string> Headers, List<List<string>> Rows) ReadXlsx(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var wb = new XSSFWorkbook(stream);
        // Bevorzugt das „ExcelExport"-Sheet (d.velop-Export-Default), sonst Sheet 0.
        ISheet? sheet = null;
        for (int s = 0; s < wb.NumberOfSheets; s++)
        {
            var sh = wb.GetSheetAt(s);
            if (string.Equals(sh.SheetName, "ExcelExport", StringComparison.OrdinalIgnoreCase))
            { sheet = sh; break; }
        }
        sheet ??= wb.GetSheetAt(0);
        if (sheet == null) return (new List<string>(), new List<List<string>>());

        var headerRow = sheet.GetRow(0);
        var headers = new List<string>();
        if (headerRow != null)
        {
            for (int c = 0; c < headerRow.LastCellNum; c++)
                headers.Add((headerRow.GetCell(c)?.ToString() ?? "").Trim());
        }

        var rows = new List<List<string>>();
        for (int r = 1; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) { rows.Add(new List<string>()); continue; }
            var fields = new List<string>(headers.Count);
            for (int c = 0; c < headers.Count; c++)
            {
                var cell = row.GetCell(c);
                fields.Add(CellToString(cell));
            }
            // Komplett-leere Zeile überspringen (XLSX hat oft Trail-Empty-Rows)
            if (fields.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(fields);
        }
        return (headers, rows);
    }

    /// <summary>
    /// Robustes Cell-zu-String — bei Datums-Cells nutzen wir das ISO-Format
    /// damit ParseDate() die Werte ohne Locale-Tricks erkennt. Numerische
    /// Cells werden ohne Tausender-Trenner ausgegeben.
    /// </summary>
    private static string CellToString(ICell? cell)
    {
        if (cell == null) return "";
        return cell.CellType switch
        {
            CellType.String  => cell.StringCellValue ?? "",
            CellType.Boolean => cell.BooleanCellValue ? "true" : "false",
            CellType.Numeric => DateUtil.IsCellDateFormatted(cell)
                                  ? (cell.DateCellValue?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")
                                  : cell.NumericCellValue.ToString(CultureInfo.InvariantCulture),
            CellType.Formula => cell.CachedFormulaResultType == CellType.Numeric
                                  ? cell.NumericCellValue.ToString(CultureInfo.InvariantCulture)
                                  : (cell.StringCellValue ?? ""),
            CellType.Blank   => "",
            _ => cell.ToString() ?? ""
        };
    }

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

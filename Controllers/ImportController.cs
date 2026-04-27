using System.IO.Compression;
using System.Text.RegularExpressions;
using HrSystem.Data;
using HrSystem.Models;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/import")]
public class ImportController : ControllerBase
{
    private readonly AppDbContext _db;
    public ImportController(AppDbContext db) => _db = db;

    // GET /api/import/stempelzeiten/count  — Anzahl Einträge in DB
    [HttpGet("stempelzeiten/count")]
    public async Task<IActionResult> GetCount()
    {
        var count = await _db.EmployeeTimeEntries.CountAsync();
        return Ok(new { count });
    }

    // POST /api/import/stempelzeiten/dedupe — löscht Duplikate, behält je
    // (employee_id, time_in) den Eintrag mit der niedrigsten Id.
    [HttpPost("stempelzeiten/dedupe")]
    public async Task<IActionResult> DedupeStempelzeiten()
    {
        var beforeCount = await _db.EmployeeTimeEntries.CountAsync();

        // Raw-SQL ist hier effizienter als LINQ-Ladeoperationen
        var deleted = await _db.Database.ExecuteSqlRawAsync(@"
            DELETE FROM employee_time_entry
            WHERE id NOT IN (
                SELECT MIN(id)
                FROM employee_time_entry
                GROUP BY employee_id, time_in
            )
        ");

        return Ok(new {
            before   = beforeCount,
            deleted  = deleted,
            after    = beforeCount - deleted
        });
    }

    // POST /api/import/stempelzeiten/preview  — Zeigt was der Parser aus dem PDF liest
    [HttpPost("stempelzeiten/preview")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> PreviewStempelzeiten(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });

        // Datei-Bytes einmal einlesen, dann zwei MemoryStreams daraus bauen
        using var buf = new MemoryStream();
        await file.CopyToAsync(buf);
        var bytes = buf.ToArray();

        // 1) Roher Text (erste 3 Seiten)
        var rawLines = new List<string>();
        try
        {
            using var ms1 = new MemoryStream(bytes);
            var reader = new PdfReader(ms1);
            var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);
            for (int i = 1; i <= Math.Min(pdfDoc.GetNumberOfPages(), 3); i++)
            {
                var strategy = new LocationTextExtractionStrategy();
                var pageText = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i), strategy);
                rawLines.Add($"=== Seite {i} ===");
                foreach (var line in pageText.Split('\n').Take(50))
                    rawLines.Add(line);
            }
            pdfDoc.Close();
        }
        catch (Exception ex) { rawLines.Add($"Fehler beim Text-Lesen: {ex.Message}"); }

        // 2) Parser-Ergebnis
        var entries = new List<object>();
        int totalParsed = 0;
        try
        {
            using var ms2 = new MemoryStream(bytes);
            var parsed = ParseStempelzeitenPdf(ms2);
            totalParsed = parsed.Count;
            entries = parsed.Take(5).Select(e => (object)new {
                emp      = e.EmployeeNumber,
                timeIn   = e.TimeIn,
                timeOut  = e.TimeOut,
                duration = e.DurationHours
            }).ToList();
        }
        catch (Exception ex) { entries.Add(new { error = ex.Message }); }

        return Ok(new { rawLines, parsedSample = entries, totalParsed });
    }

    // POST /api/import/stempelzeiten
    [HttpPost("stempelzeiten")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> ImportStempelzeiten(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Nur PDF-Dateien werden unterstützt." });

        // ── PDF parsen ──────────────────────────────────────────────────
        List<PdfTimeEntry> entries;
        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            ms.Position = 0;
            entries = ParseStempelzeitenPdf(ms);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"PDF konnte nicht gelesen werden: {ex.Message}" });
        }

        if (entries.Count == 0)
            return Ok(new ImportResult { Imported = 0, Skipped = 0, UnknownEmployees = [] });

        // ── Mitarbeiter-Map laden ───────────────────────────────────────
        var empNumbers = entries.Select(e => e.EmployeeNumber).Distinct().ToList();
        var empMap = await _db.Employees
            .Where(e => empNumbers.Contains(e.EmployeeNumber))
            .ToDictionaryAsync(e => e.EmployeeNumber!, e => e.Id);

        // ── Bestehende Einträge als HashSet für Duplikat-Erkennung ──────
        var empIds = empMap.Values.ToList();
        var existingKeys = (await _db.EmployeeTimeEntries
            .Where(t => empIds.Contains(t.EmployeeId))
            .Select(t => new { t.EmployeeId, t.TimeIn })
            .ToListAsync())
            .Select(t => $"{t.EmployeeId}|{t.TimeIn:yyyy-MM-ddTHH:mm:ss}")
            .ToHashSet();

        // ── Einfügen ────────────────────────────────────────────────────
        int imported = 0, skipped = 0;
        var unknownEmployees = new HashSet<string>();

        foreach (var e in entries)
        {
            if (!empMap.TryGetValue(e.EmployeeNumber, out var empId))
            {
                unknownEmployees.Add(e.EmployeeNumber);
                continue;
            }

            var key = $"{empId}|{e.TimeIn:yyyy-MM-ddTHH:mm:ss}";
            if (existingKeys.Contains(key)) { skipped++; continue; }

            existingKeys.Add(key);

            // Stempelzeiten werden als Lokalzeit gespeichert (timestamp ohne TZ)
            var timeIn  = DateTime.SpecifyKind(e.TimeIn,  DateTimeKind.Unspecified);
            var timeOut = DateTime.SpecifyKind(e.TimeOut, DateTimeKind.Unspecified);

            _db.EmployeeTimeEntries.Add(new EmployeeTimeEntry
            {
                EmployeeId    = empId,
                EntryDate     = DateOnly.FromDateTime(timeIn),
                TimeIn        = timeIn,
                TimeOut       = timeOut,
                Comment       = e.Comment,
                DurationHours = e.DurationHours,
                NightHours    = e.NightHours ?? 0,
                TotalHours    = e.TotalHours,
                Source        = "import",
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow,
            });
            imported++;

            if (imported % 200 == 0)
                await _db.SaveChangesAsync();
        }

        await _db.SaveChangesAsync();

        return Ok(new ImportResult
        {
            Imported         = imported,
            Skipped          = skipped,
            UnknownEmployees = [.. unknownEmployees.Order()]
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // PDF PARSER — iText7, reines C#, keine Python-Abhängigkeit
    // ══════════════════════════════════════════════════════════════════

    private static readonly Regex RxEmployee =
        new(@"^(.+?)\s+#(\d+)\s*$", RegexOptions.Compiled);
    private static readonly Regex RxDate =
        new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);
    private static readonly Regex RxTime =
        new(@"^\d{2}:\d{2}:\d{2}$", RegexOptions.Compiled);
    private static readonly Regex RxHHMM =
        new(@"^\d{2}:\d{2}$", RegexOptions.Compiled);

    private static readonly Regex RxTruncDate    = new(@"^(\d{4}-\d{2})-$", RegexOptions.Compiled);
    private static readonly Regex RxWrapNextLine = new(@"^(\d{2})\s+(\d{2}:\d{2}:\d{2})\s+(\d{2}:\d{2}:\d{2})\b", RegexOptions.Compiled);

    private static List<PdfTimeEntry> ParseStempelzeitenPdf(Stream stream)
    {
        var result    = new List<PdfTimeEntry>();
        string? currentEmp = null;

        var reader = new PdfReader(stream);
        var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);

        for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
        {
            var strategy = new LocationTextExtractionStrategy();
            var pageText = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i), strategy);

            // Wrap-Lines zusammenführen: bei langen Kommentaren bricht Mirus die
            // Datum/Zeit-Spalten in 2 physische Zeilen um (siehe MergeWrappedLines).
            var lines = pageText.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();
            var merged = MergeWrappedLines(lines);

            foreach (var line in merged)
            {
                // Mitarbeiter-Header: "Aleksandra Tomova #580003"
                var mEmp = RxEmployee.Match(line);
                if (mEmp.Success)
                {
                    currentEmp = mEmp.Groups[2].Value;
                    continue;
                }

                if (currentEmp == null) continue;

                var entry = TryParseEntryLine(line, currentEmp);
                if (entry != null) result.Add(entry);
            }
        }

        pdfDoc.Close();
        return result;
    }

    /// <summary>
    /// Mirus-PDFs brechen Tabellenzeilen in 2 physische Zeilen um, wenn ein
    /// Kommentar zu lang ist (Datum-Spalte zu schmal). Diese Methode erkennt
    /// das und führt sie wieder zu einer logischen Zeile zusammen.
    ///
    /// Vorher (iText/pdfplumber):
    ///   "2026-01- 2026-01-01 2026-01-01 Ja Falsch gestempelt . 04:13 00:00 4.22"
    ///   "01 09:30:01 13:42:59"
    /// Nachher:
    ///   "2026-01-01 2026-01-01 09:30:01 2026-01-01 13:42:59 Ja Falsch gestempelt . 04:13 00:00 4.22"
    /// </summary>
    private static List<string> MergeWrappedLines(List<string> lines)
    {
        var merged = new List<string>();
        int i = 0;
        while (i < lines.Count)
        {
            var line = lines[i];
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Trunkiertes Datum als erstes Token? "2026-01-" (endet mit "-")
            bool isTruncated = tokens.Length >= 3 && RxTruncDate.IsMatch(tokens[0]);
            if (isTruncated && i + 1 < lines.Count)
            {
                var nextLine  = lines[i + 1];
                var nextMatch = RxWrapNextLine.Match(nextLine);
                if (nextMatch.Success)
                {
                    var day      = nextMatch.Groups[1].Value;          // "01"
                    var inTime   = nextMatch.Groups[2].Value;          // "09:30:01"
                    var outTime  = nextMatch.Groups[3].Value;          // "13:42:59"
                    var fullDate = $"{tokens[0]}{day}";                 // "2026-01-" + "01"
                    var rest     = tokens.Length > 3 ? string.Join(' ', tokens.Skip(3)) : "";
                    var combined = $"{fullDate} {tokens[1]} {inTime} {tokens[2]} {outTime}"
                                   + (rest.Length > 0 ? " " + rest : "");
                    merged.Add(combined);
                    i += 2;
                    continue;
                }
            }
            merged.Add(line);
            i++;
        }
        return merged;
    }

    private static PdfTimeEntry? TryParseEntryLine(string line, string employeeNumber)
    {
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 8)              return null;
        if (!RxDate.IsMatch(tokens[0]))     return null;
        if (tokens.Contains("Gesamt"))      return null;

        // Letzte 3 Token: decimal  HH:MM  HH:MM
        if (!double.TryParse(tokens[^1],
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var total))             return null;

        var nightStr    = tokens[^2];
        var durationStr = tokens[^3];
        if (!RxHHMM.IsMatch(nightStr) || !RxHHMM.IsMatch(durationStr)) return null;

        // Token 1–4: date time  date time
        if (!RxDate.IsMatch(tokens[1]) || !RxTime.IsMatch(tokens[2])) return null;
        if (!RxDate.IsMatch(tokens[3]) || !RxTime.IsMatch(tokens[4])) return null;

        if (!DateTime.TryParse($"{tokens[1]} {tokens[2]}", out var timeIn))  return null;
        if (!DateTime.TryParse($"{tokens[3]} {tokens[4]}", out var timeOut)) return null;

        // Optionaler Kommentar zwischen Token 5 und den letzten 3
        string? comment = tokens.Length > 8
            ? string.Join(" ", tokens[5..(tokens.Length - 3)])
            : null;

        return new PdfTimeEntry(
            EmployeeNumber: employeeNumber,
            TimeIn:         timeIn,
            TimeOut:        timeOut,
            Comment:        string.IsNullOrWhiteSpace(comment) ? null : comment,
            DurationHours:  HhmmToDecimal(durationStr),
            NightHours:     HhmmToDecimal(nightStr),
            TotalHours:     (decimal)total
        );
    }

    private static decimal? HhmmToDecimal(string s)
    {
        if (!RxHHMM.IsMatch(s)) return null;
        var p = s.Split(':');
        return Math.Round(int.Parse(p[0]) + int.Parse(p[1]) / 60m, 4);
    }

    // ══════════════════════════════════════════════════════════════════
    // MONATLICHES FORMAT — 1 PDF pro Mitarbeiter, optional als ZIP
    // Header:  "Name"  +  Zeile "Employee #Nummer"
    // Zeilen:  "YYYY-MM-DD  HH:MM:SS  HH:MM:SS  NH:MM  NH:MM  [Kommentar]"
    // ══════════════════════════════════════════════════════════════════

    // Employee-Header kann in einer Zeile mit dem Namen stehen:
    // "Adelina Duqi                                          Employee #580001"
    private static readonly Regex RxEmployeeLine =
        new(@"\bEmployee\s+#(\d+)\b", RegexOptions.Compiled);

    // POST /api/import/stempelzeiten-monatlich/preview
    [HttpPost("stempelzeiten-monatlich/preview")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> PreviewMonatlich(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });

        List<PdfTimeEntry> entries;
        int filesProcessed, filesEmpty;
        List<string> parseErrors;
        try
        {
            (entries, filesProcessed, filesEmpty, parseErrors) = await ExtractMonatlichAsync(file);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }

        return Ok(new {
            filesProcessed,
            filesEmpty,
            totalParsed  = entries.Count,
            parseErrors,
            sample = entries.Take(10).Select(e => new {
                emp      = e.EmployeeNumber,
                timeIn   = e.TimeIn,
                timeOut  = e.TimeOut,
                duration = e.DurationHours,
                night    = e.NightHours,
                comment  = e.Comment
            })
        });
    }

    // POST /api/import/stempelzeiten-monatlich
    [HttpPost("stempelzeiten-monatlich")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> ImportMonatlich(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });

        List<PdfTimeEntry> entries;
        int filesProcessed, filesEmpty;
        List<string> parseErrors;
        try
        {
            (entries, filesProcessed, filesEmpty, parseErrors) = await ExtractMonatlichAsync(file);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }

        if (entries.Count == 0)
        {
            return Ok(new MonatlichImportResult {
                Imported         = 0,
                Skipped          = 0,
                UnknownEmployees = [],
                FilesProcessed   = filesProcessed,
                FilesEmpty       = filesEmpty,
                ParseErrors      = parseErrors
            });
        }

        // Mitarbeiter-Map
        var empNumbers = entries.Select(e => e.EmployeeNumber).Distinct().ToList();
        var empMap = await _db.Employees
            .Where(e => empNumbers.Contains(e.EmployeeNumber))
            .ToDictionaryAsync(e => e.EmployeeNumber!, e => e.Id);

        // Duplikat-Key = EmployeeId + TimeIn (ISO)
        var empIds = empMap.Values.ToList();
        var existingKeys = (await _db.EmployeeTimeEntries
            .Where(t => empIds.Contains(t.EmployeeId))
            .Select(t => new { t.EmployeeId, t.TimeIn })
            .ToListAsync())
            .Select(t => $"{t.EmployeeId}|{t.TimeIn:yyyy-MM-ddTHH:mm:ss}")
            .ToHashSet();

        int imported = 0, skipped = 0;
        var unknownEmployees = new HashSet<string>();

        foreach (var e in entries)
        {
            if (!empMap.TryGetValue(e.EmployeeNumber, out var empId))
            {
                unknownEmployees.Add(e.EmployeeNumber);
                continue;
            }

            var key = $"{empId}|{e.TimeIn:yyyy-MM-ddTHH:mm:ss}";
            if (existingKeys.Contains(key)) { skipped++; continue; }
            existingKeys.Add(key);

            // Stempelzeiten werden als Lokalzeit gespeichert (timestamp ohne TZ)
            var timeIn  = DateTime.SpecifyKind(e.TimeIn,  DateTimeKind.Unspecified);
            var timeOut = DateTime.SpecifyKind(e.TimeOut, DateTimeKind.Unspecified);

            _db.EmployeeTimeEntries.Add(new EmployeeTimeEntry
            {
                EmployeeId    = empId,
                EntryDate     = DateOnly.FromDateTime(timeIn),
                TimeIn        = timeIn,
                TimeOut       = timeOut,
                Comment       = e.Comment,
                DurationHours = e.DurationHours,
                NightHours    = e.NightHours ?? 0,
                TotalHours    = e.TotalHours,
                Source        = "import",
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow,
            });
            imported++;

            if (imported % 200 == 0)
                await _db.SaveChangesAsync();
        }

        await _db.SaveChangesAsync();

        return Ok(new MonatlichImportResult
        {
            Imported         = imported,
            Skipped          = skipped,
            UnknownEmployees = [.. unknownEmployees.Order()],
            FilesProcessed   = filesProcessed,
            FilesEmpty       = filesEmpty,
            ParseErrors      = parseErrors
        });
    }

    // Extrahiert Einträge aus ZIP oder einzelnem PDF
    private static async Task<(List<PdfTimeEntry>, int processed, int empty, List<string> errors)>
        ExtractMonatlichAsync(IFormFile file)
    {
        var entries = new List<PdfTimeEntry>();
        int processed = 0, empty = 0;
        var errors = new List<string>();

        using var buf = new MemoryStream();
        await file.CopyToAsync(buf);
        var bytes = buf.ToArray();

        bool isZip = file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        if (isZip)
        {
            using var zipMs = new MemoryStream(bytes);
            using var zip   = new ZipArchive(zipMs, ZipArchiveMode.Read);
            foreach (var ent in zip.Entries)
            {
                if (!ent.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                using var pdfStream = ent.Open();
                using var pdfMs     = new MemoryStream();
                await pdfStream.CopyToAsync(pdfMs);
                pdfMs.Position = 0;
                processed++;
                try
                {
                    var pdfEntries = ParseMonatlichPdf(pdfMs);
                    if (pdfEntries.Count == 0) empty++;
                    else                       entries.AddRange(pdfEntries);
                }
                catch (Exception ex)
                {
                    errors.Add($"{ent.Name}: {ex.Message}");
                }
            }
        }
        else
        {
            using var ms = new MemoryStream(bytes);
            processed = 1;
            try
            {
                var pdfEntries = ParseMonatlichPdf(ms);
                if (pdfEntries.Count == 0) empty = 1;
                else                       entries.AddRange(pdfEntries);
            }
            catch (Exception ex)
            {
                errors.Add($"{file.FileName}: {ex.Message}");
            }
        }

        return (entries, processed, empty, errors);
    }

    private static List<PdfTimeEntry> ParseMonatlichPdf(Stream stream)
    {
        var result = new List<PdfTimeEntry>();
        string? currentEmp = null;

        var reader = new PdfReader(stream);
        var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);

        for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
        {
            var strategy = new LocationTextExtractionStrategy();
            var pageText = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i), strategy);

            foreach (var rawLine in pageText.Split('\n'))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // Header: "Employee #580010"
                var mEmp = RxEmployeeLine.Match(line);
                if (mEmp.Success)
                {
                    currentEmp = mEmp.Groups[1].Value;
                    continue;
                }

                if (currentEmp == null) continue;

                var entry = TryParseMonatlichEntryLine(line, currentEmp);
                if (entry != null) result.Add(entry);
            }
        }

        pdfDoc.Close();
        return result;
    }

    private static PdfTimeEntry? TryParseMonatlichEntryLine(string line, string employeeNumber)
    {
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Mindestens: Datum, IN, OUT, NightH, NormalH  (5 Tokens)
        if (tokens.Length < 5) return null;
        if (!RxDate.IsMatch(tokens[0])) return null;
        if (tokens.Contains("SUM")) return null;

        // Tokens 1 und 2: HH:MM:SS (IN/OUT)
        if (!RxTime.IsMatch(tokens[1]) || !RxTime.IsMatch(tokens[2])) return null;

        // Tokens 3 und 4: HH:MM (Night / Normal)
        if (!RxHHMM.IsMatch(tokens[3]) || !RxHHMM.IsMatch(tokens[4])) return null;

        var dateStr = tokens[0];
        var inStr   = tokens[1];
        var outStr  = tokens[2];
        var nightS  = tokens[3];
        var normalS = tokens[4];

        // Kommentar: zwischen Normal-Hours und optionalem "Edited"-Marker
        string? comment = null;
        if (tokens.Length > 5)
        {
            // Letztes Token kann "Edited"-Marker "Ja" sein — wir ignorieren es
            int endIdx = tokens.Length;
            if (tokens[^1] == "Ja") endIdx--;
            if (endIdx > 5)
                comment = string.Join(" ", tokens[5..endIdx]);
        }

        if (!DateTime.TryParse($"{dateStr} {inStr}",  out var timeIn))  return null;
        if (!DateTime.TryParse($"{dateStr} {outStr}", out var timeOut)) return null;

        // Wenn OUT vor IN → Schicht über Mitternacht, OUT auf Folgetag schieben
        if (timeOut < timeIn) timeOut = timeOut.AddDays(1);

        decimal? night    = HhmmToDecimal(nightS);
        decimal? duration = HhmmToDecimal(normalS);
        decimal  total    = (night ?? 0) + (duration ?? 0);

        return new PdfTimeEntry(
            EmployeeNumber: employeeNumber,
            TimeIn:         timeIn,
            TimeOut:        timeOut,
            Comment:        string.IsNullOrWhiteSpace(comment) ? null : comment,
            DurationHours:  duration,
            NightHours:     night,
            TotalHours:     total
        );
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────
public record PdfTimeEntry(
    string   EmployeeNumber,
    DateTime TimeIn,
    DateTime TimeOut,
    string?  Comment,
    decimal? DurationHours,
    decimal? NightHours,
    decimal  TotalHours
);

public record ImportResult
{
    public int          Imported         { get; init; }
    public int          Skipped          { get; init; }
    public List<string> UnknownEmployees { get; init; } = [];
}

public record MonatlichImportResult
{
    public int          Imported         { get; init; }
    public int          Skipped          { get; init; }
    public List<string> UnknownEmployees { get; init; } = [];
    public int          FilesProcessed   { get; init; }
    public int          FilesEmpty       { get; init; }
    public List<string> ParseErrors      { get; init; } = [];
}

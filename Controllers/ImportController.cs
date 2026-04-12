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

            var timeIn  = DateTime.SpecifyKind(e.TimeIn,  DateTimeKind.Utc);
            var timeOut = DateTime.SpecifyKind(e.TimeOut, DateTimeKind.Utc);

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

            foreach (var rawLine in pageText.Split('\n'))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

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

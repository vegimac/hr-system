using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace HrSystem.Controllers;

/// <summary>
/// Bewilligungslisten-Import (XLSX aus Mirus / Migrationsamt).
///
/// Erwartetes Format (Tabelle1, Spaltenkopf in Zeile 1):
///   Pers. Nr. | Name | Vorname | Bewilligung | Ablauf Bewilligung | Kostenstelle
///
/// Bewilligung-Klartext-Mapping:
///   "Niedergelassene (C)"        → C
///   "Jahresaufenthalter (B)"     → B
///   "Kurzaufenthalter (L)"       → L
///   "Schutzbedürftige (S)"       → S
///   "Vorläufig aufgenommene (F)" → F
///   "Asylsuchende (N)"           → N
///   "Grenzgänger (G)"            → G
///
/// Endpoints:
///   POST /api/imports/permit/preview  →  XLSX parsen, MA matchen, Vorschau
///   POST /api/imports/permit/commit   →  Update employee + Insert history
///
/// Auf bestehende Werte: existing employee.permit_type_id wird überschrieben.
/// Bei der History wird ein neuer Eintrag NUR angelegt, wenn der MA noch
/// keinen offenen Eintrag hat. Sonst wird der bestehende offene Eintrag
/// aktualisiert (Code + Ablauf), damit der Verlauf nicht mit duplizierten
/// "Initial"-Einträgen explodiert.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/imports/permit")]
public class PermitImportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<PermitImportController> _log;

    public PermitImportController(AppDbContext db, ILogger<PermitImportController> log)
    {
        _db = db;
        _log = log;
    }

    public class PreviewRow
    {
        public int RowNum { get; set; }
        public string EmployeeNumber { get; set; } = "";
        public string CsvFirstName { get; set; } = "";
        public string CsvLastName  { get; set; } = "";
        public string PermitText   { get; set; } = "";    // roher Klartext aus XLSX
        public string? PermitCode  { get; set; }          // gemappter Code (B/C/L/S/...)
        public DateOnly? PermitExpiry { get; set; }
        public string? Kostenstelle { get; set; }

        // Match-Resultat
        public int?    EmployeeId          { get; set; }
        public string? DbFirstName         { get; set; }
        public string? DbLastName          { get; set; }
        public string? CurrentPermitCode   { get; set; }
        public DateOnly? CurrentPermitExpiry { get; set; }

        public string Status { get; set; } = "OK"; // OK | NO_MATCH | UNKNOWN_PERMIT | NO_DATE
        public string? Note   { get; set; }
    }

    public class PreviewResponse
    {
        public List<PreviewRow> Rows { get; set; } = new();
        public int TotalRows { get; set; }
        public int Matched   { get; set; }
        public int NoMatch   { get; set; }
        public int Unknown   { get; set; }
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });

        var rows = await ParseAsync(file);
        if (rows == null) return BadRequest(new { error = "Konnte XLSX nicht parsen — ist das Format korrekt? (Erwartet: Pers. Nr. | Name | Vorname | Bewilligung | Ablauf Bewilligung | Kostenstelle)" });

        // Alle MA vorladen für Match
        var allEmps = await _db.Employees
            .AsNoTracking()
            .Include(e => e.PermitType)
            .Select(e => new {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
                e.PermitTypeId, PermitCode = e.PermitType != null ? e.PermitType.Code : null,
                e.PermitExpiryDate
            })
            .ToListAsync();

        var permitCodeToId = await _db.PermitTypes.AsNoTracking()
            .ToDictionaryAsync(p => p.Code, p => p.Id);

        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.PermitCode))
            {
                r.Status = "UNKNOWN_PERMIT";
                r.Note = $"Bewilligung '{r.PermitText}' konnte nicht zugeordnet werden.";
            }
            if (r.PermitExpiry == null && r.Status == "OK")
            {
                r.Status = "NO_DATE";
                r.Note = "Kein Ablaufdatum.";
            }

            var emp = allEmps.FirstOrDefault(e => e.EmployeeNumber == r.EmployeeNumber);
            if (emp == null)
            {
                if (r.Status == "OK") r.Status = "NO_MATCH";
                r.Note = (r.Note ?? "") + (r.Note != null ? " · " : "") + $"Personalnummer {r.EmployeeNumber} nicht gefunden.";
                continue;
            }
            r.EmployeeId          = emp.Id;
            r.DbFirstName         = emp.FirstName;
            r.DbLastName          = emp.LastName;
            r.CurrentPermitCode   = emp.PermitCode;
            r.CurrentPermitExpiry = emp.PermitExpiryDate.HasValue
                ? DateOnly.FromDateTime(emp.PermitExpiryDate.Value) : null;
        }

        return Ok(new PreviewResponse
        {
            Rows = rows,
            TotalRows = rows.Count,
            Matched   = rows.Count(r => r.Status == "OK"),
            NoMatch   = rows.Count(r => r.Status == "NO_MATCH"),
            Unknown   = rows.Count(r => r.Status == "UNKNOWN_PERMIT" || r.Status == "NO_DATE")
        });
    }

    [HttpPost("commit")]
    public async Task<IActionResult> Commit([FromForm] IFormFile file, [FromForm] string? validFrom = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });

        // Beginn-Datum für die Verlaufseinträge — die Bewilligungsliste enthält
        // selbst kein Beginn-Datum, daher gibt Walter es vor dem Import vor.
        // Pflicht, weil sonst willkürliche Defaults in der Verlaufstabelle
        // landen würden.
        if (string.IsNullOrWhiteSpace(validFrom)
         || !DateOnly.TryParse(validFrom, out var validFromDate))
        {
            return BadRequest(new { error = "Beginn-Datum (validFrom) ist erforderlich (Format YYYY-MM-DD)." });
        }

        var rows = await ParseAsync(file);
        if (rows == null) return BadRequest(new { error = "Konnte XLSX nicht parsen." });

        var permitCodeToId = await _db.PermitTypes.ToDictionaryAsync(p => p.Code, p => p.Id);
        var userId = GetCurrentUserId();

        int updated = 0, skipped = 0, historyAdded = 0, historyUpdated = 0;
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.PermitCode) || r.PermitExpiry == null)
            { skipped++; continue; }
            if (!permitCodeToId.TryGetValue(r.PermitCode, out var permitTypeId))
            { skipped++; continue; }

            var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeNumber == r.EmployeeNumber);
            if (emp == null) { skipped++; continue; }

            // 1. Stammdaten: Bewilligung + Ablauf updaten
            emp.PermitTypeId = permitTypeId;
            emp.PermitExpiryDate = r.PermitExpiry.Value.ToDateTime(TimeOnly.MinValue);

            // 2. History: bestehenden offenen Eintrag aktualisieren ODER neuen anlegen
            var openEntry = await _db.EmployeePermitHistories
                .Where(h => h.EmployeeId == emp.Id && h.ValidTo == null)
                .OrderByDescending(h => h.ValidFrom)
                .FirstOrDefaultAsync();

            if (openEntry != null)
            {
                // Bestehender offener Eintrag: aktualisieren wenn sich Code, Ablauf
                // oder Beginn-Datum (Walter-Vorgabe) ändert.
                bool codeChanged   = openEntry.PermitTypeId != permitTypeId;
                bool expiryChanged = openEntry.PermitExpiryDate != r.PermitExpiry;
                bool fromChanged   = openEntry.ValidFrom != validFromDate;
                if (codeChanged || expiryChanged || fromChanged)
                {
                    openEntry.PermitTypeId     = permitTypeId;
                    openEntry.PermitExpiryDate = r.PermitExpiry;
                    openEntry.ValidFrom        = validFromDate;
                    if (string.IsNullOrWhiteSpace(openEntry.Note))
                        openEntry.Note = "Aktualisiert via Bewilligungsliste-Import";
                    historyUpdated++;
                }
            }
            else
            {
                // Kein offener Eintrag: Initial-Eintrag anlegen — Beginn-Datum
                // kommt direkt aus dem Form-Feld.
                _db.EmployeePermitHistories.Add(new EmployeePermitHistory
                {
                    EmployeeId       = emp.Id,
                    PermitTypeId     = permitTypeId,
                    ValidFrom        = validFromDate,
                    ValidTo          = null,
                    PermitExpiryDate = r.PermitExpiry,
                    Note             = "Initial via Bewilligungsliste-Import",
                    CreatedAt        = DateTime.UtcNow,
                    CreatedByUserId  = userId
                });
                historyAdded++;
            }
            updated++;
        }

        await _db.SaveChangesAsync();
        _log.LogInformation("[PermitImport] Commit: {Updated} MA aktualisiert, {Skipped} übersprungen, {HAdd} History neu, {HUpd} History aktualisiert",
                            updated, skipped, historyAdded, historyUpdated);

        return Ok(new {
            updated, skipped, historyAdded, historyUpdated
        });
    }

    // ──────────────────────────────────────────────────────────────────────
    // PARSING
    // ──────────────────────────────────────────────────────────────────────

    private async Task<List<PreviewRow>?> ParseAsync(IFormFile file)
    {
        try
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;
            using var wb = new XSSFWorkbook(stream);

            var sheet = wb.GetSheetAt(0);
            if (sheet == null) return null;

            // Header-Zeile parsen (Zeile 0)
            var header = sheet.GetRow(0);
            if (header == null) return null;
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.LastCellNum; i++)
            {
                var cell = header.GetCell(i);
                var name = cell?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(name)) headerMap[name] = i;
            }

            int colNr   = FindCol(headerMap, "Pers. Nr.", "PersNr", "Personalnummer", "Nummer");
            int colName = FindCol(headerMap, "Name", "Nachname");
            int colVor  = FindCol(headerMap, "Vorname");
            int colBew  = FindCol(headerMap, "Bewilligung");
            int colAbl  = FindCol(headerMap, "Ablauf Bewilligung", "Ablauf", "Gültig bis");
            int colKst  = FindCol(headerMap, "Kostenstelle");
            if (colNr < 0 || colBew < 0 || colAbl < 0)
            {
                _log.LogWarning("[PermitImport] Pflicht-Spalten fehlen — Headers: {Headers}", string.Join(", ", headerMap.Keys));
                return null;
            }

            var rows = new List<PreviewRow>();
            for (int r = 1; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;
                var nrCell = row.GetCell(colNr);
                if (nrCell == null) continue;

                // Personalnummer kann in Excel als Number oder String stehen
                string nr = ExcelCellToString(nrCell);
                if (string.IsNullOrWhiteSpace(nr)) continue;

                var bewText = ExcelCellToString(row.GetCell(colBew));
                if (string.IsNullOrWhiteSpace(bewText)) continue;  // leere Zeile

                var ablRaw = row.GetCell(colAbl);
                DateOnly? expiry = null;
                if (ablRaw != null)
                {
                    if (ablRaw.CellType == CellType.Numeric && DateUtil.IsCellDateFormatted(ablRaw))
                        expiry = DateOnly.FromDateTime(ablRaw.DateCellValue ?? DateTime.MinValue);
                    else
                    {
                        var s = ExcelCellToString(ablRaw);
                        if (DateTime.TryParse(s, out var d)) expiry = DateOnly.FromDateTime(d);
                    }
                }

                rows.Add(new PreviewRow
                {
                    RowNum         = r + 1,
                    EmployeeNumber = nr.Trim(),
                    CsvLastName    = colName >= 0 ? ExcelCellToString(row.GetCell(colName)) : "",
                    CsvFirstName   = colVor  >= 0 ? ExcelCellToString(row.GetCell(colVor))  : "",
                    PermitText     = bewText.Trim(),
                    PermitCode     = MapPermitText(bewText),
                    PermitExpiry   = expiry,
                    Kostenstelle   = colKst >= 0 ? ExcelCellToString(row.GetCell(colKst)) : null
                });
            }

            return rows;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[PermitImport] Parse-Fehler");
            return null;
        }
    }

    private static int FindCol(Dictionary<string, int> headers, params string[] candidates)
    {
        foreach (var c in candidates)
            if (headers.TryGetValue(c, out var i)) return i;
        return -1;
    }

    private static string ExcelCellToString(ICell? cell)
    {
        if (cell == null) return "";
        return cell.CellType switch
        {
            CellType.Numeric  => cell.NumericCellValue.ToString("0.################"),
            CellType.String   => cell.StringCellValue ?? "",
            CellType.Boolean  => cell.BooleanCellValue ? "TRUE" : "FALSE",
            CellType.Formula  => cell.ToString() ?? "",
            _                  => cell.ToString() ?? ""
        };
    }

    /// <summary>
    /// Mappt deutschen Klartext aus der Bewilligungsliste auf den 1-2-Buchstaben-
    /// Code aus permit_type. Heuristik: nach Code in Klammern suchen, sonst
    /// häufige Klartexte erkennen.
    /// </summary>
    private static string? MapPermitText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();

        // Code in Klammern z.B. "Niedergelassene (C)"
        var openIdx = s.LastIndexOf('(');
        var closeIdx = s.LastIndexOf(')');
        if (openIdx >= 0 && closeIdx > openIdx)
        {
            var inside = s.Substring(openIdx + 1, closeIdx - openIdx - 1).Trim().ToUpperInvariant();
            if (inside.Length >= 1 && inside.Length <= 3 && inside.All(char.IsLetter))
                return inside;
        }

        // Fallback: Klartext-Erkennung (case-insensitive)
        var lower = s.ToLowerInvariant();
        if (lower.Contains("niedergelassen"))    return "C";
        if (lower.Contains("jahresaufent"))      return "B";
        if (lower.Contains("kurzaufent"))        return "L";
        if (lower.Contains("schutzbedürf") || lower.Contains("schutzbedurf")) return "S";
        if (lower.Contains("grenzgänger") || lower.Contains("grenzgaenger")) return "G";
        if (lower.Contains("vorläufig") || lower.Contains("vorlaufig"))      return "F";
        if (lower.Contains("asylsuch"))          return "N";
        return null;
    }

    private int? GetCurrentUserId()
    {
        var sub = User.FindFirstValue("sub")
               ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }
}

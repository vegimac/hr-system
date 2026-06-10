using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.HSSF.UserModel;
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
        public int TotalRows     { get; set; }
        public int Matched       { get; set; }
        public int NoMatch       { get; set; }
        public int Unknown       { get; set; }
        // Walter-Vorgabe 07.06.2026: zwei neue Status für „MA hat schon eine Bewilligung".
        public int ExistingSame  { get; set; }   // identisch — wird übersprungen
        public int ExistingDiff  { get; set; }   // andere — abhängig vom Modus
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
                e.PermitTypeId, PermitCode = e.PermitType != null ? e.PermitType.Code : null
            })
            .ToListAsync();
        // Aktuelles Ablauf-Datum pro MA = ValidTo des jüngsten History-Eintrags
        // mit PermitTypeId != NULL (Walter 01.06.2026).
        var maIds = allEmps.Select(e => e.Id).ToList();
        var hist = await _db.EmployeePermitHistories
            .AsNoTracking()
            .Where(h => maIds.Contains(h.EmployeeId) && h.PermitTypeId != null)
            .ToListAsync();
        var currentExpiryByMa = hist
            .GroupBy(h => h.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.ValidFrom).ThenByDescending(x => x.Id).First().ValidTo);

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
            r.CurrentPermitExpiry = currentExpiryByMa.TryGetValue(emp.Id, out var expVT) ? expVT : null;

            // Walter-Vorgabe 07.06.2026: Bestehende Bewilligung erkennen.
            // EXISTING_SAME = MA hat schon GENAU diese Bewilligung (selber Typ +
            //   selbes Ablaufdatum) → braucht nicht importiert werden.
            // EXISTING_DIFF = MA hat eine andere Bewilligung → Walter entscheidet
            //   pro Lauf, ob ersetzen, beenden+neu oder überspringen.
            // OK             = MA hat noch gar keine Bewilligung.
            if (r.Status == "OK")
            {
                var hasExisting = !string.IsNullOrEmpty(r.CurrentPermitCode) || r.CurrentPermitExpiry != null;
                if (hasExisting)
                {
                    var sameType   = string.Equals(r.CurrentPermitCode, r.PermitCode, StringComparison.OrdinalIgnoreCase);
                    var sameExpiry = r.CurrentPermitExpiry.HasValue && r.PermitExpiry.HasValue
                                  && r.CurrentPermitExpiry.Value == r.PermitExpiry.Value;
                    if (sameType && sameExpiry)
                    {
                        r.Status = "EXISTING_SAME";
                        r.Note   = "Identische Bewilligung bereits erfasst — wird übersprungen.";
                    }
                    else
                    {
                        r.Status = "EXISTING_DIFF";
                        r.Note   = $"Bestehende Bewilligung {r.CurrentPermitCode ?? "?"}" +
                                   (r.CurrentPermitExpiry.HasValue ? $" (bis {r.CurrentPermitExpiry:dd.MM.yyyy})" : "") +
                                   " — Entscheidung pro Modus.";
                    }
                }
            }
        }

        return Ok(new PreviewResponse
        {
            Rows = rows,
            TotalRows    = rows.Count,
            Matched      = rows.Count(r => r.Status == "OK"),
            NoMatch      = rows.Count(r => r.Status == "NO_MATCH"),
            Unknown      = rows.Count(r => r.Status == "UNKNOWN_PERMIT" || r.Status == "NO_DATE"),
            ExistingSame = rows.Count(r => r.Status == "EXISTING_SAME"),
            ExistingDiff = rows.Count(r => r.Status == "EXISTING_DIFF")
        });
    }

    [HttpPost("commit")]
    public async Task<IActionResult> Commit([FromForm] IFormFile file, [FromForm] string? validFrom = null, [FromForm] string? existingMode = null)
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

        // Walter-Vorgabe 07.06.2026: 3-Modi-Logik für MA mit bestehender Bewilligung:
        //   STRICT  (Default) → MA mit bestehender Bewilligung wird übersprungen,
        //                       nur Neu-Erfassungen laufen durch.
        //   REPLACE → bestehende History des MA wird KOMPLETT gelöscht, dann
        //             genau ein neuer Eintrag (validFromDate → PermitExpiry).
        //   APPEND  → bestehende History bleibt erhalten, alle überlappenden
        //             Vorgänger werden auf validFromDate-1 geschlossen, neuer
        //             Eintrag dahinter.
        var mode = (existingMode ?? "STRICT").ToUpperInvariant();
        if (mode != "STRICT" && mode != "REPLACE" && mode != "APPEND")
        {
            return BadRequest(new { error = $"Unbekannter existingMode '{existingMode}'. Erlaubt: STRICT, REPLACE, APPEND." });
        }

        var rows = await ParseAsync(file);
        if (rows == null) return BadRequest(new { error = "Konnte XLSX nicht parsen." });

        var permitCodeToId = await _db.PermitTypes.ToDictionaryAsync(p => p.Code, p => p.Id);
        var userId = GetCurrentUserId();

        int updated = 0, skipped = 0, historyAdded = 0, historyUpdated = 0;
        int skippedExisting = 0, replacedExisting = 0, appendedExisting = 0;
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.PermitCode) || r.PermitExpiry == null)
            { skipped++; continue; }
            if (!permitCodeToId.TryGetValue(r.PermitCode, out var permitTypeId))
            { skipped++; continue; }

            var emp = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeNumber == r.EmployeeNumber);
            if (emp == null) { skipped++; continue; }

            var allEntries = await _db.EmployeePermitHistories
                .Where(h => h.EmployeeId == emp.Id)
                .ToListAsync();
            var hasExisting = allEntries.Any(h => h.PermitTypeId != null);

            if (hasExisting)
            {
                // EXISTING_SAME (identisch) — nie anfassen, egal welcher Modus.
                // Wir vergleichen den jüngsten Eintrag.
                var maxDate = new DateOnly(9999, 12, 31);
                var newest = allEntries
                    .OrderByDescending(h => h.ValidTo ?? maxDate)
                    .ThenBy(h => h.ValidFrom)
                    .ThenBy(h => h.Id)
                    .First();
                if (newest.PermitTypeId == permitTypeId && newest.ValidTo == r.PermitExpiry)
                {
                    skipped++; skippedExisting++;
                    continue;
                }

                if (mode == "STRICT")
                {
                    skipped++; skippedExisting++;
                    continue;
                }
                if (mode == "REPLACE")
                {
                    // Komplett-Reset der History dieses MA.
                    _db.EmployeePermitHistories.RemoveRange(allEntries);
                    _db.EmployeePermitHistories.Add(new EmployeePermitHistory
                    {
                        EmployeeId       = emp.Id,
                        PermitTypeId     = permitTypeId,
                        ValidFrom        = validFromDate,
                        ValidTo          = r.PermitExpiry,
                        Note             = "Importiert via Bewilligungsliste-Import (REPLACE)",
                        CreatedAt        = DateTime.UtcNow,
                        CreatedByUserId  = userId
                    });
                    emp.PermitTypeId = permitTypeId;
                    updated++; replacedExisting++; historyAdded++;
                    continue;
                }
                // APPEND: alle Vorgänger schliessen, neuen Eintrag dahinter.
                foreach (var p in allEntries.Where(h => h.ValidFrom < validFromDate
                                                       && (h.ValidTo == null || h.ValidTo >= validFromDate)))
                {
                    p.ValidTo = validFromDate.AddDays(-1);
                }
                _db.EmployeePermitHistories.Add(new EmployeePermitHistory
                {
                    EmployeeId       = emp.Id,
                    PermitTypeId     = permitTypeId,
                    ValidFrom        = validFromDate,
                    ValidTo          = r.PermitExpiry,
                    Note             = "Importiert via Bewilligungsliste-Import (APPEND)",
                    CreatedAt        = DateTime.UtcNow,
                    CreatedByUserId  = userId
                });
                emp.PermitTypeId = permitTypeId;
                updated++; appendedExisting++; historyAdded++;
                continue;
            }

            // Keine bestehende Bewilligung → einfach anlegen.
            emp.PermitTypeId = permitTypeId;
            _db.EmployeePermitHistories.Add(new EmployeePermitHistory
            {
                EmployeeId       = emp.Id,
                PermitTypeId     = permitTypeId,
                ValidFrom        = validFromDate,
                ValidTo          = r.PermitExpiry,
                Note             = "Importiert via Bewilligungsliste-Import",
                CreatedAt        = DateTime.UtcNow,
                CreatedByUserId  = userId
            });
            historyAdded++;
            updated++;
        }

        await _db.SaveChangesAsync();
        _log.LogInformation("[PermitImport] Commit ({Mode}): {Updated} MA, {Skipped} übersprungen ({SkExist} davon mit bestehender Bewilligung), {Repl} ersetzt, {App} verlängert, {HAdd} History neu",
                            mode, updated, skipped, skippedExisting, replacedExisting, appendedExisting, historyAdded);

        return Ok(new {
            updated, skipped, historyAdded, historyUpdated,
            skippedExisting, replacedExisting, appendedExisting, mode
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
            // Walter-Vorgabe 07.06.2026: beide Excel-Formate akzeptieren.
            // .xls (HSSF) und .xlsx (XSSF) anhand der Endung wählen.
            var ext = (Path.GetExtension(file.FileName) ?? "").ToLowerInvariant();
            IWorkbook wb = ext == ".xls"
                ? new HSSFWorkbook(stream)
                : new XSSFWorkbook(stream);

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

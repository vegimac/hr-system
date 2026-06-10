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
/// Mirus-QST-Auswertung importieren (.xls).
///
/// Walter-Vorgabe 07.06.2026: Im Mirus über „QST Auswertung → Vorschau →
/// Speichern als Excel xls (einzelnes Tabellenblatt)" generiert. Pro Kanton
/// ein File, pro MA × Monat eine Zeile.
///
/// Zwei bekannte Layouts (Format-Auto-Detection im Parser):
///   • LU-Layout (76 Spalten, Name in EINER Spalte „Nachname Vorname"):
///       AHV=C3  Name=C10  Geb=C17  Gemeinde=C23  Kanton=C27
///       Monat=C36  Brutto=C43  Aperiodisch=C49  Satzbest=C57
///       Tarif=C63  Kinder=C66  Kirche=C70  QST=C74
///   • AG-Layout (149 Spalten, Vor- und Nachname getrennt):
///       Monat=C3  AHV=C8  Nachname=C16  Vorname=C28  Wohnort=C38  Kanton=C46
///       Tarif=C82  Kinder=C90  Kirche=C100
///       Brutto=C110  Aperiodisch=C119  Satzbest=C131  QST=C143
/// Erkennung: enthält C8 eine AHV-Nummer (756.xxxx.xxxx.xx) → AG, sonst LU.
///
/// Logik:
///   1. MA-Match per AHV-Nr (primär), Fallback Vor-/Nachname mit Geburtsdatum.
///   2. Pro MA werden alle Zeilen nach Tarif-Code/Kinder/Kirche gruppiert
///      → eine Allowance-Phase pro Konstellation.
///      Beispiel Ananthakumar Tibosika: M1-M3 B0Y, M4-M5 C0Y → zwei Einträge:
///        B0Y ab 01.01.JJJJ bis 31.03.JJJJ
///        C0Y ab 01.04.JJJJ offen
///   3. 3-Modi für bestehende QST-Einträge des MA:
///        STRICT  → überspringen
///        APPEND  → bestehenden offenen Eintrag auf neue ValidFrom-1 schliessen
///        REPLACE → komplette QST-History des MA löschen, dann neue anlegen
///   4. Identische Einträge (gleicher Code + ValidFrom + ValidTo) werden
///      automatisch übersprungen — kein Datenmüll.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/imports/qst")]
public class QstImportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<QstImportController> _log;

    public QstImportController(AppDbContext db, ILogger<QstImportController> log)
    {
        _db = db;
        _log = log;
    }

    // ════════════════════════════════════════════════════════════════════════
    // DTOs
    // ════════════════════════════════════════════════════════════════════════
    public class RawRow
    {
        public string AhvNumber       { get; set; } = "";
        public string FirstName       { get; set; } = "";
        public string LastName        { get; set; } = "";
        public DateOnly? DateOfBirth  { get; set; }
        public string Wohnort         { get; set; } = "";
        public string Kanton          { get; set; } = "";
        public int    Monat           { get; set; }
        public decimal Brutto         { get; set; }
        public decimal Aperiodisch    { get; set; }
        public decimal Satzbest       { get; set; }
        public string TarifCode       { get; set; } = "";
        public int    AnzahlKinder    { get; set; }
        public bool   Kirchensteuer   { get; set; }
        public decimal QstBetrag      { get; set; }
    }

    public class PlannedPhaseDto
    {
        public string TarifCode      { get; set; } = "";
        public int    AnzahlKinder   { get; set; }
        public bool   Kirchensteuer  { get; set; }
        public string QstCode        { get; set; } = "";
        public DateOnly ValidFrom    { get; set; }
        public DateOnly? ValidTo     { get; set; }
        public int    MonateImBlock  { get; set; }
    }

    public class PreviewRow
    {
        public string AhvNumber       { get; set; } = "";
        public string XlsFirstName    { get; set; } = "";
        public string XlsLastName     { get; set; } = "";
        public DateOnly? XlsDateOfBirth { get; set; }
        public string Wohnort         { get; set; } = "";
        public string Kanton          { get; set; } = "";
        public List<int> Monate       { get; set; } = new();

        // Match-Resultat
        public int?   EmployeeId       { get; set; }
        public string? DbFirstName     { get; set; }
        public string? DbLastName      { get; set; }
        public string? DbEmployeeNumber{ get; set; }
        public List<MatchCandidate> Candidates { get; set; } = new();

        // Geplante Tarif-Phasen
        public List<PlannedPhaseDto> Phasen { get; set; } = new();

        // Existing QST-Eintrag(e) am Stichtag (für Status)
        public string? ExistingQstCode   { get; set; }
        public DateOnly? ExistingValidFrom { get; set; }

        public string Status { get; set; } = "OK";
        // OK             = neu, kein bestehender Eintrag
        // EXISTING_SAME  = identisch wie aktueller Eintrag
        // EXISTING_DIFF  = bestehender Eintrag anders → Modus entscheidet
        // NO_MATCH       = kein MA gefunden
        // AMBIGUOUS      = mehrere MA-Treffer
        // NO_DATA        = keine Tarif-Phase ableitbar
        public string? Note { get; set; }
    }

    public class MatchCandidate
    {
        public int     EmployeeId      { get; set; }
        public string  FirstName       { get; set; } = "";
        public string  LastName        { get; set; } = "";
        public string  EmployeeNumber  { get; set; } = "";
        public DateOnly? DateOfBirth   { get; set; }
        public bool    IsActive        { get; set; }
    }

    public class PreviewResponse
    {
        public List<PreviewRow> Rows { get; set; } = new();
        public int TotalRows     { get; set; }
        public int Matched       { get; set; }
        public int NoMatch       { get; set; }
        public int Ambiguous     { get; set; }
        public int ExistingSame  { get; set; }
        public int ExistingDiff  { get; set; }
        public string FormatErkannt { get; set; } = "";  // "LU" | "AG"
        public int Year          { get; set; }
    }

    // ════════════════════════════════════════════════════════════════════════
    // PREVIEW
    // ════════════════════════════════════════════════════════════════════════
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromForm] IFormFile file,
                                             [FromForm] int year,
                                             [FromForm] int companyProfileId = 0)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });
        if (year < 2000 || year > 2100)
            return BadRequest(new { error = "Bitte gültiges Jahr angeben." });

        var (raw, format) = ParseXls(file);
        if (raw == null) return BadRequest(new { error = "Konnte QST-XLS nicht parsen. Erwartet wird das Mirus-Format aus 'QST Auswertung → Vorschau → Speichern als Excel xls (einzelnes Tabellenblatt)'." });

        // MA-Pool — auf gewählte Filiale beschränken (wenn gesetzt).
        var allEmps = await _db.Employees
            .AsNoTracking()
            .Where(e => companyProfileId == 0
                     || e.Employments.Any(em => em.CompanyProfileId == companyProfileId))
            .Select(e => new {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
                e.DateOfBirth, e.SocialSecurityNumber, e.IsActive
            })
            .ToListAsync();

        // QST-Einträge: jüngster pro MA → Vergleich für EXISTING_SAME / DIFF
        var maxDate = new DateOnly(9999, 12, 31);
        var qstByEmp = (await _db.EmployeeQuellensteuer
            .AsNoTracking()
            .ToListAsync())
            .GroupBy(q => q.EmployeeId)
            .ToDictionary(g => g.Key, g => g
                .OrderByDescending(q => q.ValidTo ?? maxDate)
                .ThenByDescending(q => q.ValidFrom)
                .ThenByDescending(q => q.Id)
                .First());

        // Gruppieren der RAW-Zeilen nach AHV (= ein „Block" pro MA)
        var groups = raw.GroupBy(r => r.AhvNumber).ToList();

        var result = new List<PreviewRow>();
        foreach (var g in groups)
        {
            var first = g.First();
            var row = new PreviewRow
            {
                AhvNumber      = first.AhvNumber,
                XlsFirstName   = first.FirstName,
                XlsLastName    = first.LastName,
                XlsDateOfBirth = first.DateOfBirth,
                Wohnort        = first.Wohnort,
                Kanton         = first.Kanton,
                Monate         = g.OrderBy(x => x.Monat).Select(x => x.Monat).ToList()
            };

            // 1) MA-Match
            var byAhv = !string.IsNullOrWhiteSpace(first.AhvNumber)
                ? allEmps.Where(e => Normalize(e.SocialSecurityNumber) == Normalize(first.AhvNumber)).ToList()
                : new();
            var cands = byAhv;
            if (cands.Count == 0)
            {
                cands = allEmps.Where(e => Normalize(e.FirstName) == Normalize(first.FirstName)
                                        && Normalize(e.LastName)  == Normalize(first.LastName))
                               .ToList();
                if (cands.Count > 1 && first.DateOfBirth.HasValue)
                {
                    var f = cands.Where(e => e.DateOfBirth.HasValue
                                          && DateOnly.FromDateTime(e.DateOfBirth.Value) == first.DateOfBirth.Value)
                                 .ToList();
                    if (f.Count >= 1) cands = f;
                }
            }

            row.Candidates = cands.Select(e => new MatchCandidate {
                EmployeeId     = e.Id,
                FirstName      = e.FirstName,
                LastName       = e.LastName,
                EmployeeNumber = e.EmployeeNumber,
                DateOfBirth    = e.DateOfBirth.HasValue ? DateOnly.FromDateTime(e.DateOfBirth.Value) : (DateOnly?)null,
                IsActive       = e.IsActive
            }).ToList();

            if (cands.Count == 0)
            {
                row.Status = "NO_MATCH";
                row.Note   = $"Kein MA mit AHV {first.AhvNumber} oder Name \"{first.FirstName} {first.LastName}\" gefunden.";
                // Picker-Pool = alle MA der Filiale.
                row.Candidates = allEmps
                    .OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
                    .Select(e => new MatchCandidate {
                        EmployeeId     = e.Id, FirstName = e.FirstName, LastName = e.LastName,
                        EmployeeNumber = e.EmployeeNumber,
                        DateOfBirth    = e.DateOfBirth.HasValue ? DateOnly.FromDateTime(e.DateOfBirth.Value) : (DateOnly?)null,
                        IsActive       = e.IsActive
                    }).ToList();
            }
            else if (cands.Count > 1)
            {
                row.Status = "AMBIGUOUS";
                row.Note   = $"{cands.Count} MA-Treffer — bitte den richtigen auswählen.";
            }
            else
            {
                var emp = cands.Single();
                row.EmployeeId       = emp.Id;
                row.DbFirstName      = emp.FirstName;
                row.DbLastName       = emp.LastName;
                row.DbEmployeeNumber = emp.EmployeeNumber;
            }

            // 2) Tarif-Phasen ableiten
            row.Phasen = PlanPhases(g.OrderBy(x => x.Monat).ToList(), year);
            if (row.Phasen.Count == 0)
            {
                row.Status = "NO_DATA";
                row.Note   = "Keine Tarif-Phase ableitbar.";
                result.Add(row);
                continue;
            }

            // 3) EXISTING-Status
            if (row.EmployeeId.HasValue && qstByEmp.TryGetValue(row.EmployeeId.Value, out var ex))
            {
                row.ExistingQstCode   = ex.QstCode;
                row.ExistingValidFrom = ex.ValidFrom;
                // identisch = aktuelle (letzte) Phase = letzter bestehender Eintrag
                var last = row.Phasen.Last();
                if (string.Equals(ex.QstCode, last.QstCode, StringComparison.OrdinalIgnoreCase)
                 && ex.ValidFrom == last.ValidFrom
                 && ex.ValidTo == last.ValidTo)
                {
                    row.Status = "EXISTING_SAME";
                    row.Note   = "Identischer QST-Eintrag schon erfasst.";
                }
                else
                {
                    row.Status = "EXISTING_DIFF";
                    row.Note   = $"Bestehend: {ex.QstCode} ab {ex.ValidFrom:dd.MM.yyyy} — Entscheidung pro Modus.";
                }
            }

            result.Add(row);
        }

        return Ok(new PreviewResponse
        {
            Rows = result,
            TotalRows    = result.Count,
            Matched      = result.Count(r => r.Status == "OK"),
            NoMatch      = result.Count(r => r.Status == "NO_MATCH"),
            Ambiguous    = result.Count(r => r.Status == "AMBIGUOUS"),
            ExistingSame = result.Count(r => r.Status == "EXISTING_SAME"),
            ExistingDiff = result.Count(r => r.Status == "EXISTING_DIFF"),
            FormatErkannt = format,
            Year          = year
        });
    }

    // ════════════════════════════════════════════════════════════════════════
    // COMMIT
    // ════════════════════════════════════════════════════════════════════════
    [HttpPost("commit")]
    public async Task<IActionResult> Commit([FromForm] IFormFile file,
                                            [FromForm] int year,
                                            [FromForm] string? existingMode = null,
                                            [FromForm] int companyProfileId = 0,
                                            [FromForm] string? manualMatches = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });
        if (year < 2000 || year > 2100)
            return BadRequest(new { error = "Bitte gültiges Jahr angeben." });

        var mode = (existingMode ?? "STRICT").ToUpperInvariant();
        if (mode != "STRICT" && mode != "REPLACE" && mode != "APPEND")
            return BadRequest(new { error = $"Unbekannter existingMode '{existingMode}'." });

        var manual = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(manualMatches))
        {
            foreach (var part in manualMatches.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split(':');
                if (pair.Length == 2 && int.TryParse(pair[1], out var ei))
                    manual[pair[0].Trim()] = ei;
            }
        }

        var (raw, format) = ParseXls(file);
        if (raw == null) return BadRequest(new { error = "Konnte QST-XLS nicht parsen." });

        var allEmps = await _db.Employees
            .Where(e => companyProfileId == 0
                     || e.Employments.Any(em => em.CompanyProfileId == companyProfileId))
            .ToListAsync();

        int added = 0, replaced = 0, appended = 0, skippedSame = 0, skipped = 0;
        var warnings = new List<string>();

        foreach (var g in raw.GroupBy(r => r.AhvNumber))
        {
            var first = g.First();

            int empId;
            if (manual.TryGetValue(first.AhvNumber, out var mid))
            {
                empId = mid;
            }
            else
            {
                var cands = allEmps.Where(e => Normalize(e.SocialSecurityNumber) == Normalize(first.AhvNumber)).ToList();
                if (cands.Count == 0)
                {
                    cands = allEmps.Where(e => Normalize(e.FirstName) == Normalize(first.FirstName)
                                            && Normalize(e.LastName)  == Normalize(first.LastName))
                                   .ToList();
                    if (cands.Count > 1 && first.DateOfBirth.HasValue)
                    {
                        var f = cands.Where(e => e.DateOfBirth.HasValue
                                              && DateOnly.FromDateTime(e.DateOfBirth.Value) == first.DateOfBirth.Value)
                                     .ToList();
                        if (f.Count >= 1) cands = f;
                    }
                }
                if (cands.Count != 1)
                {
                    skipped++;
                    warnings.Add($"AHV {first.AhvNumber}: {(cands.Count == 0 ? "NO_MATCH" : "AMBIGUOUS")} — übersprungen.");
                    continue;
                }
                empId = cands.Single().Id;
            }

            var phasen = PlanPhases(g.OrderBy(x => x.Monat).ToList(), year);
            if (phasen.Count == 0) { skipped++; continue; }

            var existing = await _db.EmployeeQuellensteuer
                .Where(q => q.EmployeeId == empId)
                .ToListAsync();

            // Identisch? → skip
            var maxD = new DateOnly(9999, 12, 31);
            var lastExisting = existing
                .OrderByDescending(q => q.ValidTo ?? maxD)
                .ThenByDescending(q => q.ValidFrom)
                .ThenByDescending(q => q.Id)
                .FirstOrDefault();
            var lastPhase = phasen.Last();
            if (lastExisting != null
             && string.Equals(lastExisting.QstCode, lastPhase.QstCode, StringComparison.OrdinalIgnoreCase)
             && lastExisting.ValidFrom == lastPhase.ValidFrom
             && lastExisting.ValidTo == lastPhase.ValidTo)
            {
                skippedSame++;
                continue;
            }

            // STRICT: bei bestehenden Einträgen skippen.
            if (existing.Count > 0 && mode == "STRICT")
            {
                skipped++;
                continue;
            }

            // REPLACE: bestehende komplett löschen.
            if (mode == "REPLACE" && existing.Count > 0)
            {
                _db.EmployeeQuellensteuer.RemoveRange(existing);
                replaced++;
            }

            // APPEND: offene Vorgänger schliessen auf erste neue ValidFrom-1.
            if (mode == "APPEND" && existing.Count > 0)
            {
                var firstNewFrom = phasen.First().ValidFrom;
                foreach (var ex in existing.Where(q => q.ValidFrom < firstNewFrom
                                                    && (q.ValidTo == null || q.ValidTo >= firstNewFrom)))
                {
                    ex.ValidTo = firstNewFrom.AddDays(-1);
                    ex.UpdatedAt = DateTime.UtcNow;
                }
                appended++;
            }

            // Neue Phasen anlegen.
            foreach (var p in phasen)
            {
                _db.EmployeeQuellensteuer.Add(new EmployeeQuellensteuer
                {
                    EmployeeId       = empId,
                    ValidFrom        = p.ValidFrom,
                    ValidTo          = p.ValidTo,
                    Steuerkanton     = first.Kanton,
                    QstGemeinde      = first.Wohnort,
                    TarifvorschlagQst= true,
                    TarifCode        = p.TarifCode,
                    AnzahlKinder     = p.AnzahlKinder,
                    Kirchensteuer    = p.Kirchensteuer,
                    QstCode          = p.QstCode,
                    CreatedAt        = DateTime.UtcNow,
                    UpdatedAt        = DateTime.UtcNow
                });
                added++;
            }
        }

        await _db.SaveChangesAsync();
        _log.LogInformation("[QstImport] ({Format}/{Mode}) added={A} replaced={R} appended={Ap} same={S} skip={Sk}",
            format, mode, added, replaced, appended, skippedSame, skipped);

        return Ok(new {
            mode, format,
            added, replaced, appended, skippedSame, skipped, warnings
        });
    }

    // ════════════════════════════════════════════════════════════════════════
    // Tarif-Phasen ableiten
    // ════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Walter-Vorgabe 07.06.2026: Mirus-Monatszeilen nach Tarif-Konstellation
    /// gruppieren — pro Konstellation (TarifCode + AnzahlKinder + Kirchensteuer)
    /// ein Phase-Eintrag. ValidFrom = 1. des ersten Monats der Phase,
    /// ValidTo = letzter Tag des letzten Monats der Phase ODER NULL wenn die
    /// Phase bis Dezember oder höchstens den letzten Monat im File reicht
    /// und das die letzte Phase ist (= aktuell laufend).
    /// </summary>
    private static List<PlannedPhaseDto> PlanPhases(List<RawRow> rows, int year)
    {
        var result = new List<PlannedPhaseDto>();
        if (rows.Count == 0) return result;

        PlannedPhaseDto? current = null;
        foreach (var r in rows)
        {
            var phaseKey = $"{r.TarifCode}|{r.AnzahlKinder}|{(r.Kirchensteuer ? "Y" : "N")}";
            if (current != null
             && string.Equals(current.TarifCode, r.TarifCode, StringComparison.OrdinalIgnoreCase)
             && current.AnzahlKinder == r.AnzahlKinder
             && current.Kirchensteuer == r.Kirchensteuer)
            {
                // Phase verlängern
                current.ValidTo = LastDayOfMonth(year, r.Monat);
                current.MonateImBlock++;
                continue;
            }
            // Phase wechseln → vorherige abschliessen, neue starten
            if (current != null)
                current.ValidTo = LastDayOfMonth(year, r.Monat - 1);
            current = new PlannedPhaseDto
            {
                TarifCode     = r.TarifCode,
                AnzahlKinder  = r.AnzahlKinder,
                Kirchensteuer = r.Kirchensteuer,
                QstCode       = BuildQstCode(r.TarifCode, r.AnzahlKinder, r.Kirchensteuer),
                ValidFrom     = new DateOnly(year, Math.Max(1, r.Monat), 1),
                ValidTo       = LastDayOfMonth(year, r.Monat),
                MonateImBlock = 1
            };
            result.Add(current);
        }

        // Letzte Phase = laufend → ValidTo = NULL (offener Eintrag).
        if (result.Count > 0)
            result[^1].ValidTo = null;

        return result;
    }

    private static DateOnly LastDayOfMonth(int year, int month)
    {
        if (month < 1) month = 1;
        if (month > 12) month = 12;
        var d = new DateOnly(year, month, 1).AddMonths(1).AddDays(-1);
        return d;
    }

    private static string BuildQstCode(string tarif, int kinder, bool kirche)
    {
        var t = (tarif ?? "").Trim().ToUpperInvariant();
        var k = kirche ? "Y" : "N";
        return $"{t}{kinder}{k}";
    }

    // ════════════════════════════════════════════════════════════════════════
    // PARSING — beide Layouts (LU + AG) mit Auto-Detection
    // ════════════════════════════════════════════════════════════════════════
    private (List<RawRow>?, string) ParseXls(IFormFile file)
    {
        try
        {
            using var stream = new MemoryStream();
            file.CopyTo(stream);
            stream.Position = 0;
            var ext = (Path.GetExtension(file.FileName) ?? "").ToLowerInvariant();
            IWorkbook wb = ext == ".xlsx" ? new XSSFWorkbook(stream) : new HSSFWorkbook(stream);
            var sheet = wb.GetSheetAt(0);
            if (sheet == null) return (null, "");

            // Format-Detection: enthält Spalte 8 in den ersten 100 Zeilen
            // eine AHV-Nummer (756.*)? → AG. Sonst LU.
            string format = "LU";
            for (int r = 0; r < Math.Min(100, sheet.LastRowNum + 1); r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;
                var v8 = GetString(row.GetCell(8));
                if (v8.StartsWith("756.") && v8.Length >= 16) { format = "AG"; break; }
            }

            return format == "AG"
                ? (ParseAg(sheet), "AG")
                : (ParseLu(sheet), "LU");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[QstImport] Parse-Fehler");
            return (null, "");
        }
    }

    private static List<RawRow> ParseLu(ISheet sheet)
    {
        var rows = new List<RawRow>();
        for (int r = 0; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            var ahv = GetString(row.GetCell(3));
            if (!ahv.StartsWith("756.")) continue;
            var fullName = GetString(row.GetCell(10)).Trim();
            // LU-Format: „Nachname Vorname" zusammen mit Leerzeichen
            var (last, first) = SplitNachnameVorname(fullName);
            rows.Add(new RawRow
            {
                AhvNumber     = ahv,
                LastName      = last,
                FirstName     = first,
                DateOfBirth   = TryParseDate(row.GetCell(17)),
                Wohnort       = GetString(row.GetCell(23)).Trim(),
                Kanton        = GetString(row.GetCell(27)).Trim(),
                Monat         = (int)(GetDouble(row.GetCell(36)) ?? 0),
                Brutto        = (decimal)(GetDouble(row.GetCell(43)) ?? 0),
                Aperiodisch   = (decimal)(GetDouble(row.GetCell(49)) ?? 0),
                Satzbest      = (decimal)(GetDouble(row.GetCell(57)) ?? 0),
                TarifCode     = GetString(row.GetCell(63)).Trim().ToUpperInvariant(),
                AnzahlKinder  = (int)(GetDouble(row.GetCell(66)) ?? 0),
                Kirchensteuer = GetString(row.GetCell(70)).Trim().Equals("Y", StringComparison.OrdinalIgnoreCase),
                QstBetrag     = (decimal)(GetDouble(row.GetCell(74)) ?? 0)
            });
        }
        return rows;
    }

    private static List<RawRow> ParseAg(ISheet sheet)
    {
        var rows = new List<RawRow>();
        for (int r = 0; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            var ahv = GetString(row.GetCell(8));
            if (!ahv.StartsWith("756.")) continue;
            rows.Add(new RawRow
            {
                Monat         = (int)(GetDouble(row.GetCell(3)) ?? 0),
                AhvNumber     = ahv,
                LastName      = GetString(row.GetCell(16)).Trim(),
                FirstName     = GetString(row.GetCell(28)).Trim(),
                Wohnort       = GetString(row.GetCell(38)).Trim(),
                Kanton        = GetString(row.GetCell(46)).Trim(),
                TarifCode     = GetString(row.GetCell(82)).Trim().ToUpperInvariant(),
                AnzahlKinder  = (int)(GetDouble(row.GetCell(90)) ?? 0),
                Kirchensteuer = GetString(row.GetCell(100)).Trim().Equals("Y", StringComparison.OrdinalIgnoreCase),
                Brutto        = (decimal)(GetDouble(row.GetCell(110)) ?? 0),
                Aperiodisch   = (decimal)(GetDouble(row.GetCell(119)) ?? 0),
                Satzbest      = (decimal)(GetDouble(row.GetCell(131)) ?? 0),
                QstBetrag     = (decimal)(GetDouble(row.GetCell(143)) ?? 0)
            });
        }
        return rows;
    }

    private static (string Last, string First) SplitNachnameVorname(string s)
    {
        var parts = (s ?? "").Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return ("", "");
        if (parts.Length == 1) return (parts[0], "");
        return (parts[0], parts[1]);
    }

    private static string GetString(ICell? cell)
    {
        if (cell == null) return "";
        return cell.CellType switch
        {
            CellType.String  => cell.StringCellValue ?? "",
            CellType.Numeric => DateUtil.IsCellDateFormatted(cell)
                ? cell.DateCellValue?.ToString("dd.MM.yyyy") ?? ""
                : cell.NumericCellValue.ToString("0.##"),
            CellType.Boolean => cell.BooleanCellValue.ToString(),
            CellType.Formula => cell.CachedFormulaResultType switch
            {
                CellType.String  => cell.StringCellValue ?? "",
                CellType.Numeric => cell.NumericCellValue.ToString("0.##"),
                _ => ""
            },
            _ => ""
        };
    }

    private static double? GetDouble(ICell? cell)
    {
        if (cell == null) return null;
        try
        {
            return cell.CellType switch
            {
                CellType.Numeric => cell.NumericCellValue,
                CellType.String  => double.TryParse(cell.StringCellValue, System.Globalization.NumberStyles.Any,
                                                    System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : (double?)null,
                _ => null
            };
        }
        catch { return null; }
    }

    private static DateOnly? TryParseDate(ICell? cell)
    {
        if (cell == null) return null;
        if (cell.CellType == CellType.Numeric && DateUtil.IsCellDateFormatted(cell))
        {
            var d = cell.DateCellValue;
            if (d.HasValue) return DateOnly.FromDateTime(d.Value);
        }
        var s = GetString(cell).Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateOnly.TryParseExact(s, new[] { "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd" },
                                   System.Globalization.CultureInfo.InvariantCulture,
                                   System.Globalization.DateTimeStyles.None, out var d2)) return d2;
        return null;
    }

    private static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return new string(s.Trim().ToLowerInvariant()
            .Where(ch => !char.IsWhiteSpace(ch) && ch != '.' && ch != '-').ToArray());
    }
}

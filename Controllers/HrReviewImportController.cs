using System.Security.Claims;
using System.Text;
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
/// Mirus HR-Review-Import (.xls).
///
/// Die Mirus-Lohn-Auswertung „HR-Review" enthält pro Filiale eine Übersicht
/// aller MA mit Personalien, Bewilligung, Eintritt/Austritt etc. Wir
/// importieren daraus 5 Felder pro MA:
///   1. Bewilligung (Aufenthaltsstatus + Ablauf) — gleiche 3-Modi-Logik
///      wie der Bewilligungsliste-Importer (STRICT / APPEND / REPLACE).
///   2. Geburtsdatum.
///   3. Nationalität (ISO-Code → Nationality-Tabelle).
///   4. Eintritt in Betrieb (= Employee.EntryDate).
///   5. Austritt aus Betrieb (= Employee.ExitDate). IsActive wird NICHT
///      mitgeführt — Walter entscheidet das manuell pro MA.
/// Alle anderen Spalten (Tätigkeit, Anstellungsart, Vertragslohn, etc.)
/// werden ignoriert.
///
/// Erwartetes XLS-Layout (Sheet 0, .xls oder .xlsx):
///   Zeile 0: leer
///   Zeile 1: Restaurant-Header (z.B. „McDonald's Restaurant Oftringen …")
///   Zeile 2: Spaltenüberschriften (Vorname | Name | Tätigkeit | Nationalität |
///            Ablauf Bewilligung | Aufenthaltsstatus | Eintritt Betrieb |
///            Austritt Betrieb | Geburtsdatum | Anstellungsart | Angestellt zu |
///            Ausbildung | Vertragslohn)
///   Zeile 3+: Datenzeilen.
///
/// MA-Match: keine Personalnummer in der XLS → Match per Vorname + Nachname
/// + Geburtsdatum (Tokens / normalized). NO_MATCH und AMBIGUOUS lassen sich
/// vom Frontend manuell pro Zeile zuordnen (manualMatches-Parameter).
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/imports/hr-review")]
public class HrReviewImportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<HrReviewImportController> _log;

    public HrReviewImportController(AppDbContext db, ILogger<HrReviewImportController> log)
    {
        _db = db;
        _log = log;
    }

    public class PreviewRow
    {
        public int RowNum { get; set; }
        public string CsvFirstName { get; set; } = "";
        public string CsvLastName  { get; set; } = "";
        public DateOnly? CsvDateOfBirth { get; set; }
        public string? CsvNationalityCode { get; set; }
        public string  PermitText   { get; set; } = "";
        public string? PermitCode   { get; set; }
        public DateOnly? PermitExpiry { get; set; }
        public DateOnly? EntryDate { get; set; }
        public DateOnly? ExitDate  { get; set; }

        // Match-Ergebnis
        public int?    EmployeeId  { get; set; }
        public string? DbFirstName { get; set; }
        public string? DbLastName  { get; set; }
        public string? DbEmployeeNumber { get; set; }
        public List<MatchCandidate> Candidates { get; set; } = new();
        public string? CurrentPermitCode   { get; set; }
        public DateOnly? CurrentPermitExpiry { get; set; }
        public string? CurrentNationalityCode { get; set; }
        public DateOnly? CurrentDateOfBirth { get; set; }
        public DateOnly? CurrentEntryDate { get; set; }
        public DateOnly? CurrentExitDate { get; set; }

        public string Status { get; set; } = "OK";
        // OK              → MA ohne bestehende Bewilligung, alles wird übernommen.
        // EXISTING_SAME   → MA hat identische Bewilligung — Permit übersprungen,
        //                   übrige Felder werden trotzdem übernommen.
        // EXISTING_DIFF   → MA hat andere Bewilligung — Permit per Modus,
        //                   übrige Felder werden trotzdem übernommen.
        // NO_MATCH        → kein MA gefunden — Walter wählt im Picker.
        // AMBIGUOUS       → mehrere MA-Treffer — Walter wählt im Picker.
        // UNKNOWN_PERMIT  → Aufenthaltsstatus-Klartext konnte nicht gemappt werden.
        // NO_PERMIT       → CH-Bürger oder leer — kein Permit-Import, sonst alles ok.
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
        public int Unknown       { get; set; }
        public int ExistingSame  { get; set; }
        public int ExistingDiff  { get; set; }
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromForm] IFormFile file, [FromForm] int companyProfileId = 0)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });

        var rows = await ParseAsync(file);
        if (rows == null) return BadRequest(new { error = "Konnte HR-Review-Datei nicht parsen. Erwartet wird das Mirus-Format mit Spaltenüberschriften in Zeile 3." });

        // Walter-Vorgabe 07.06.2026: MA-Pool auf die aktive Filiale beschränken,
        // damit der Picker nicht alle 1000+ MA quer durch alle Filialen zeigt.
        // companyProfileId = 0 → keine Beschränkung (Notausgang für admin).
        // Selbes Muster wie EmployeeStammdatenImportController.
        var allEmps = await _db.Employees
            .AsNoTracking()
            .Include(e => e.PermitType)
            .Include(e => e.NationalityRef)
            .Where(e => companyProfileId == 0
                     || e.Employments.Any(emp => emp.CompanyProfileId == companyProfileId))
            .Select(e => new {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
                e.DateOfBirth, e.EntryDate, e.ExitDate, e.IsActive,
                e.PermitTypeId,
                PermitCode = e.PermitType != null ? e.PermitType.Code : null,
                NationalityCode = e.NationalityRef != null ? e.NationalityRef.Code : null
            })
            .ToListAsync();
        var maIds = allEmps.Select(e => e.Id).ToList();
        // Aktuelles Ablauf-Datum = ValidTo der „neuesten" Bewilligung
        // (max ValidTo, bei Gleichheit min ValidFrom — konsistent mit
        // EmployeePermitHistoryController).
        var hist = await _db.EmployeePermitHistories
            .AsNoTracking()
            .Where(h => maIds.Contains(h.EmployeeId) && h.PermitTypeId != null)
            .ToListAsync();
        var max = new DateOnly(9999, 12, 31);
        var currentByMa = hist
            .GroupBy(h => h.EmployeeId)
            .ToDictionary(g => g.Key, g => g
                .OrderByDescending(x => x.ValidTo ?? max)
                .ThenBy(x => x.ValidFrom)
                .ThenBy(x => x.Id)
                .First());

        foreach (var r in rows)
        {
            // 1) MA-Match
            var firstTok = Normalize(r.CsvFirstName);
            var lastTok  = Normalize(r.CsvLastName);
            var cands = allEmps
                .Where(e => Normalize(e.FirstName) == firstTok
                         && Normalize(e.LastName)  == lastTok)
                .ToList();
            if (cands.Count == 0)
            {
                // Fallback: Name-Tokens vertauscht / Bindestrich-Varianten
                cands = allEmps
                    .Where(e => Normalize(e.FirstName) == lastTok
                             && Normalize(e.LastName)  == firstTok)
                    .ToList();
            }
            // Bei mehreren Match-Kandidaten → über Geburtsdatum filtern.
            if (cands.Count > 1 && r.CsvDateOfBirth.HasValue)
            {
                var dobFiltered = cands.Where(e => e.DateOfBirth.HasValue
                                               && DateOnly.FromDateTime(e.DateOfBirth.Value) == r.CsvDateOfBirth.Value)
                                       .ToList();
                if (dobFiltered.Count >= 1) cands = dobFiltered;
            }

            r.Candidates = cands.Select(e => new MatchCandidate
            {
                EmployeeId     = e.Id,
                FirstName      = e.FirstName,
                LastName       = e.LastName,
                EmployeeNumber = e.EmployeeNumber,
                DateOfBirth    = e.DateOfBirth.HasValue ? DateOnly.FromDateTime(e.DateOfBirth.Value) : (DateOnly?)null,
                IsActive       = e.IsActive
            }).ToList();

            if (cands.Count == 0)
            {
                r.Status = "NO_MATCH";
                r.Note   = $"Kein MA mit Vor-/Nachname \"{r.CsvFirstName} {r.CsvLastName}\" gefunden.";
                // Picker-Pool im Frontend = alle MA
                r.Candidates = allEmps
                    .OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
                    .Select(e => new MatchCandidate
                    {
                        EmployeeId     = e.Id,
                        FirstName      = e.FirstName,
                        LastName       = e.LastName,
                        EmployeeNumber = e.EmployeeNumber,
                        DateOfBirth    = e.DateOfBirth.HasValue ? DateOnly.FromDateTime(e.DateOfBirth.Value) : (DateOnly?)null,
                        IsActive       = e.IsActive
                    }).ToList();
                continue;
            }
            if (cands.Count > 1)
            {
                r.Status = "AMBIGUOUS";
                r.Note   = $"{cands.Count} MA-Treffer — bitte den richtigen auswählen.";
                continue;
            }

            var emp = cands.Single();
            r.EmployeeId          = emp.Id;
            r.DbFirstName         = emp.FirstName;
            r.DbLastName          = emp.LastName;
            r.DbEmployeeNumber    = emp.EmployeeNumber;
            r.CurrentPermitCode   = emp.PermitCode;
            r.CurrentNationalityCode = emp.NationalityCode;
            r.CurrentDateOfBirth  = emp.DateOfBirth.HasValue ? DateOnly.FromDateTime(emp.DateOfBirth.Value) : (DateOnly?)null;
            r.CurrentEntryDate    = emp.EntryDate.HasValue ? DateOnly.FromDateTime(emp.EntryDate.Value) : (DateOnly?)null;
            r.CurrentExitDate     = emp.ExitDate.HasValue ? DateOnly.FromDateTime(emp.ExitDate.Value) : (DateOnly?)null;
            r.CurrentPermitExpiry = currentByMa.TryGetValue(emp.Id, out var cur) ? cur.ValidTo : null;

            // 2) Permit-Status
            if (string.IsNullOrWhiteSpace(r.PermitCode))
            {
                if (string.IsNullOrWhiteSpace(r.PermitText))
                {
                    // CH-Bürger / leer → kein Permit-Import, übrige Felder OK.
                    r.Status = "NO_PERMIT";
                }
                else
                {
                    r.Status = "UNKNOWN_PERMIT";
                    r.Note   = $"Aufenthaltsstatus \"{r.PermitText}\" konnte nicht zugeordnet werden.";
                }
                continue;
            }
            var hasExisting = !string.IsNullOrEmpty(r.CurrentPermitCode) || r.CurrentPermitExpiry != null;
            if (hasExisting)
            {
                var sameType   = string.Equals(r.CurrentPermitCode, r.PermitCode, StringComparison.OrdinalIgnoreCase);
                var sameExpiry = r.CurrentPermitExpiry.HasValue && r.PermitExpiry.HasValue
                              && r.CurrentPermitExpiry.Value == r.PermitExpiry.Value;
                if (sameType && sameExpiry)
                {
                    r.Status = "EXISTING_SAME";
                    r.Note   = "Bewilligung identisch — Permit wird übersprungen, übrige Felder werden trotzdem übernommen.";
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

        return Ok(new PreviewResponse
        {
            Rows = rows,
            TotalRows    = rows.Count,
            Matched      = rows.Count(r => r.Status == "OK" || r.Status == "NO_PERMIT"),
            NoMatch      = rows.Count(r => r.Status == "NO_MATCH"),
            Ambiguous    = rows.Count(r => r.Status == "AMBIGUOUS"),
            Unknown      = rows.Count(r => r.Status == "UNKNOWN_PERMIT"),
            ExistingSame = rows.Count(r => r.Status == "EXISTING_SAME"),
            ExistingDiff = rows.Count(r => r.Status == "EXISTING_DIFF")
        });
    }

    public class CommitRequest
    {
        public string? ExistingMode { get; set; } // STRICT / APPEND / REPLACE
        // Map: rowNum → employeeId. Frontend setzt das für NO_MATCH/AMBIGUOUS.
        public Dictionary<int, int>? ManualMatches { get; set; }
        // Validfrom für neue Permit-History-Einträge.
        public string? ValidFrom { get; set; }
    }

    [HttpPost("commit")]
    public async Task<IActionResult> Commit([FromForm] IFormFile file,
        [FromForm] string? existingMode = null,
        [FromForm] string? validFrom = null,
        [FromForm] string? manualMatches = null,
        [FromForm] int companyProfileId = 0)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });
        if (string.IsNullOrWhiteSpace(validFrom) || !DateOnly.TryParse(validFrom, out var validFromDate))
            return BadRequest(new { error = "Beginn-Datum (validFrom) ist erforderlich (Format YYYY-MM-DD)." });

        var mode = (existingMode ?? "STRICT").ToUpperInvariant();
        if (mode != "STRICT" && mode != "REPLACE" && mode != "APPEND")
            return BadRequest(new { error = $"Unbekannter existingMode '{existingMode}'." });

        // Manuelle Zuordnungen parsen: "rowNum:empId,rowNum:empId"
        var manual = new Dictionary<int, int>();
        if (!string.IsNullOrWhiteSpace(manualMatches))
        {
            foreach (var part in manualMatches.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split(':');
                if (pair.Length == 2 && int.TryParse(pair[0], out var rn) && int.TryParse(pair[1], out var ei))
                    manual[rn] = ei;
            }
        }

        var rows = await ParseAsync(file);
        if (rows == null) return BadRequest(new { error = "Konnte HR-Review-Datei nicht parsen." });

        var permitCodeToId = await _db.PermitTypes.ToDictionaryAsync(p => p.Code, p => p.Id);
        // Walter-Vorgabe 07.06.2026: Lookup über Code UND Code2, damit
        // abweichende Importer-Codes (z.B. Mirus XZ für Kosovo) ohne Code-
        // Anpassung gemappt werden. Bei Konflikt gewinnt Code (kanonisch).
        var natList = await _db.Nationalities.ToListAsync();
        var nationalityCodeToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in natList)
        {
            if (!string.IsNullOrWhiteSpace(n.Code2))
                nationalityCodeToId[n.Code2.Trim().ToUpperInvariant()] = n.Id;
            // Code zuletzt → überschreibt Code2 bei Konflikt.
            if (!string.IsNullOrWhiteSpace(n.Code))
                nationalityCodeToId[n.Code.Trim().ToUpperInvariant()] = n.Id;
        }
        var userId = GetCurrentUserId();

        // Walter-Vorgabe 07.06.2026: Auto-Match-Pool ebenfalls filial-gefiltert,
        // konsistent mit Preview. Manuelle MA-IDs (aus dem Picker) gewinnen ohnehin.
        var allEmps = await _db.Employees
            .Where(e => companyProfileId == 0
                     || e.Employments.Any(emp => emp.CompanyProfileId == companyProfileId))
            .ToListAsync();

        int birthUpdated = 0, nationalityUpdated = 0, entryUpdated = 0, exitUpdated = 0;
        int permitAdded = 0, permitReplaced = 0, permitAppended = 0, permitSkippedExisting = 0;
        int skipped = 0;
        var warnings = new List<string>();

        foreach (var r in rows)
        {
            // 1) MA bestimmen — manuelle Auswahl gewinnt
            int empId;
            if (manual.TryGetValue(r.RowNum, out var manId))
            {
                empId = manId;
            }
            else
            {
                var firstTok = Normalize(r.CsvFirstName);
                var lastTok  = Normalize(r.CsvLastName);
                var cands = allEmps.Where(e => Normalize(e.FirstName) == firstTok && Normalize(e.LastName) == lastTok).ToList();
                if (cands.Count == 0)
                    cands = allEmps.Where(e => Normalize(e.FirstName) == lastTok && Normalize(e.LastName) == firstTok).ToList();
                if (cands.Count > 1 && r.CsvDateOfBirth.HasValue)
                {
                    var dobF = cands.Where(e => e.DateOfBirth.HasValue && DateOnly.FromDateTime(e.DateOfBirth.Value) == r.CsvDateOfBirth.Value).ToList();
                    if (dobF.Count >= 1) cands = dobF;
                }
                if (cands.Count != 1)
                {
                    skipped++;
                    warnings.Add($"Zeile {r.RowNum}: {r.CsvFirstName} {r.CsvLastName} — kein eindeutiger MA-Match (Stat. {(cands.Count == 0 ? "NO_MATCH" : "AMBIGUOUS")}).");
                    continue;
                }
                empId = cands.Single().Id;
            }

            var emp = allEmps.FirstOrDefault(e => e.Id == empId);
            if (emp == null) { skipped++; continue; }

            // 2) Geburtsdatum übernehmen (überschreibend)
            if (r.CsvDateOfBirth.HasValue)
            {
                var dobNew = r.CsvDateOfBirth.Value.ToDateTime(TimeOnly.MinValue);
                if (emp.DateOfBirth != dobNew)
                {
                    emp.DateOfBirth = dobNew;
                    birthUpdated++;
                }
            }
            // 3) Nationalität übernehmen — Lookup gegen Code UND Code2.
            if (!string.IsNullOrWhiteSpace(r.CsvNationalityCode))
            {
                if (nationalityCodeToId.TryGetValue(r.CsvNationalityCode.Trim().ToUpperInvariant(), out var natId))
                {
                    if (emp.NationalityId != natId)
                    {
                        emp.NationalityId = natId;
                        nationalityUpdated++;
                    }
                }
                else
                {
                    warnings.Add($"Zeile {r.RowNum}: Nationalitäts-Code \"{r.CsvNationalityCode}\" nicht in Nationality-Tabelle.");
                }
            }
            // 4) Eintritt übernehmen
            if (r.EntryDate.HasValue)
            {
                var entryNew = r.EntryDate.Value.ToDateTime(TimeOnly.MinValue);
                if (emp.EntryDate != entryNew)
                {
                    emp.EntryDate = entryNew;
                    entryUpdated++;
                }
            }
            // 5) Austritt übernehmen — NICHT IsActive ändern! (Walter pflegt manuell)
            if (r.ExitDate.HasValue)
            {
                var exitNew = r.ExitDate.Value.ToDateTime(TimeOnly.MinValue);
                if (emp.ExitDate != exitNew)
                {
                    emp.ExitDate = exitNew;
                    exitUpdated++;
                }
            }

            // 6) Bewilligung — gleiche 3-Modi-Logik wie PermitImport
            if (string.IsNullOrWhiteSpace(r.PermitCode) || r.PermitExpiry == null)
            {
                // CH-Bürger / leer / unbekannt → kein Permit-Import.
                continue;
            }
            if (!permitCodeToId.TryGetValue(r.PermitCode, out var permitTypeId))
                continue;

            var allEntries = await _db.EmployeePermitHistories
                .Where(h => h.EmployeeId == emp.Id)
                .ToListAsync();
            var hasExisting = allEntries.Any(h => h.PermitTypeId != null);

            if (hasExisting)
            {
                var maxD = new DateOnly(9999, 12, 31);
                var newest = allEntries
                    .OrderByDescending(h => h.ValidTo ?? maxD)
                    .ThenBy(h => h.ValidFrom)
                    .ThenBy(h => h.Id)
                    .First();
                if (newest.PermitTypeId == permitTypeId && newest.ValidTo == r.PermitExpiry)
                {
                    permitSkippedExisting++;
                    continue;
                }
                if (mode == "STRICT") { permitSkippedExisting++; continue; }
                if (mode == "REPLACE")
                {
                    _db.EmployeePermitHistories.RemoveRange(allEntries);
                    _db.EmployeePermitHistories.Add(new EmployeePermitHistory
                    {
                        EmployeeId      = emp.Id,
                        PermitTypeId    = permitTypeId,
                        ValidFrom       = validFromDate,
                        ValidTo         = r.PermitExpiry,
                        Note            = "Importiert via HR-Review-Import (REPLACE)",
                        CreatedAt       = DateTime.UtcNow,
                        CreatedByUserId = userId
                    });
                    emp.PermitTypeId = permitTypeId;
                    permitReplaced++; permitAdded++;
                    continue;
                }
                // APPEND
                foreach (var p in allEntries.Where(h => h.ValidFrom < validFromDate
                                                     && (h.ValidTo == null || h.ValidTo >= validFromDate)))
                {
                    p.ValidTo = validFromDate.AddDays(-1);
                }
                _db.EmployeePermitHistories.Add(new EmployeePermitHistory
                {
                    EmployeeId      = emp.Id,
                    PermitTypeId    = permitTypeId,
                    ValidFrom       = validFromDate,
                    ValidTo         = r.PermitExpiry,
                    Note            = "Importiert via HR-Review-Import (APPEND)",
                    CreatedAt       = DateTime.UtcNow,
                    CreatedByUserId = userId
                });
                emp.PermitTypeId = permitTypeId;
                permitAppended++; permitAdded++;
                continue;
            }

            // Keine bestehende Bewilligung → einfach anlegen.
            emp.PermitTypeId = permitTypeId;
            _db.EmployeePermitHistories.Add(new EmployeePermitHistory
            {
                EmployeeId      = emp.Id,
                PermitTypeId    = permitTypeId,
                ValidFrom       = validFromDate,
                ValidTo         = r.PermitExpiry,
                Note            = "Importiert via HR-Review-Import",
                CreatedAt       = DateTime.UtcNow,
                CreatedByUserId = userId
            });
            permitAdded++;
        }

        await _db.SaveChangesAsync();
        _log.LogInformation("[HrReviewImport] ({Mode}) DOB={DOB} Nat={Nat} Entry={E} Exit={X} PermitNew={Pn} Repl={Pr} App={Pa} Skip={Ps} RowsSkip={Sk}",
            mode, birthUpdated, nationalityUpdated, entryUpdated, exitUpdated, permitAdded, permitReplaced, permitAppended, permitSkippedExisting, skipped);

        return Ok(new {
            mode,
            birthUpdated, nationalityUpdated, entryUpdated, exitUpdated,
            permitAdded, permitReplaced, permitAppended, permitSkippedExisting,
            skipped, warnings
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

            IWorkbook wb;
            // .xls oder .xlsx anhand Dateinamen entscheiden — HSSFWorkbook
            // braucht für .xlsx einen falschen Header-Check.
            var ext = (Path.GetExtension(file.FileName) ?? "").ToLowerInvariant();
            if (ext == ".xlsx") wb = new XSSFWorkbook(stream);
            else                wb = new HSSFWorkbook(stream);

            var sheet = wb.GetSheetAt(0);
            if (sheet == null) return null;

            // Header-Zeile suchen — sie enthält „Vorname" in Spalte 0.
            int headerRow = -1;
            for (int r = 0; r <= Math.Min(10, sheet.LastRowNum); r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;
                var v0 = GetString(row.GetCell(0)).Trim();
                if (string.Equals(v0, "Vorname", StringComparison.OrdinalIgnoreCase))
                { headerRow = r; break; }
            }
            if (headerRow < 0) return null;

            // Spalten-Index per Header-Klartext (Mirus mischt manchmal die
            // Reihenfolge bei verschiedenen Filialen).
            var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var hRow = sheet.GetRow(headerRow);
            for (int c = 0; c < hRow.LastCellNum; c++)
            {
                var v = GetString(hRow.GetCell(c)).Trim();
                if (!string.IsNullOrWhiteSpace(v)) idx[v] = c;
            }

            int colVorname    = idx.GetValueOrDefault("Vorname", 0);
            int colName       = idx.GetValueOrDefault("Name", 1);
            int colNat        = idx.GetValueOrDefault("Nationalität", 3);
            int colAblauf     = idx.GetValueOrDefault("Ablauf Bewilligung", 4);
            int colStatus     = idx.GetValueOrDefault("Aufenthaltsstatus", 5);
            int colEntry      = idx.GetValueOrDefault("Eintritt Betrieb", 6);
            int colExit       = idx.GetValueOrDefault("Austritt Betrieb", 7);
            int colDob        = idx.GetValueOrDefault("Geburtsdatum", 8);

            var rows = new List<PreviewRow>();
            for (int r = headerRow + 1; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;

                var firstName = GetString(row.GetCell(colVorname)).Trim();
                var lastName  = GetString(row.GetCell(colName)).Trim();
                if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName))
                    continue; // leere Zeile

                var pr = new PreviewRow
                {
                    RowNum             = r + 1, // 1-basiert für UI-Anzeige
                    CsvFirstName       = firstName,
                    CsvLastName        = lastName,
                    CsvNationalityCode = GetString(row.GetCell(colNat)).Trim(),
                    CsvDateOfBirth     = TryParseDate(row.GetCell(colDob)),
                    PermitText         = GetString(row.GetCell(colStatus)).Trim(),
                    PermitExpiry       = TryParseDate(row.GetCell(colAblauf)),
                    EntryDate          = TryParseDate(row.GetCell(colEntry)),
                    ExitDate           = TryParseDate(row.GetCell(colExit))
                };
                pr.PermitCode = MapPermitText(pr.PermitText);
                rows.Add(pr);
            }
            return rows;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[HrReviewImport] Parse-Fehler");
            return null;
        }
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
                                   System.Globalization.DateTimeStyles.None, out var d2))
            return d2;
        if (DateOnly.TryParse(s, System.Globalization.CultureInfo.GetCultureInfo("de-CH"),
                              System.Globalization.DateTimeStyles.None, out var d3))
            return d3;
        return null;
    }

    /// <summary>Mirus-Klartext „Jahresaufenthalter (B)" → „B".</summary>
    private static string? MapPermitText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.ToLowerInvariant();
        if (t.Contains("niedergelassen"))   return "C";
        if (t.Contains("jahresaufenthalt"))  return "B";
        if (t.Contains("kurzaufenthalt"))    return "L";
        if (t.Contains("schutzbedürftig") || t.Contains("schutzbeduerftig")) return "S";
        if (t.Contains("vorläufig") || t.Contains("vorlaeufig")) return "F";
        if (t.Contains("asylsuch"))          return "N";
        if (t.Contains("grenzgänger") || t.Contains("grenzgaenger")) return "G";
        return null;
    }

    /// <summary>Name normalisieren: trim, lowercase, Whitespace zu single space,
    /// Umlaute belassen — wir wollen exakte Mirus-Schreibweise matchen.</summary>
    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder();
        bool prevWs = false;
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(ch)) { if (!prevWs) sb.Append(' '); prevWs = true; }
            else { sb.Append(ch); prevWs = false; }
        }
        return sb.ToString();
    }

    private int? GetCurrentUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }
}

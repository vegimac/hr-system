using System.Globalization;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace HrSystem.Controllers;

/// <summary>
/// Dienstplan-Absenzen-Import.
///
/// Liest einen Quartals-/Monats-Dienstplan (XLS/XLSX). Aufbau:
///   - Zeile 0 = Kopfzeile, je Tag eine Datums-Spalte (Format TT.MM.JJJJ).
///     Leere Zwischenspalten werden ignoriert.
///   - Je MA eine Zeile, Spalte 0 = Name ("Nachname Vorname").
///   - Zellen enthalten Absenz-Codes: FE = Ferien, KR = Krankheit, UN = Unfall.
///     Ein führender Stern (z.B. *KR) ist eine Plan-Markierung und wird
///     ignoriert (Code = KR).
///
/// Konsekutive Tage mit demselben Code werden zu EINER Absenz zusammengefasst.
/// MA-Zuordnung über token-basierten Namensvergleich (wie der Stammdaten-
/// Importer); bei NO_MATCH / AMBIGUOUS bietet das Frontend einen manuellen
/// MA-Picker.
///
/// hoursCredited + workedDays werden serverseitig nach derselben Logik wie
/// calcAbsHoursPreview() / renderAbsDayCheckboxes() in employees.js berechnet.
///
/// Endpoints:
///   POST /api/imports/roster-absences/preview → parsen + matchen, kein Schreiben
///   POST /api/imports/roster-absences/commit  → ausgewählte Spans als Absence anlegen
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/imports/roster-absences")]
public class RosterAbsenceImportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<RosterAbsenceImportController> _log;
    private readonly LohnEditLockService _editLock;

    public RosterAbsenceImportController(AppDbContext db, ILogger<RosterAbsenceImportController> log, LohnEditLockService editLock)
    {
        _db  = db;
        _log = log;
        _editLock = editLock;
    }

    // Dienstplan-Code → Absence.AbsenceType. Erweiterbar wenn weitere Codes
    // auftauchen (z.B. SC für Schulung).
    private static readonly Dictionary<string, string> CodeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["FE"] = "FERIEN",
            ["FI"] = "FEIERTAG",     // Walter-Vorgabe 15.05.2026
            ["FK"] = "FREI_KOMP",    // Frei-Kompensation (Plus-Stunden-Verbrauch, Walter-Vorgabe 15.05.2026)
            ["ZF"] = "BEZ_ABSENZ",   // Bezahlte Absenz, 1/5 wie Krankheit (Walter-Vorgabe 15.05.2026)
            ["KR"] = "KRANK",
            ["UN"] = "UNFALL",
            ["MV"] = "MUTT_VATER",   // Mutter-/Vaterschaftsurlaub (Walter-Vorgabe 15.05.2026)
            ["UU"] = "UNBEZ_URLAUB", // Unbezahlter Urlaub (Walter-Vorgabe 27.06.2026)
        };

    // ── DTOs ────────────────────────────────────────────────────────────────

    public class PreviewRow
    {
        public int    RowNum      { get; set; }   // synthetische, stabile ID je Span
        public string RawName     { get; set; } = "";
        public string Code        { get; set; } = "";   // FE/KR/UN (normalisiert, ohne *)
        public string AbsenceType { get; set; } = "";   // FERIEN/KRANK/UNFALL
        public string DateFrom    { get; set; } = "";   // yyyy-MM-dd
        public string DateTo      { get; set; } = "";
        public int    DayCount    { get; set; }
        public List<string> Days        { get; set; } = new();   // alle markierten Kalendertage
        public List<string> WorkedDays  { get; set; } = new();   // angerechnete Tage (Sa/So-Heuristik)
        public decimal HoursCredited { get; set; }
        public bool    HadStar    { get; set; }

        public int?    EmployeeId      { get; set; }
        public string? EmployeeNumber  { get; set; }
        public string? DbFirstName     { get; set; }
        public string? DbLastName      { get; set; }
        public string? EmploymentModel { get; set; }

        // OK | NO_MATCH | AMBIGUOUS | UNKNOWN_CODE | DUPLICATE
        public string  Status { get; set; } = "OK";
        public string? Note   { get; set; }
    }

    // Kompakte MA-Liste der Filiale für den manuellen Picker (NO_MATCH/AMBIGUOUS).
    public class BranchEmployeeDto
    {
        public int      Id             { get; set; }
        public string?  EmployeeNumber { get; set; }
        public string?  FirstName      { get; set; }
        public string?  LastName       { get; set; }
        public bool     IsActive       { get; set; } = true;
    }

    public class PreviewResponse
    {
        public List<PreviewRow> Rows  { get; set; } = new();
        public int  TotalRows  { get; set; }
        public int  Matched    { get; set; }
        public int  NoMatch    { get; set; }
        public int  Importable { get; set; }
        public string? PeriodFrom { get; set; }
        public string? PeriodTo   { get; set; }
        public List<BranchEmployeeDto> BranchEmployees { get; set; } = new();
    }

    // ── Preview ─────────────────────────────────────────────────────────────

    [HttpPost("preview")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Preview([FromForm] IFormFile file,
                                             [FromForm] int companyProfileId)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });

        var (spans, periodFrom, periodTo, parseError) = ParseRoster(file);
        if (parseError != null) return BadRequest(new { error = parseError });
        if (spans.Count == 0)
            return BadRequest(new { error = "Keine Absenzen im Dienstplan gefunden (Codes FE/KR/UN)." });

        var employees   = await LoadEmployeePoolAsync(companyProfileId);
        var absenzTypen = await _db.AbsenzTypen.ToListAsync();
        var profile     = await _db.CompanyProfiles.FirstOrDefaultAsync(p => p.Id == companyProfileId);

        foreach (var r in spans)
        {
            if (r.Status == "UNKNOWN_CODE") continue;

            var matches = employees
                .Where(e => NameTokensMatch(e.FirstName, e.LastName, "", r.RawName))
                .ToList();

            if (matches.Count == 0)
            {
                r.Status = "NO_MATCH";
                r.Note   = "Kein MA mit passendem Namen in der Filiale — bitte manuell wählen.";
                continue;
            }
            if (matches.Count > 1)
            {
                r.Status = "AMBIGUOUS";
                r.Note   = $"{matches.Count} MA mit diesem Namen — bitte manuell wählen.";
                continue;
            }

            FillMatch(r, matches[0], absenzTypen, profile);
        }

        // Duplikat-Check gegen bereits erfasste Absenzen (gleicher MA, gleicher
        // Typ, überlappender Zeitraum) — verhindert Doppelimport desselben Plans.
        await FlagDuplicatesAsync(spans);

        return Ok(new PreviewResponse
        {
            Rows       = spans,
            TotalRows  = spans.Count,
            Matched    = spans.Count(r => r.EmployeeId != null),
            NoMatch    = spans.Count(r => r.Status == "NO_MATCH" || r.Status == "AMBIGUOUS"),
            Importable = spans.Count(r => r.Status == "OK" && r.EmployeeId != null),
            PeriodFrom = periodFrom,
            PeriodTo   = periodTo,
            BranchEmployees = employees
                .OrderBy(e => e.FirstName ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.LastName  ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(e => new BranchEmployeeDto
                {
                    Id             = e.Id,
                    EmployeeNumber = e.EmployeeNumber,
                    FirstName      = e.FirstName,
                    LastName       = e.LastName,
                    IsActive       = e.IsActive
                })
                .ToList()
        });
    }

    // ── Commit ──────────────────────────────────────────────────────────────

    [HttpPost("commit")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Commit([FromForm] IFormFile file,
                                            [FromForm] int companyProfileId,
                                            [FromForm] string? rowNums,
                                            [FromForm] string? manualMatches)
    {
        // rowNums: Komma-Liste der zu importierenden Spans. Leer → alle.
        var selectedRows = string.IsNullOrWhiteSpace(rowNums)
            ? new HashSet<int>()
            : rowNums.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(int.Parse)
                     .ToHashSet();

        // manualMatches: "rowNum:employeeId,rowNum:employeeId" — manuelle
        // MA-Zuordnung aus dem Frontend gewinnt vor dem Auto-Match.
        var manualMap = new Dictionary<int, int>();
        if (!string.IsNullOrWhiteSpace(manualMatches))
        {
            foreach (var pair in manualMatches.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = pair.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (kv.Length == 2 && int.TryParse(kv[0], out var rn) && int.TryParse(kv[1], out var eid))
                    manualMap[rn] = eid;
            }
        }

        var (spans, _, _, parseError) = ParseRoster(file);
        if (parseError != null) return BadRequest(new { error = parseError });

        var employees   = await LoadEmployeePoolAsync(companyProfileId);
        var absenzTypen = await _db.AbsenzTypen.ToListAsync();
        var profile     = await _db.CompanyProfiles.FirstOrDefaultAsync(p => p.Id == companyProfileId);

        int created = 0, skipped = 0, duplicates = 0, lockedSkipped = 0;
        var lockedMsgs = new List<string>();

        foreach (var r in spans)
        {
            if (selectedRows.Count > 0 && !selectedRows.Contains(r.RowNum)) continue;
            if (r.Status == "UNKNOWN_CODE" || string.IsNullOrEmpty(r.AbsenceType)) { skipped++; continue; }

            // Stufe 0: manuelle Zuordnung gewinnt immer.
            Employee? emp = null;
            if (manualMap.TryGetValue(r.RowNum, out var manualEmpId))
                emp = employees.FirstOrDefault(e => e.Id == manualEmpId);

            // Stufe 1: Auto-Match per Namen (nur wenn eindeutig).
            if (emp == null)
            {
                var matches = employees
                    .Where(e => NameTokensMatch(e.FirstName, e.LastName, "", r.RawName))
                    .ToList();
                if (matches.Count == 1) emp = matches[0];
            }
            if (emp == null) { skipped++; continue; }

            var df = DateOnly.Parse(r.DateFrom);
            var dt = DateOnly.Parse(r.DateTo);

            // Duplikat-Check: gleiche Absenz (MA + Typ + überlappender Zeitraum)
            bool dup = await _db.Absences.AnyAsync(a =>
                   a.EmployeeId  == emp.Id
                && a.AbsenceType == r.AbsenceType
                && a.DateFrom    <= dt
                && a.DateTo      >= df);
            if (dup) { duplicates++; continue; }

            // Per-Periode-Sperre (Walter-Vorgabe 27.06.2026): KEINE Absenz in eine
            // bereits abgeschlossene / in Verarbeitung befindliche Lohnperiode
            // importieren. Offene/nie verarbeitete Perioden bleiben erlaubt.
            var lockCheck = await _editLock.CheckRangePeriodAsync(User, companyProfileId, df, dt);
            if (lockCheck.Locked)
            {
                lockedSkipped++;
                lockedMsgs.Add($"{emp.FirstName} {emp.LastName} — {r.AbsenceType} {df:dd.MM.yyyy}–{dt:dd.MM.yyyy}: Lohnperiode abgeschlossen/in Verarbeitung, nicht importiert.");
                continue;
            }

            var activeEmp = emp.Employments.FirstOrDefault(e => e.IsActive)
                         ?? emp.Employments.FirstOrDefault();
            var typCfg     = absenzTypen.FirstOrDefault(t => t.Code == r.AbsenceType);
            var workedDays = ComputeWorkedDays(r.AbsenceType, r.Days);
            var hours      = ComputeHours(r.AbsenceType, activeEmp?.EmploymentModel ?? "",
                                          typCfg, profile, activeEmp, workedDays.Count);

            _db.Absences.Add(new Absence
            {
                EmployeeId    = emp.Id,
                AbsenceType   = r.AbsenceType,
                DateFrom      = df,
                DateTo        = dt,
                WorkedDays    = System.Text.Json.JsonSerializer.Serialize(workedDays),
                HoursCredited = hours,
                Prozent       = 100m,
                Notes         = $"Import Dienstplan {DateTime.Now:dd.MM.yyyy}",
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow,
            });
            created++;
        }

        await _db.SaveChangesAsync();
        _log.LogInformation("[RosterAbsenceImport] Filiale={CP} erstellt={Created}, Dubletten={Dup}, übersprungen={Skip}, gesperrt={Locked}",
            companyProfileId, created, duplicates, skipped, lockedSkipped);

        return Ok(new { created, duplicates, skipped, lockedSkipped, lockedMessages = lockedMsgs });
    }

    // ── MA-Pool ─────────────────────────────────────────────────────────────
    // STRIKT auf die gewählte Filiale begrenzt — Walter-Vorgabe 15.05.2026:
    // Wenn beim Dienstplan-Import ein MA nicht automatisch zugeordnet werden
    // kann, soll der manuelle Picker NUR MA der gewählten Filiale zeigen
    // (aktive + bereits ausgetretene), keine Phantom-MA / Personaldossiers
    // aus anderen Filialen.
    //
    // Anders als beim Stammdaten-Importer (der MA ohne Vertrag mit aufnimmt,
    // weil GastroSocial-/BVG-Listen filialübergreifend Phantom-MA enthalten
    // können): der Dienstplan ist immer pro Filiale, also gehört nichts
    // dazwischen, das nicht zur Filiale gehört.
    private async Task<List<Employee>> LoadEmployeePoolAsync(int companyProfileId)
    {
        // Walter-Vorgabe 27.06.2026: auch VERTRAGSLOSE MA dieser Filiale
        // (Personaldossier) in den Pool nehmen, damit alte Info-Absenzen
        // importiert werden können. Da kein Vertrag die Filiale liefert, wird
        // die Filiale über das Personalnummer-Präfix bestimmt (restaurantCode
        // ohne führende Nullen, z.B. 058 → "58" → Personalnummer 580066…).
        string prefix = "";
        if (companyProfileId != 0)
        {
            var rc = await _db.CompanyProfiles
                .Where(p => p.Id == companyProfileId)
                .Select(p => p.RestaurantCode)
                .FirstOrDefaultAsync();
            prefix = (rc ?? "").TrimStart('0');
        }

        return await _db.Employees
            .Include(e => e.Employments)
            .Where(e => companyProfileId == 0
                     || e.Employments.Any(emp => emp.CompanyProfileId == companyProfileId)
                     || (!e.Employments.Any()
                         && prefix != ""
                         && e.EmployeeNumber != null
                         && e.EmployeeNumber.StartsWith(prefix)))
            .ToListAsync();
    }

    // Übernimmt einen sicheren Match in die Preview-Zeile + berechnet
    // workedDays / hoursCredited (für die Vorschau-Anzeige).
    private void FillMatch(PreviewRow r, Employee m,
                           List<AbsenzTyp> absenzTypen, CompanyProfile? profile)
    {
        var activeEmp = m.Employments.FirstOrDefault(e => e.IsActive)
                     ?? m.Employments.FirstOrDefault();
        r.EmployeeId      = m.Id;
        r.EmployeeNumber  = m.EmployeeNumber;
        r.DbFirstName     = m.FirstName;
        r.DbLastName      = m.LastName;
        r.EmploymentModel = activeEmp?.EmploymentModel;

        var typCfg = absenzTypen.FirstOrDefault(t => t.Code == r.AbsenceType);
        r.WorkedDays    = ComputeWorkedDays(r.AbsenceType, r.Days);
        r.HoursCredited = ComputeHours(r.AbsenceType, activeEmp?.EmploymentModel ?? "",
                                       typCfg, profile, activeEmp, r.WorkedDays.Count);
    }

    // Markiert Spans die sich mit einer bereits erfassten Absenz überschneiden.
    private async Task FlagDuplicatesAsync(List<PreviewRow> spans)
    {
        var matched = spans.Where(s => s.EmployeeId != null && s.Status == "OK").ToList();
        if (matched.Count == 0) return;

        var empIds = matched.Select(s => s.EmployeeId!.Value).Distinct().ToList();
        var existing = await _db.Absences
            .Where(a => empIds.Contains(a.EmployeeId))
            .Select(a => new { a.EmployeeId, a.AbsenceType, a.DateFrom, a.DateTo })
            .ToListAsync();

        foreach (var r in matched)
        {
            var df = DateOnly.Parse(r.DateFrom);
            var dt = DateOnly.Parse(r.DateTo);
            bool dup = existing.Any(a => a.EmployeeId == r.EmployeeId!.Value
                                      && a.AbsenceType == r.AbsenceType
                                      && a.DateFrom <= dt && a.DateTo >= df);
            if (dup)
            {
                r.Status = "DUPLICATE";
                r.Note   = "Für diesen MA existiert bereits eine Absenz dieses Typs in diesem Zeitraum.";
            }
        }
    }

    // ── Stunden- / Tagesberechnung ──────────────────────────────────────────
    // Spiegelt renderAbsDayCheckboxes() in employees.js: bei KRANK/UNFALL/
    // SCHULUNG werden Sa/So NICHT angerechnet, sofern die ganze Mo–So-Woche im
    // Zeitraum liegt. Bei FERIEN/FEIERTAG/UNBEZ_URLAUB zählen alle Kalendertage.
    private static List<string> ComputeWorkedDays(string absenceType, List<string> allDays)
    {
        // Unbezahlter Urlaub wird wie Ferien kalenderbasiert (1/7) gezählt
        // (Walter-Vorgabe 27.06.2026) — passt zum Festlohn-Tagessatz 12/365.
        if (absenceType == "FERIEN" || absenceType == "FEIERTAG" || absenceType == "UNBEZ_URLAUB")
            return new List<string>(allDays);

        var daySet = new HashSet<DateOnly>();
        foreach (var s in allDays)
            if (DateOnly.TryParse(s, out var d)) daySet.Add(d);

        bool FullWeekInRange(DateOnly day)
        {
            int delta   = ((int)day.DayOfWeek + 6) % 7;   // Montag = 0
            var monday  = day.AddDays(-delta);
            for (int i = 0; i < 7; i++)
                if (!daySet.Contains(monday.AddDays(i))) return false;
            return true;
        }

        var result = new List<string>();
        foreach (var s in allDays)
        {
            if (!DateOnly.TryParse(s, out var d)) continue;
            bool isSaSo = d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;
            if (isSaSo && FullWeekInRange(d)) continue;
            result.Add(s);
        }
        return result;
    }

    // Spiegelt calcAbsHoursPreview() in employees.js. Prozent ist beim
    // Dienstplan-Import immer 100 (der Plan kennt keine Teil-Ausfälle).
    private static decimal ComputeHours(string absenceType, string empModel,
                                        AbsenzTyp? typCfg, CompanyProfile? profile,
                                        Employment? emp, int workedDayCount)
    {
        int count = workedDayCount;
        if (count <= 0) return 0m;

        // MTP/UTP bei FERIEN oder FEIERTAG → keine Stunden-Gutschrift.
        if ((empModel == "MTP" || empModel == "FLEX")
            && (absenceType == "FERIEN" || absenceType == "FEIERTAG"))
            return 0m;

        bool   hatGutschrift  = typCfg?.Zeitgutschrift  ?? true;
        string modus          = typCfg?.GutschriftModus ?? "1/5";
        bool   utpAuszahlung  = typCfg?.UtpAuszahlung   ?? false;
        string basisStunden   = typCfg?.BasisStunden    ?? "BETRIEB";
        string? reduziertSaldo = typCfg?.ReduziertSaldo;

        // UTP ohne UtpAuszahlung-Flag → keine automatische Gutschrift.
        if (empModel == "FLEX" && !utpAuszahlung) return 0m;

        decimal betriebWeekly = profile?.NormalWeeklyHours ?? 42m;
        decimal weeklyH       = betriebWeekly;
        if (basisStunden == "VERTRAG")
        {
            if (empModel == "MTP")
            {
                weeklyH = emp?.GuaranteedHoursPerWeek ?? emp?.WeeklyHours ?? betriebWeekly;
            }
            else if (empModel == "FIX" || empModel == "FIX-M")
            {
                // Nur bei FERIEN/FEIERTAG pensum-adjustiert, sonst Betriebs-Woche.
                if (absenceType == "FERIEN" || absenceType == "FEIERTAG")
                {
                    decimal pct = emp?.EmploymentPercentage ?? 100m;
                    weeklyH = emp?.WeeklyHours ?? (betriebWeekly * pct / 100m);
                }
            }
        }

        decimal hours;
        if (reduziertSaldo == "NACHT_STUNDEN")
            hours = count * (weeklyH / 5m);
        else if (!hatGutschrift)
            hours = count * (weeklyH / 5m);
        else if (modus == "1/7")
            hours = count * (weeklyH / 7m);
        else
            hours = count * (weeklyH / 5m);

        return Math.Round(hours, 2);
    }

    // ── Dienstplan-Parser ───────────────────────────────────────────────────

    private (List<PreviewRow> Spans, string? From, string? To, string? Error)
        ParseRoster(IFormFile file)
    {
        try
        {
            using var stream = file.OpenReadStream();
            IWorkbook wb;
            try
            {
                wb = file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                    ? new XSSFWorkbook(stream)
                    : new HSSFWorkbook(stream);
            }
            catch
            {
                stream.Position = 0;
                wb = new XSSFWorkbook(stream);
            }

            var sheet = wb.GetSheetAt(0);
            if (sheet == null) return (new(), null, null, "Datei enthält kein Tabellenblatt.");

            var header = sheet.GetRow(0);
            if (header == null) return (new(), null, null, "Keine Kopfzeile gefunden.");

            // Spalte → Datum (Format TT.MM.JJJJ; numerische Datumszellen ebenfalls).
            var colDate = new Dictionary<int, DateOnly>();
            for (int c = 1; c < header.LastCellNum; c++)
            {
                var cell = header.GetCell(c);
                if (cell == null) continue;
                if (cell.CellType == CellType.Numeric && DateUtil.IsCellDateFormatted(cell))
                {
                    colDate[c] = DateOnly.FromDateTime(cell.DateCellValue ?? DateTime.MinValue);
                    continue;
                }
                var raw = (cell.ToString() ?? "").Trim();
                if (DateOnly.TryParseExact(raw, "dd.MM.yyyy", CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out var d))
                    colDate[c] = d;
            }
            if (colDate.Count == 0)
                return (new(), null, null,
                    "Keine Datums-Spalten in der Kopfzeile erkannt (erwartet Format TT.MM.JJJJ).");

            var orderedCols = colDate.OrderBy(kv => kv.Value).ToList();
            var spans = new List<PreviewRow>();
            int rowNum = 0;

            for (int rIdx = 1; rIdx <= sheet.LastRowNum; rIdx++)
            {
                var row = sheet.GetRow(rIdx);
                if (row == null) continue;
                var name = (row.GetCell(0)?.ToString() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                // (Datum, Roh-Code) in Datums-Reihenfolge einsammeln.
                var marks = new List<(DateOnly Date, string Raw)>();
                foreach (var kv in orderedCols)
                {
                    var v = (row.GetCell(kv.Key)?.ToString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(v)) marks.Add((kv.Value, v));
                }
                if (marks.Count == 0) continue;

                // Konsekutive Tage mit demselben Code zu Spans zusammenfassen.
                PreviewRow? cur = null;
                string?     curCode = null;
                DateOnly?   curLast = null;
                foreach (var (date, raw) in marks)
                {
                    bool   hadStar = raw.Contains('*');
                    string norm    = raw.TrimStart('*').Trim().ToUpperInvariant();

                    if (cur != null && curCode == norm
                        && curLast.HasValue && date == curLast.Value.AddDays(1))
                    {
                        cur.DateTo = date.ToString("yyyy-MM-dd");
                        cur.Days.Add(date.ToString("yyyy-MM-dd"));
                        cur.DayCount = cur.Days.Count;
                        if (hadStar) cur.HadStar = true;
                        curLast = date;
                    }
                    else
                    {
                        rowNum++;
                        cur = new PreviewRow
                        {
                            RowNum      = rowNum,
                            RawName     = name,
                            Code        = norm,
                            AbsenceType = CodeMap.TryGetValue(norm, out var at) ? at : "",
                            DateFrom    = date.ToString("yyyy-MM-dd"),
                            DateTo      = date.ToString("yyyy-MM-dd"),
                            Days        = new List<string> { date.ToString("yyyy-MM-dd") },
                            DayCount    = 1,
                            HadStar     = hadStar,
                        };
                        if (string.IsNullOrEmpty(cur.AbsenceType))
                        {
                            cur.Status = "UNKNOWN_CODE";
                            cur.Note   = $"Unbekannter Code \"{norm}\" — wird nicht importiert.";
                        }
                        spans.Add(cur);
                        curCode = norm;
                        curLast = date;
                    }
                }
            }

            wb.Close();
            return (spans,
                    orderedCols.First().Value.ToString("yyyy-MM-dd"),
                    orderedCols.Last().Value.ToString("yyyy-MM-dd"),
                    null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[RosterAbsenceImport] Parse fehlgeschlagen");
            return (new(), null, null, "Datei konnte nicht gelesen werden: " + ex.Message);
        }
    }

    // ── Token-basierter Namensvergleich (1:1 aus EmployeeStammdatenImportController) ──

    private static HashSet<string> NameTokens(params string?[] parts)
    {
        var tokens = new HashSet<string>();
        foreach (var p in parts)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            foreach (var tok in p.Split(new[] { ' ', '-', '–', '.', '\t', '/' },
                                        StringSplitOptions.RemoveEmptyEntries))
            {
                var t = tok.Trim().ToLowerInvariant();
                if (t.Length > 0) tokens.Add(t);
            }
        }
        return tokens;
    }

    private static bool NameTokensMatch(string? dbFirst, string? dbLast,
                                        string? csvFirst, string? csvLast)
    {
        var db  = NameTokens(dbFirst, dbLast);
        var csv = NameTokens(csvFirst, csvLast);
        if (db.Count == 0 || csv.Count == 0) return false;
        var smaller = db.Count <= csv.Count ? db : csv;
        var larger  = db.Count <= csv.Count ? csv : db;
        if (smaller.Count < 2) return false;          // mind. Vor- + Nachname
        return smaller.IsSubsetOf(larger);
    }
}

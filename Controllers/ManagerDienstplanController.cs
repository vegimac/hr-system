using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Manager-Dienstplan (Walter-Vorgabe 08.08.2026, ersetzt die Excel):
/// Monats-Grid über ALLE Filialen — Zeilen = FIX-M-MA mit laufendem Vertrag
/// (Heimatfiliale = Filiale des laufenden FIX-M-Vertrags), Zellen = Kürzel
/// aus dienstplan_code. Absenzen (Ferien/Krank/…) kommen als Live-Overlay
/// aus den Absences und sperren die Zelle serverseitig.
///
/// Rechte: SEHEN darf jeder mit Zugriff (admin/superuser/user), PLANEN darf
/// admin überall, sonst nur Filialen mit user_branch_access.can_dienstplan
/// (Pflege im Filial-Tab «Unterzeichner»).
/// </summary>
[Authorize(Roles = "admin,superuser,user")]
[ApiController]
[Route("api/manager-dienstplan")]
public class ManagerDienstplanController : ControllerBase
{
    private readonly AppDbContext _db;
    public ManagerDienstplanController(AppDbContext db) => _db = db;

    private int? GetUserId()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private async Task<(bool isAdmin, HashSet<int> planBranches, string? name)> GetPlanRechteAsync()
    {
        var uid = GetUserId();
        var isAdmin = User.IsInRole("admin");
        var branches = new HashSet<int>();
        string? name = null;
        if (uid.HasValue)
        {
            // DisplayName ist ein berechnetes Property (nicht EF-übersetzbar) —
            // Rohfelder laden und im Speicher zusammensetzen.
            var u = await _db.AppUsers.AsNoTracking()
                .Where(x => x.Id == uid.Value)
                .Select(x => new
                {
                    x.FirstName, x.LastName, x.Username,
                    Branches = x.BranchAccess.Where(b => b.CanDienstplan)
                                             .Select(b => b.CompanyProfileId).ToList(),
                })
                .FirstOrDefaultAsync();
            if (u != null)
            {
                var voll = $"{u.FirstName} {u.LastName}".Trim();
                name = string.IsNullOrWhiteSpace(voll) ? u.Username : voll;
                foreach (var b in u.Branches) branches.Add(b);
            }
        }
        return (isAdmin, branches, name);
    }

    /// <summary>Monats-Grid: Zeilen, Plan-Zellen, Absenz-Overlay, Kürzel-Katalog.</summary>
    [HttpGet]
    public async Task<IActionResult> GetMonth([FromQuery] int year, [FromQuery] int month)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return BadRequest(new { error = "PERIODE_UNGUELTIG" });
        var from = new DateOnly(year, month, 1);
        var to   = from.AddMonths(1).AddDays(-1);
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MinValue);

        // FIX-M-MA mit im Monat LAUFENDEM Vertrag; Heimatfiliale = Filiale
        // des (spätesten) laufenden FIX-M-Vertrags.
        var fixmRows = await _db.Employments.AsNoTracking()
            .Where(em => em.EmploymentModel == "FIX-M"
                      && em.CompanyProfileId != null
                      && em.ContractStartDate <= toDt
                      && (em.ContractEndDate == null || em.ContractEndDate >= fromDt)
                      && !em.Employee!.IsHidden && !em.Employee!.IsPayrollExcluded)
            .Select(em => new
            {
                em.EmployeeId, em.CompanyProfileId, em.ContractStartDate,
                em.Employee!.FirstName, em.Employee!.LastName,
                JobCode = em.JobGroup != null ? em.JobGroup.Code : em.JobTitle,
            })
            .ToListAsync();
        var proMa = fixmRows
            .GroupBy(x => x.EmployeeId)
            .Select(g => g.OrderByDescending(x => x.ContractStartDate).First())
            .ToList();
        var empIds = proMa.Select(x => x.EmployeeId).ToList();

        var branches = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.RestaurantCode, c.BranchName, c.City })
            .ToListAsync();

        var plan = await _db.ManagerDienstplanEntries.AsNoTracking()
            .Where(p => empIds.Contains(p.EmployeeId) && p.Datum >= from && p.Datum <= to)
            .Select(p => new { p.EmployeeId, p.Datum, p.Code })
            .ToListAsync();

        var absences = await _db.Absences.AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId) && a.DateFrom <= to && a.DateTo >= from)
            .Select(a => new { a.EmployeeId, a.AbsenceType, a.DateFrom, a.DateTo })
            .ToListAsync();

        var codes = await _db.DienstplanCodes.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new { c.Code, c.Bezeichnung, c.Farbe })
            .ToListAsync();

        var (isAdmin, planBranches, _) = await GetPlanRechteAsync();

        // GF (REST_MANAGER) pro Filiale zuoberst, danach alphabetisch (Walter 08.08.2026).
        var zeilen = proMa
            .OrderBy(x => branches.FirstOrDefault(b => b.Id == x.CompanyProfileId)?.RestaurantCode ?? "")
            .ThenByDescending(x => x.JobCode == "REST_MANAGER")
            .ThenBy(x => x.FirstName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new
            {
                employeeId = x.EmployeeId,
                vorname = x.FirstName,
                nachname = x.LastName,
                istGf = x.JobCode == "REST_MANAGER",
                companyProfileId = x.CompanyProfileId,
                planbar = isAdmin || (x.CompanyProfileId.HasValue && planBranches.Contains(x.CompanyProfileId.Value)),
                zellen = plan.Where(p => p.EmployeeId == x.EmployeeId)
                    .ToDictionary(p => p.Datum.ToString("yyyy-MM-dd"), p => p.Code),
                absenzen = absences.Where(a => a.EmployeeId == x.EmployeeId)
                    .Select(a => new
                    {
                        typ = a.AbsenceType,
                        von = (a.DateFrom < from ? from : a.DateFrom).ToString("yyyy-MM-dd"),
                        bis = (a.DateTo > to ? to : a.DateTo).ToString("yyyy-MM-dd"),
                    }).ToList(),
            })
            .ToList();

        return Ok(new
        {
            year, month,
            filialen = branches.Select(b => new { b.Id, code = b.RestaurantCode, name = b.BranchName ?? b.City }),
            zeilen,
            codes,
        });
    }

    public class CellDto
    {
        public int EmployeeId { get; set; }
        public string? Datum { get; set; }   // ISO yyyy-MM-dd
        public string? Code { get; set; }    // null/leer = Zelle löschen
    }

    /// <summary>Zelle setzen/löschen — nur mit Planungsrecht auf der Heimatfiliale des MA.</summary>
    [HttpPut("cell")]
    public async Task<IActionResult> PutCell([FromBody] CellDto dto)
    {
        if (!DateOnly.TryParse(dto.Datum, out var datum))
            return BadRequest(new { error = "DATUM_UNGUELTIG" });

        var (isAdmin, planBranches, name) = await GetPlanRechteAsync();
        if (!isAdmin)
        {
            // Heimatfiliale des MA am Datum (laufender FIX-M-Vertrag).
            var dt = datum.ToDateTime(TimeOnly.MinValue);
            var cpId = await _db.Employments.AsNoTracking()
                .Where(em => em.EmployeeId == dto.EmployeeId
                          && em.EmploymentModel == "FIX-M"
                          && em.ContractStartDate <= dt
                          && (em.ContractEndDate == null || em.ContractEndDate >= dt))
                .OrderByDescending(em => em.ContractStartDate)
                .Select(em => em.CompanyProfileId)
                .FirstOrDefaultAsync();
            if (!cpId.HasValue || !planBranches.Contains(cpId.Value))
                return StatusCode(403, new
                {
                    error = "KEIN_PLANRECHT",
                    message = "Kein Planungsrecht für die Filiale dieses MA (Filial-Tab «Unterzeichner» → Häkchen «Dienstplan»).",
                });
        }

        // Absenz-Kollision: Zelle mit Absenz ist gesperrt.
        bool absenz = await _db.Absences.AnyAsync(a =>
            a.EmployeeId == dto.EmployeeId && a.DateFrom <= datum && a.DateTo >= datum);
        if (absenz)
            return Conflict(new
            {
                error = "ABSENZ_GESPERRT",
                message = "An diesem Tag besteht eine Absenz — Planung nicht möglich (Absenzen im MA-Detail pflegen).",
            });

        var entry = await _db.ManagerDienstplanEntries
            .FirstOrDefaultAsync(p => p.EmployeeId == dto.EmployeeId && p.Datum == datum);
        var code = (dto.Code ?? "").Trim();
        if (string.IsNullOrEmpty(code))
        {
            if (entry != null) _db.ManagerDienstplanEntries.Remove(entry);
        }
        else
        {
            bool gueltig = await _db.DienstplanCodes.AnyAsync(c => c.Code == code && c.IsActive);
            if (!gueltig) return BadRequest(new { error = "CODE_UNBEKANNT" });
            if (entry == null)
            {
                entry = new ManagerDienstplanEntry { EmployeeId = dto.EmployeeId, Datum = datum };
                _db.ManagerDienstplanEntries.Add(entry);
            }
            entry.Code = code;
            entry.UpdatedAt = DateTime.Now;
            entry.UpdatedBy = name;
        }
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Einmal-Import aus der alten Excel «Manager DP 2026.xlsx»
    //  (Walter-Vorgabe 08.08.2026). Struktur: 12 Monats-Sheets JAN…DEZ,
    //  Zeile «Datum» = Tag-Spalten, Filial-Blöcke (Zelle A = FILIALORT in
    //  Grossbuchstaben), darunter Namenszeilen mit Kürzeln pro Tag.
    //  Übernommen werden NUR Kürzel aus unserem dienstplan_code-Katalog
    //  (F/M/S/-/SK/SKM) — Absenz-Zeichen der Excel (#, K, *, …) werden
    //  übersprungen, weil Absenzen bei uns aus dem System kommen. Tage mit
    //  System-Absenz werden ebenfalls übersprungen (Absenz gewinnt).
    // ═════════════════════════════════════════════════════════════════════
    private static readonly Dictionary<string, int> SHEET_MONATE = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JAN"] = 1, ["FEB"] = 2, ["MAR"] = 3, ["APR"] = 4, ["MAI"] = 5, ["JUN"] = 6,
        ["JUL"] = 7, ["AUG"] = 8, ["SEP"] = 9, ["OKT"] = 10, ["NOV"] = 11, ["DEZ"] = 12,
    };

    [Authorize(Roles = "admin")]
    [HttpPost("import-excel")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> ImportExcel([FromForm] IFormFile file, [FromQuery] int year = 2026, [FromQuery] bool dryRun = true)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "DATEI_FEHLT" });

        NPOI.SS.UserModel.IWorkbook wb;
        try
        {
            using var stream = file.OpenReadStream();
            wb = new NPOI.XSSF.UserModel.XSSFWorkbook(stream);
        }
        catch
        {
            return BadRequest(new { error = "DATEI_UNGUELTIG", message = "Datei konnte nicht als Excel (.xlsx) gelesen werden." });
        }

        // Roster: alle FIX-M-MA mit Vertrag im Jahr, Heimatfiliale = spätester Vertrag.
        var jahrFrom = new DateTime(year, 1, 1);
        var jahrTo   = new DateTime(year, 12, 31);
        var fixm = await _db.Employments.AsNoTracking()
            .Where(em => em.EmploymentModel == "FIX-M"
                      && em.CompanyProfileId != null
                      && em.ContractStartDate <= jahrTo
                      && (em.ContractEndDate == null || em.ContractEndDate >= jahrFrom)
                      && !em.Employee!.IsHidden && !em.Employee!.IsPayrollExcluded)
            .Select(em => new { em.EmployeeId, em.CompanyProfileId, em.ContractStartDate, em.Employee!.FirstName, em.Employee!.LastName })
            .ToListAsync();
        var roster = fixm.GroupBy(x => x.EmployeeId)
            .Select(g => g.OrderByDescending(x => x.ContractStartDate).First())
            .ToList();

        var branches = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.RestaurantCode, c.BranchName, c.City })
            .ToListAsync();
        var codes = await _db.DienstplanCodes.Where(c => c.IsActive).Select(c => c.Code).ToListAsync();
        var codeSet = new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);

        var absences = await _db.Absences.AsNoTracking()
            .Where(a => a.DateFrom <= new DateOnly(year, 12, 31) && a.DateTo >= new DateOnly(year, 1, 1))
            .Select(a => new { a.EmployeeId, a.DateFrom, a.DateTo })
            .ToListAsync();

        var eintraege = new Dictionary<(int empId, DateOnly datum), string>();
        var matched = new List<object>();
        var matchedNames = new HashSet<string>();   // «SHEET|BRANCHZEILE|NAME» einmalig melden
        var unmatched = new List<object>();
        var unmatchedNames = new HashSet<string>();
        var skippedCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int absenzGesperrt = 0;

        for (int s = 0; s < wb.NumberOfSheets; s++)
        {
            var sheet = wb.GetSheetAt(s);
            if (!SHEET_MONATE.TryGetValue(sheet.SheetName.Trim(), out var monat)) continue;
            int tageImMonat = DateTime.DaysInMonth(year, monat);

            // «Datum»-Zeile suchen → Spalte→Tag-Mapping.
            var tagSpalten = new Dictionary<int, int>();
            int datumRowIdx = -1;
            for (int r = 0; r <= Math.Min(sheet.LastRowNum, 10); r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;
                if (string.Equals(CellText(row.GetCell(0)), "Datum", StringComparison.OrdinalIgnoreCase))
                {
                    datumRowIdx = r;
                    for (int c = 1; c <= row.LastCellNum; c++)
                    {
                        var t = CellText(row.GetCell(c));
                        if (int.TryParse(t, out var tag) && tag >= 1 && tag <= 31)
                            tagSpalten[c] = tag;
                    }
                    break;
                }
            }
            if (datumRowIdx < 0 || tagSpalten.Count == 0) continue;

            object? curBranch = null;
            List<(int EmpId, string FirstName, string LastName)> branchMas = new();
            string curBranchLabel = "";

            for (int r = datumRowIdx + 2; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;
                var nameCell = CellText(row.GetCell(0)).Trim();
                if (string.IsNullOrEmpty(nameCell)) continue;

                // Filial-Header? (Ortsname in GROSSBUCHSTABEN, matcht City/BranchName)
                var br = branches.FirstOrDefault(b =>
                    (!string.IsNullOrEmpty(b.City) && b.City.Contains(nameCell, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(b.BranchName) && b.BranchName.Contains(nameCell, StringComparison.OrdinalIgnoreCase)));
                if (br != null && nameCell == nameCell.ToUpperInvariant() && nameCell.Length >= 4)
                {
                    curBranch = br;
                    curBranchLabel = nameCell;
                    branchMas = roster.Where(x => x.CompanyProfileId == br.Id)
                        .Select(x => (x.EmployeeId, x.FirstName ?? "", x.LastName ?? "")).ToList();
                    continue;
                }
                if (curBranch == null) continue;   // Zeilen vor dem ersten Filial-Block (Supervisoren) überspringen

                // MA-Match: exakt → Präfix (≥3) → Tippfehler-Toleranz (Levenshtein ≤ 2).
                var empId = MatchName(nameCell, branchMas, out var maName);
                var meldeKey = $"{curBranchLabel}|{nameCell}";
                if (empId == null)
                {
                    if (unmatchedNames.Add(meldeKey))
                        unmatched.Add(new { name = nameCell, filiale = curBranchLabel });
                    continue;
                }
                if (matchedNames.Add(meldeKey))
                    matched.Add(new { name = nameCell, ma = maName, filiale = curBranchLabel });

                foreach (var (col, tag) in tagSpalten)
                {
                    if (tag > tageImMonat) continue;
                    var code = CellText(row.GetCell(col)).Trim();
                    if (string.IsNullOrEmpty(code)) continue;
                    if (!codeSet.Contains(code))
                    {
                        skippedCodes[code] = skippedCodes.GetValueOrDefault(code) + 1;
                        continue;
                    }
                    var datum = new DateOnly(year, monat, tag);
                    if (absences.Any(a => a.EmployeeId == empId.Value && a.DateFrom <= datum && a.DateTo >= datum))
                    {
                        absenzGesperrt++;
                        continue;
                    }
                    var kanonisch = codes.First(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
                    eintraege[(empId.Value, datum)] = kanonisch;
                }
            }
        }

        if (!dryRun && eintraege.Count > 0)
        {
            var (_, _, actorName) = await GetPlanRechteAsync();
            var von = new DateOnly(year, 1, 1);
            var bis = new DateOnly(year, 12, 31);
            var empIds = eintraege.Keys.Select(k => k.empId).Distinct().ToList();
            var existing = await _db.ManagerDienstplanEntries
                .Where(p => empIds.Contains(p.EmployeeId) && p.Datum >= von && p.Datum <= bis)
                .ToListAsync();
            var byKey = existing.ToDictionary(p => (p.EmployeeId, p.Datum));
            foreach (var ((empId, datum), code) in eintraege)
            {
                if (byKey.TryGetValue((empId, datum), out var entry))
                {
                    entry.Code = code;
                    entry.UpdatedAt = DateTime.Now;
                    entry.UpdatedBy = actorName;
                }
                else
                {
                    _db.ManagerDienstplanEntries.Add(new ManagerDienstplanEntry
                    {
                        EmployeeId = empId, Datum = datum, Code = code,
                        UpdatedAt = DateTime.Now, UpdatedBy = actorName,
                    });
                }
            }
            await _db.SaveChangesAsync();
        }

        return Ok(new
        {
            dryRun, year,
            eintraege = eintraege.Count,
            matched,
            unmatched,
            uebersprungeneKuerzel = skippedCodes.OrderByDescending(kv => kv.Value).Select(kv => new { kuerzel = kv.Key, anzahl = kv.Value }),
            absenzGesperrt,
        });
    }

    private static string CellText(NPOI.SS.UserModel.ICell? cell)
    {
        if (cell == null) return "";
        return cell.CellType switch
        {
            NPOI.SS.UserModel.CellType.String => cell.StringCellValue ?? "",
            NPOI.SS.UserModel.CellType.Numeric => cell.NumericCellValue == Math.Floor(cell.NumericCellValue)
                ? ((long)cell.NumericCellValue).ToString()
                : cell.NumericCellValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NPOI.SS.UserModel.CellType.Formula => cell.CachedFormulaResultType == NPOI.SS.UserModel.CellType.String
                ? cell.StringCellValue ?? "" : "",
            _ => "",
        };
    }

    /// <summary>Excel-Name (oft Kurzform: «Sinthy», «Xheva») → MA der Filiale.</summary>
    private static int? MatchName(string excelName, List<(int EmpId, string FirstName, string LastName)> mas, out string? maName)
    {
        maName = null;
        var n = excelName.Trim();
        // 1) exakt (Vorname)
        var hit = mas.Where(m => string.Equals(m.FirstName, n, StringComparison.OrdinalIgnoreCase)).ToList();
        // 2) Präfix in beide Richtungen (≥ 3 Zeichen)
        if (hit.Count == 0 && n.Length >= 3)
            hit = mas.Where(m => m.FirstName.StartsWith(n, StringComparison.OrdinalIgnoreCase)
                              || n.StartsWith(m.FirstName, StringComparison.OrdinalIgnoreCase)).ToList();
        // 3) Tippfehler-Toleranz (Levenshtein ≤ 2 bei Namen ab 5 Zeichen)
        if (hit.Count == 0 && n.Length >= 5)
            hit = mas.Where(m => Levenshtein(m.FirstName.ToLowerInvariant(), n.ToLowerInvariant()) <= 2).ToList();
        if (hit.Count != 1) return null;   // 0 oder mehrdeutig → manuell klären
        maName = $"{hit[0].FirstName} {hit[0].LastName}".Trim();
        return hit[0].EmpId;
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                                   d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}

using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace HrSystem.Controllers;

/// <summary>
/// Manager-Schulungen (Walter-Vorgabe 14.08.2026): Übersicht + Pflege der
/// drei wiederkehrenden Schulungen Nothelfer / Peak-Verifizierung / Seco
/// für FIX-M-Manager, plus eID/SSO (gibt es für ALLE MA — Pflege auch im
/// MA-Personal-Tab). Gültigkeitsdauer pro Schulung in Monaten (app_setting,
/// SchulungConfig). Einmal-Import aus der Excel «Nothelfer_…xlsx».
/// Warnungen laufen über DashboardService (schulung_nothelfer/peak/seco).
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/manager-schulungen")]
public class ManagerSchulungenController : ControllerBase
{
    private readonly AppDbContext _db;
    public ManagerSchulungenController(AppDbContext db) { _db = db; }

    private async Task<(int nothelfer, int peak, int seco)> LoadMonateAsync()
    {
        var s = await _db.AppSettings.AsNoTracking()
            .Where(x => x.Key.StartsWith("Schulung."))
            .ToDictionaryAsync(x => x.Key, x => x.Value);
        return (
            SchulungConfig.ParseMonate(s.TryGetValue(SchulungConfig.KeyNothelfer, out var a) ? a : null, SchulungConfig.DefaultNothelfer),
            SchulungConfig.ParseMonate(s.TryGetValue(SchulungConfig.KeyPeak, out var b) ? b : null, SchulungConfig.DefaultPeak),
            SchulungConfig.ParseMonate(s.TryGetValue(SchulungConfig.KeySeco, out var c) ? c : null, SchulungConfig.DefaultSeco));
    }

    private static object SchulungCell(DateTime? am, int monate)
    {
        if (am is null) return new { am = (string?)null, bis = (string?)null, status = "fehlt" };
        var bis = DateOnly.FromDateTime(am.Value).AddMonths(monate);
        var tage = bis.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber;
        var status = tage < 0 ? "abgelaufen" : tage <= 60 ? "bald" : "ok";
        return new
        {
            am = am.Value.ToString("yyyy-MM-dd"),
            bis = bis.ToString("yyyy-MM-dd"),
            tage,
            status,
        };
    }

    // ── GET /api/manager-schulungen ──────────────────────────────────────
    /// <summary>Matrix: FIX-M-Manager × (eID, SSO, 3 Schulungen mit gültig-bis).</summary>
    [HttpGet]
    public async Task<IActionResult> GetOverview()
    {
        var (nh, pk, se) = await LoadMonateAsync();

        var rows = await _db.Employments.AsNoTracking()
            .Where(em => em.EmploymentModel == "FIX-M" && em.IsActive
                      && em.CompanyProfileId != null
                      && !em.Employee!.IsHidden && !em.Employee!.IsPayrollExcluded
                      && em.Employee!.IsActive)
            .Select(em => new
            {
                em.EmployeeId, em.CompanyProfileId, em.ContractStartDate,
                em.Employee!.FirstName, em.Employee!.LastName, em.Employee!.EmployeeNumber,
                em.Employee!.Eid, em.Employee!.Sso,
                em.Employee!.SchulungNothelferAm, em.Employee!.SchulungPeakAm, em.Employee!.SchulungSecoAm,
                JobCode = em.JobGroup != null ? em.JobGroup.Code : em.JobTitle,
            })
            .ToListAsync();
        var proMa = rows.GroupBy(x => x.EmployeeId)
            .Select(g => g.OrderByDescending(x => x.ContractStartDate).First())
            .ToList();

        var branches = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.RestaurantCode, c.City, c.BranchName, c.WorkLocation })
            .ToListAsync();

        var zeilen = proMa
            .OrderBy(x => branches.FirstOrDefault(b => b.Id == x.CompanyProfileId)?.RestaurantCode ?? "")
            .ThenByDescending(x => x.JobCode == "REST_MANAGER")
            .ThenBy(x => x.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LastName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new
            {
                employeeId = x.EmployeeId,
                vorname = x.FirstName ?? "",
                nachname = x.LastName ?? "",
                employeeNumber = x.EmployeeNumber,
                companyProfileId = x.CompanyProfileId,
                filiale = branches.Where(b => b.Id == x.CompanyProfileId)
                    .Select(b => !string.IsNullOrWhiteSpace(b.WorkLocation) ? b.WorkLocation : (b.City ?? b.BranchName))
                    .FirstOrDefault(),
                istGf = x.JobCode == "REST_MANAGER",
                eid = x.Eid,
                sso = x.Sso,
                nothelfer = SchulungCell(x.SchulungNothelferAm, nh),
                peak = SchulungCell(x.SchulungPeakAm, pk),
                seco = SchulungCell(x.SchulungSecoAm, se),
            });

        return Ok(new
        {
            settings = new { nothelferMonate = nh, peakMonate = pk, secoMonate = se },
            zeilen,
        });
    }

    public class SchulungDto
    {
        public string? Eid { get; set; }
        public string? Sso { get; set; }
        public string? NothelferAm { get; set; }
        public string? PeakAm { get; set; }
        public string? SecoAm { get; set; }
    }

    // ── PUT /api/manager-schulungen/{empId} ──────────────────────────────
    /// <summary>Pflege pro Manager: eID, SSO, 3 Schulungsdaten (leer = löschen).</summary>
    [HttpPut("{empId:int}")]
    public async Task<IActionResult> Update(int empId, [FromBody] SchulungDto dto)
    {
        var emp = await _db.Employees.FindAsync(empId);
        if (emp is null) return NotFound();

        static DateTime? P(string? s) =>
            DateTime.TryParse(s, out var d) ? d.Date : null;

        emp.Eid = string.IsNullOrWhiteSpace(dto.Eid) ? null : dto.Eid.Trim();
        emp.Sso = string.IsNullOrWhiteSpace(dto.Sso) ? null : dto.Sso.Trim();
        emp.SchulungNothelferAm = P(dto.NothelferAm);
        emp.SchulungPeakAm      = P(dto.PeakAm);
        emp.SchulungSecoAm      = P(dto.SecoAm);
        await _db.SaveChangesAsync();
        return Ok();
    }

    public class SettingsDto
    {
        public int NothelferMonate { get; set; }
        public int PeakMonate { get; set; }
        public int SecoMonate { get; set; }
    }

    // ── PUT /api/manager-schulungen/settings ─────────────────────────────
    /// <summary>Gültigkeitsdauer pro Schulung in Monaten (admin).</summary>
    [Authorize(Roles = "admin")]
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] SettingsDto dto)
    {
        if (dto.NothelferMonate <= 0 || dto.PeakMonate <= 0 || dto.SecoMonate <= 0)
            return BadRequest(new { error = "INVALID", message = "Monate müssen grösser als 0 sein." });

        async Task Upsert(string key, int val)
        {
            var s = await _db.AppSettings.FindAsync(key);
            if (s is null) _db.AppSettings.Add(new Models.AppSetting { Key = key, Value = val.ToString() });
            else { s.Value = val.ToString(); s.UpdatedAt = DateTime.UtcNow; }
        }
        await Upsert(SchulungConfig.KeyNothelfer, dto.NothelferMonate);
        await Upsert(SchulungConfig.KeyPeak, dto.PeakMonate);
        await Upsert(SchulungConfig.KeySeco, dto.SecoMonate);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── POST /api/manager-schulungen/import-excel?dryRun= ────────────────
    /// <summary>
    /// Einmal-Import aus der Nothelfer-Excel: Spalten Name / Nothelfer /
    /// Peak-Verif. / SSO / Seco / ID(=eID) / Gb.D. Match per Namens-Tokens,
    /// Geburtsdatum als Absicherung. Betrifft ALLE gematchten MA (eID/SSO
    /// auch für Nicht-Manager); nur gefüllte Excel-Werte überschreiben.
    /// </summary>
    [Authorize(Roles = "admin")]
    [HttpPost("import-excel")]
    public async Task<IActionResult> ImportExcel(IFormFile file, [FromQuery] bool dryRun = true)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "DATEI_FEHLT", message = "Bitte eine .xlsx-Datei hochladen." });

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        stream.Position = 0;
        IWorkbook wb;
        try { wb = new XSSFWorkbook(stream); }
        catch { return BadRequest(new { error = "FORMAT", message = "Datei konnte nicht als .xlsx gelesen werden." }); }
        var sheet = wb.GetSheetAt(0);

        // Header-Zeile finden (Zelle A = «Name»).
        int headerRow = -1;
        var colIdx = new Dictionary<string, int>();
        for (int r = 0; r <= Math.Min(10, sheet.LastRowNum); r++)
        {
            var row = sheet.GetRow(r);
            if (row is null) continue;
            for (int c = 0; c < row.LastCellNum; c++)
            {
                var v = (row.GetCell(c)?.ToString() ?? "").Trim().ToLowerInvariant();
                if (c == 0 && v == "name") headerRow = r;
            }
            if (headerRow == r)
            {
                for (int c = 0; c < row.LastCellNum; c++)
                {
                    var v = (row.GetCell(c)?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (v.StartsWith("nothelfer")) colIdx["nothelfer"] = c;
                    else if (v.StartsWith("peak")) colIdx["peak"] = c;
                    else if (v.StartsWith("seco")) colIdx["seco"] = c;
                    else if (v == "sso") colIdx["sso"] = c;
                    else if (v == "id") colIdx["eid"] = c;
                    else if (v.StartsWith("gb")) colIdx["geb"] = c;
                }
                break;
            }
        }
        if (headerRow < 0 || !colIdx.ContainsKey("nothelfer"))
            return BadRequest(new { error = "HEADER_FEHLT", message = "Kopfzeile (Name / Nothelfer / Peak-Verif. / SSO / Seco / ID / Gb.D) nicht gefunden." });

        static DateTime? CellDate(ICell? cell)
        {
            if (cell is null) return null;
            try
            {
                if (cell.CellType == CellType.Numeric && DateUtil.IsCellDateFormatted(cell))
                    return cell.DateCellValue?.Date;
            }
            catch { }
            var s = (cell.ToString() ?? "").Trim();
            return DateTime.TryParse(s, out var d) ? d.Date : null;
        }
        static string? CellText(ICell? cell)
        {
            var s = (cell?.ToString() ?? "").Trim();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        static HashSet<string> Tokens(string? name) =>
            (name ?? "").ToLowerInvariant()
                .Split(new[] { ' ', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 1)
                .ToHashSet();

        // Alle MA laden (Match-Pool; auch inaktive — eID/SSO schadet nicht).
        var emps = await _db.Employees
            .Where(e => !e.IsHidden)
            .ToListAsync();
        var empInfo = emps.Select(e => new
        {
            Emp = e,
            Toks = Tokens($"{e.FirstName} {e.LastName}"),
            Geb = e.DateOfBirth?.Date,
        }).ToList();

        var matched = new List<object>();
        var unmatched = new List<object>();
        int updated = 0;

        for (int r = headerRow + 1; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is null) continue;
            var name = CellText(row.GetCell(0));
            if (name is null) continue;

            var toks = Tokens(name);
            if (toks.Count == 0) continue;
            var geb = colIdx.TryGetValue("geb", out var gc) ? CellDate(row.GetCell(gc)) : null;

            // Match: 1) Geburtsdatum + mind. 1 Namens-Token,
            //        2) sonst alle Excel-Tokens ⊆ DB-Tokens (oder umgekehrt).
            var cands = empInfo.Where(x =>
                (geb != null && x.Geb == geb && x.Toks.Overlaps(toks))
                || toks.IsSubsetOf(x.Toks) || x.Toks.IsSubsetOf(toks)).ToList();
            if (geb != null && cands.Count > 1)
                cands = cands.Where(x => x.Geb == geb).ToList();

            if (cands.Count != 1)
            {
                unmatched.Add(new { zeile = r + 1, name, grund = cands.Count == 0 ? "kein Treffer" : "mehrdeutig" });
                continue;
            }
            var emp = cands[0].Emp;

            var eid = colIdx.TryGetValue("eid", out var ec) ? CellText(row.GetCell(ec)) : null;
            var sso = colIdx.TryGetValue("sso", out var sc) ? CellText(row.GetCell(sc)) : null;
            var nothelfer = CellDate(row.GetCell(colIdx["nothelfer"]));
            var peak = colIdx.TryGetValue("peak", out var pc) ? CellDate(row.GetCell(pc)) : null;
            var seco = colIdx.TryGetValue("seco", out var xc) ? CellDate(row.GetCell(xc)) : null;

            matched.Add(new
            {
                zeile = r + 1, name,
                employeeId = emp.Id,
                maName = $"{emp.FirstName} {emp.LastName}".Trim(),
                employeeNumber = emp.EmployeeNumber,
                eid, sso,
                nothelfer = nothelfer?.ToString("yyyy-MM-dd"),
                peak = peak?.ToString("yyyy-MM-dd"),
                seco = seco?.ToString("yyyy-MM-dd"),
            });

            if (!dryRun)
            {
                // Nur gefüllte Excel-Werte übernehmen (kein Leer-Überschreiben).
                if (eid != null) emp.Eid = eid;
                if (sso != null) emp.Sso = sso;
                if (nothelfer != null) emp.SchulungNothelferAm = nothelfer;
                if (peak != null) emp.SchulungPeakAm = peak;
                if (seco != null) emp.SchulungSecoAm = seco;
                updated++;
            }
        }

        if (!dryRun) await _db.SaveChangesAsync();
        return Ok(new { dryRun, matched, unmatched, updated });
    }
}

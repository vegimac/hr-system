using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Controllers;

/// <summary>
/// Absenz-Auswertung pro Filiale + Jahr.
///
/// Aggregiert die im Jahr erfassten Krankheits-, Unfall- und Mutter-/
/// Vaterschafts-Absenzen pro Mitarbeiter:in. Mehrjährige Absenzen werden
/// auf das gewählte Jahr beschnitten (Dec-Feb-Krankheit zählt im
/// Vorjahres-Report nur die Dezember-Tage, im aktuellen Jahres-Report die
/// Jan-Feb-Tage). Pro MA kommen Total-Tage + Anzahl Fälle pro Typ und
/// die einzelnen Absenz-Records (für den Drilldown im Frontend) zurück.
/// Sortiert nach Gesamt-Ausfalltagen absteigend.
///
/// Endpoint:
///   GET /api/reports/absences/branch/{cpid}?year=2026
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/reports/absences")]
public class AbsenceReportController : ControllerBase
{
    private readonly AppDbContext _db;
    public AbsenceReportController(AppDbContext db) => _db = db;

    // Welche Absenz-Typen die Auswertung berücksichtigt. Ferien/Schulung/etc.
    // bewusst NICHT — das ist eine Krank-/Unfall-Auswertung für HR-Aufsicht.
    private static readonly string[] TrackedTypes = new[] { "KRANK", "UNFALL", "MUTT_VATER" };

    public record DetailRow(
        int     Id,
        string  AbsenceType,
        string  DateFrom,       // yyyy-MM-dd
        string  DateTo,
        int     DaysInYear,     // Kalendertage des Zeitraums, geclipped aufs Berichtsjahr
        decimal Prozent,
        string? Notes);

    public record ReportRow(
        int     EmployeeId,
        string? EmployeeNumber,
        string  FirstName,
        string  LastName,
        string? EmploymentModel,
        bool    IsActive,
        int     KrankFaelle,    int KrankTage,
        int     UnfallFaelle,   int UnfallTage,
        int     MuttVaterFaelle, int MuttVaterTage,
        int     TotalFaelle,    int TotalTage,
        List<DetailRow> Details);

    // Filial-übergreifender Report: gleiche Felder wie ReportRow, plus die
    // primäre Filiale des MA (aktiv-Employment oder neueste). So sieht
    // Walter, in welcher Filiale der Ausfall sitzt.
    public record CrossBranchRow(
        int     EmployeeId,
        string? EmployeeNumber,
        string  FirstName,
        string  LastName,
        string? EmploymentModel,
        bool    IsActive,
        int?    CompanyProfileId,
        string? BranchName,
        string? RestaurantCode,
        int     KrankFaelle,    int KrankTage,
        int     UnfallFaelle,   int UnfallTage,
        int     MuttVaterFaelle, int MuttVaterTage,
        int     TotalFaelle,    int TotalTage,
        List<DetailRow> Details);

    [HttpGet("branch/{companyProfileId:int}")]
    public async Task<IActionResult> ByBranch(int companyProfileId, [FromQuery] int? year)
    {
        var refYear = year ?? DateTime.Today.Year;
        var from = new DateOnly(refYear, 1,  1);
        var to   = new DateOnly(refYear, 12, 31);

        // MA mit irgendeinem Employment in der Filiale (auch bereits ausgetretene
        // — sie könnten im Berichtsjahr noch Absenzen gehabt haben).
        var employees = await _db.Employees
            .Include(e => e.Employments)
            .Where(e => e.Employments.Any(emp => emp.CompanyProfileId == companyProfileId))
            .ToListAsync();
        var empIds = employees.Select(e => e.Id).ToList();
        if (empIds.Count == 0)
            return Ok(new {
                year = refYear, companyProfileId,
                from = from.ToString("yyyy-MM-dd"), to = to.ToString("yyyy-MM-dd"),
                totalEmployees = 0, totalCases = 0, totalDays = 0,
                rows = Array.Empty<ReportRow>()
            });

        // Absenzen die das Berichtsjahr berühren (Überlapp via DateFrom<=to AND DateTo>=from)
        var absences = await _db.Absences
            .Where(a => empIds.Contains(a.EmployeeId)
                     && TrackedTypes.Contains(a.AbsenceType)
                     && a.DateFrom <= to
                     && a.DateTo   >= from)
            .OrderBy(a => a.DateFrom)
            .ToListAsync();

        var byEmp = absences
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<ReportRow>();
        foreach (var e in employees)
        {
            if (!byEmp.TryGetValue(e.Id, out var list) || list.Count == 0)
                continue;   // nur MA mit Absenzen im Jahr in der Auswertung

            int kT = 0, kF = 0, uT = 0, uF = 0, mT = 0, mF = 0;
            var details = new List<DetailRow>();
            foreach (var a in list)
            {
                // Tage des Zeitraums, geclipped aufs Berichtsjahr.
                var df = a.DateFrom > from ? a.DateFrom : from;
                var dt = a.DateTo   < to   ? a.DateTo   : to;
                int days = dt.DayNumber - df.DayNumber + 1;
                if (days < 0) days = 0;

                switch (a.AbsenceType)
                {
                    case "KRANK":      kT += days; kF++; break;
                    case "UNFALL":     uT += days; uF++; break;
                    case "MUTT_VATER": mT += days; mF++; break;
                }

                details.Add(new DetailRow(
                    a.Id, a.AbsenceType,
                    a.DateFrom.ToString("yyyy-MM-dd"),
                    a.DateTo.ToString("yyyy-MM-dd"),
                    days, a.Prozent, a.Notes));
            }

            // Aktiv-Employment in der Filiale für Vertragsmodell-Anzeige.
            var fEmp = e.Employments
                .Where(emp => emp.CompanyProfileId == companyProfileId)
                .OrderByDescending(emp => emp.IsActive)
                .ThenByDescending(emp => emp.ContractStartDate)
                .FirstOrDefault();

            rows.Add(new ReportRow(
                e.Id, e.EmployeeNumber, e.FirstName ?? "", e.LastName ?? "",
                fEmp?.EmploymentModel, e.IsActive,
                kF, kT, uF, uT, mF, mT,
                kF + uF + mF, kT + uT + mT,
                details));
        }

        rows = rows
            .OrderByDescending(r => r.TotalTage)
            .ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.LastName,  StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(new {
            year           = refYear,
            companyProfileId,
            from           = from.ToString("yyyy-MM-dd"),
            to             = to.ToString("yyyy-MM-dd"),
            totalEmployees = rows.Count,
            totalCases     = rows.Sum(r => r.TotalFaelle),
            totalDays      = rows.Sum(r => r.TotalTage),
            rows
        });
    }

    /// <summary>
    /// Filial-übergreifende Top-Liste der schlimmsten Krank-/Unfall-/Mutter-
    /// Vater-Absenzen im Berichtsjahr. Sortiert nach Total-Ausfalltagen
    /// absteigend; jede Zeile zeigt zusätzlich die primäre Filiale des MA
    /// (aktive Employment oder neueste).
    ///
    /// Endpoint: GET /api/reports/absences/cross-branch?year=YYYY
    /// </summary>
    [HttpGet("cross-branch")]
    public async Task<IActionResult> CrossBranch([FromQuery] int? year)
    {
        var refYear = year ?? DateTime.Today.Year;
        var from = new DateOnly(refYear, 1,  1);
        var to   = new DateOnly(refYear, 12, 31);

        // Alle Absenzen der getrackten Typen im Jahr, filialunabhängig.
        var absences = await _db.Absences
            .Where(a => TrackedTypes.Contains(a.AbsenceType)
                     && a.DateFrom <= to
                     && a.DateTo   >= from)
            .OrderBy(a => a.DateFrom)
            .ToListAsync();

        if (absences.Count == 0)
            return Ok(new {
                year = refYear,
                from = from.ToString("yyyy-MM-dd"),
                to   = to.ToString("yyyy-MM-dd"),
                totalEmployees = 0, totalCases = 0, totalDays = 0, totalBranches = 0,
                rows = Array.Empty<CrossBranchRow>()
            });

        var empIds = absences.Select(a => a.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees
            .Include(e => e.Employments)
            .Where(e => empIds.Contains(e.Id))
            .ToListAsync();

        var branchIds = employees
            .SelectMany(e => e.Employments)
            .Where(emp => emp.CompanyProfileId.HasValue)
            .Select(emp => emp.CompanyProfileId!.Value)
            .Distinct()
            .ToList();
        var branchMap = await _db.CompanyProfiles
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id);

        var byEmp = absences
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<CrossBranchRow>();
        foreach (var e in employees)
        {
            if (!byEmp.TryGetValue(e.Id, out var list) || list.Count == 0)
                continue;

            int kT = 0, kF = 0, uT = 0, uF = 0, mT = 0, mF = 0;
            var details = new List<DetailRow>();
            foreach (var a in list)
            {
                var df = a.DateFrom > from ? a.DateFrom : from;
                var dt = a.DateTo   < to   ? a.DateTo   : to;
                int days = dt.DayNumber - df.DayNumber + 1;
                if (days < 0) days = 0;

                switch (a.AbsenceType)
                {
                    case "KRANK":      kT += days; kF++; break;
                    case "UNFALL":     uT += days; uF++; break;
                    case "MUTT_VATER": mT += days; mF++; break;
                }

                details.Add(new DetailRow(
                    a.Id, a.AbsenceType,
                    a.DateFrom.ToString("yyyy-MM-dd"),
                    a.DateTo.ToString("yyyy-MM-dd"),
                    days, a.Prozent, a.Notes));
            }

            // Primäre Filiale: aktives Employment bevorzugt, sonst neuestes.
            var primaryEmp = e.Employments
                .Where(emp => emp.CompanyProfileId.HasValue)
                .OrderByDescending(emp => emp.IsActive)
                .ThenByDescending(emp => emp.ContractStartDate)
                .FirstOrDefault();
            CompanyProfile? branch = null;
            if (primaryEmp?.CompanyProfileId is int cpid)
                branchMap.TryGetValue(cpid, out branch);

            rows.Add(new CrossBranchRow(
                e.Id, e.EmployeeNumber, e.FirstName ?? "", e.LastName ?? "",
                primaryEmp?.EmploymentModel, e.IsActive,
                branch?.Id,
                branch?.BranchName ?? branch?.CompanyName,
                branch?.RestaurantCode,
                kF, kT, uF, uT, mF, mT,
                kF + uF + mF, kT + uT + mT,
                details));
        }

        rows = rows
            .OrderByDescending(r => r.TotalTage)
            .ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.LastName,  StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(new {
            year           = refYear,
            from           = from.ToString("yyyy-MM-dd"),
            to             = to.ToString("yyyy-MM-dd"),
            totalEmployees = rows.Count,
            totalCases     = rows.Sum(r => r.TotalFaelle),
            totalDays      = rows.Sum(r => r.TotalTage),
            totalBranches  = rows.Select(r => r.CompanyProfileId).Where(x => x.HasValue).Distinct().Count(),
            rows
        });
    }

    // ════════════════════════════════════════════════════════════════════════
    // EXPORT-ENDPOINTS (XLSX + PDF) — Walter-Vorgabe 15.05.2026
    // ════════════════════════════════════════════════════════════════════════
    // Vier Endpoints: /branch/{cpid}/{xlsx|pdf} und /cross-branch/{xlsx|pdf}.
    // Datenbeschaffung läuft über separate Builder-Methoden (Daten-DTOs gleich
    // wie bei den JSON-Endpoints oben). Format-Generierung mit NPOI (XLSX) bzw.
    // QuestPDF (PDF).

    [HttpGet("branch/{companyProfileId:int}/xlsx")]
    public async Task<IActionResult> BranchXlsx(int companyProfileId, [FromQuery] int? year)
    {
        var data = await BuildBranchDataAsync(companyProfileId, year);
        var bytes = BuildBranchXlsx(data.Year, data.Rows, data.Branch);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            BuildFileName("xlsx", data.Year, data.Branch, isCross: false));
    }

    [HttpGet("branch/{companyProfileId:int}/pdf")]
    public async Task<IActionResult> BranchPdf(int companyProfileId, [FromQuery] int? year)
    {
        var data = await BuildBranchDataAsync(companyProfileId, year);
        var bytes = BuildBranchPdf(data.Year, data.Rows, data.Branch);
        return File(bytes, "application/pdf",
            BuildFileName("pdf", data.Year, data.Branch, isCross: false));
    }

    [HttpGet("cross-branch/xlsx")]
    public async Task<IActionResult> CrossBranchXlsx([FromQuery] int? year)
    {
        var data = await BuildCrossBranchDataAsync(year);
        var bytes = BuildCrossBranchXlsx(data.Year, data.Rows);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            BuildFileName("xlsx", data.Year, null, isCross: true));
    }

    [HttpGet("cross-branch/pdf")]
    public async Task<IActionResult> CrossBranchPdf([FromQuery] int? year)
    {
        var data = await BuildCrossBranchDataAsync(year);
        var bytes = BuildCrossBranchPdf(data.Year, data.Rows);
        return File(bytes, "application/pdf",
            BuildFileName("pdf", data.Year, null, isCross: true));
    }

    private static string BuildFileName(string ext, int year, CompanyProfile? branch, bool isCross)
    {
        if (isCross) return $"Absenz-Auswertung-{year}-alle-Filialen.{ext}";
        var b = branch?.RestaurantCode ?? branch?.BranchName ?? branch?.CompanyName ?? "Filiale";
        // Datei-untaugliche Zeichen säubern
        var safe = new string(b.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return $"Absenz-Auswertung-{year}-{safe}.{ext}";
    }

    // ── Daten-Builder (dieselbe Logik wie die JSON-Endpoints) ─────────────────

    private async Task<(int Year, DateOnly From, DateOnly To, List<ReportRow> Rows, CompanyProfile? Branch)>
        BuildBranchDataAsync(int companyProfileId, int? year)
    {
        var refYear = year ?? DateTime.Today.Year;
        var from = new DateOnly(refYear, 1,  1);
        var to   = new DateOnly(refYear, 12, 31);

        var branch = await _db.CompanyProfiles.FirstOrDefaultAsync(b => b.Id == companyProfileId);
        var employees = await _db.Employees
            .Include(e => e.Employments)
            .Where(e => e.Employments.Any(emp => emp.CompanyProfileId == companyProfileId))
            .ToListAsync();
        var empIds = employees.Select(e => e.Id).ToList();
        if (empIds.Count == 0) return (refYear, from, to, new List<ReportRow>(), branch);

        var absences = await _db.Absences
            .Where(a => empIds.Contains(a.EmployeeId)
                     && TrackedTypes.Contains(a.AbsenceType)
                     && a.DateFrom <= to && a.DateTo >= from)
            .OrderBy(a => a.DateFrom)
            .ToListAsync();
        var byEmp = absences.GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<ReportRow>();
        foreach (var e in employees)
        {
            if (!byEmp.TryGetValue(e.Id, out var list) || list.Count == 0) continue;
            int kT = 0, kF = 0, uT = 0, uF = 0, mT = 0, mF = 0;
            var details = new List<DetailRow>();
            foreach (var a in list)
            {
                var df = a.DateFrom > from ? a.DateFrom : from;
                var dt = a.DateTo   < to   ? a.DateTo   : to;
                int days = dt.DayNumber - df.DayNumber + 1; if (days < 0) days = 0;
                switch (a.AbsenceType) {
                    case "KRANK":      kT += days; kF++; break;
                    case "UNFALL":     uT += days; uF++; break;
                    case "MUTT_VATER": mT += days; mF++; break;
                }
                details.Add(new DetailRow(a.Id, a.AbsenceType,
                    a.DateFrom.ToString("yyyy-MM-dd"), a.DateTo.ToString("yyyy-MM-dd"),
                    days, a.Prozent, a.Notes));
            }
            var fEmp = e.Employments
                .Where(emp => emp.CompanyProfileId == companyProfileId)
                .OrderByDescending(emp => emp.IsActive)
                .ThenByDescending(emp => emp.ContractStartDate)
                .FirstOrDefault();
            rows.Add(new ReportRow(e.Id, e.EmployeeNumber, e.FirstName ?? "", e.LastName ?? "",
                fEmp?.EmploymentModel, e.IsActive,
                kF, kT, uF, uT, mF, mT,
                kF + uF + mF, kT + uT + mT, details));
        }
        rows = rows.OrderByDescending(r => r.TotalTage)
                   .ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase)
                   .ThenBy(r => r.LastName,  StringComparer.OrdinalIgnoreCase).ToList();
        return (refYear, from, to, rows, branch);
    }

    private async Task<(int Year, DateOnly From, DateOnly To, List<CrossBranchRow> Rows)>
        BuildCrossBranchDataAsync(int? year)
    {
        var refYear = year ?? DateTime.Today.Year;
        var from = new DateOnly(refYear, 1,  1);
        var to   = new DateOnly(refYear, 12, 31);

        var absences = await _db.Absences
            .Where(a => TrackedTypes.Contains(a.AbsenceType)
                     && a.DateFrom <= to && a.DateTo >= from)
            .OrderBy(a => a.DateFrom).ToListAsync();
        if (absences.Count == 0) return (refYear, from, to, new List<CrossBranchRow>());

        var empIds = absences.Select(a => a.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees.Include(e => e.Employments)
            .Where(e => empIds.Contains(e.Id)).ToListAsync();
        var branchIds = employees.SelectMany(e => e.Employments)
            .Where(emp => emp.CompanyProfileId.HasValue)
            .Select(emp => emp.CompanyProfileId!.Value).Distinct().ToList();
        var branchMap = await _db.CompanyProfiles.Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id);
        var byEmp = absences.GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<CrossBranchRow>();
        foreach (var e in employees)
        {
            if (!byEmp.TryGetValue(e.Id, out var list) || list.Count == 0) continue;
            int kT = 0, kF = 0, uT = 0, uF = 0, mT = 0, mF = 0;
            var details = new List<DetailRow>();
            foreach (var a in list)
            {
                var df = a.DateFrom > from ? a.DateFrom : from;
                var dt = a.DateTo   < to   ? a.DateTo   : to;
                int days = dt.DayNumber - df.DayNumber + 1; if (days < 0) days = 0;
                switch (a.AbsenceType) {
                    case "KRANK":      kT += days; kF++; break;
                    case "UNFALL":     uT += days; uF++; break;
                    case "MUTT_VATER": mT += days; mF++; break;
                }
                details.Add(new DetailRow(a.Id, a.AbsenceType,
                    a.DateFrom.ToString("yyyy-MM-dd"), a.DateTo.ToString("yyyy-MM-dd"),
                    days, a.Prozent, a.Notes));
            }
            var primaryEmp = e.Employments.Where(emp => emp.CompanyProfileId.HasValue)
                .OrderByDescending(emp => emp.IsActive)
                .ThenByDescending(emp => emp.ContractStartDate).FirstOrDefault();
            CompanyProfile? branch = null;
            if (primaryEmp?.CompanyProfileId is int cpid) branchMap.TryGetValue(cpid, out branch);
            rows.Add(new CrossBranchRow(e.Id, e.EmployeeNumber, e.FirstName ?? "", e.LastName ?? "",
                primaryEmp?.EmploymentModel, e.IsActive,
                branch?.Id, branch?.BranchName ?? branch?.CompanyName, branch?.RestaurantCode,
                kF, kT, uF, uT, mF, mT,
                kF + uF + mF, kT + uT + mT, details));
        }
        rows = rows.OrderByDescending(r => r.TotalTage)
                   .ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase)
                   .ThenBy(r => r.LastName,  StringComparer.OrdinalIgnoreCase).ToList();
        return (refYear, from, to, rows);
    }

    // ── XLSX-Builder (NPOI) ───────────────────────────────────────────────────

    private static byte[] BuildBranchXlsx(int year, List<ReportRow> rows, CompanyProfile? branch)
    {
        var wb = new XSSFWorkbook();
        var sheet = wb.CreateSheet($"Absenz-Auswertung {year}");

        var bold = wb.CreateFont(); bold.IsBold = true;
        var hdrStyle = wb.CreateCellStyle(); hdrStyle.SetFont(bold);
        hdrStyle.FillForegroundColor = IndexedColors.Grey25Percent.Index;
        hdrStyle.FillPattern = FillPattern.SolidForeground;

        var titleRow = sheet.CreateRow(0);
        var titleCell = titleRow.CreateCell(0);
        var titleText = $"Absenz-Auswertung {year}"
            + (branch != null
                ? $" — {(string.IsNullOrEmpty(branch.RestaurantCode) ? "" : "#" + branch.RestaurantCode + " ")}{branch.BranchName ?? branch.CompanyName}"
                : "");
        titleCell.SetCellValue(titleText);
        titleCell.CellStyle = hdrStyle;

        var headers = new[] { "Vorname", "Nachname", "Personal-Nr", "Modell",
                              "Krank Tage", "Krank Fälle",
                              "Unfall Tage", "Unfall Fälle",
                              "Mutter/Vater Tage", "Mutter/Vater Fälle",
                              "Total Tage", "Total Fälle", "Status" };
        var hr = sheet.CreateRow(2);
        for (int i = 0; i < headers.Length; i++)
        {
            var c = hr.CreateCell(i);
            c.SetCellValue(headers[i]);
            c.CellStyle = hdrStyle;
        }
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var row = sheet.CreateRow(3 + i);
            row.CreateCell(0).SetCellValue(r.FirstName);
            row.CreateCell(1).SetCellValue(r.LastName);
            row.CreateCell(2).SetCellValue(r.EmployeeNumber ?? "");
            row.CreateCell(3).SetCellValue(r.EmploymentModel ?? "");
            row.CreateCell(4).SetCellValue(r.KrankTage);
            row.CreateCell(5).SetCellValue(r.KrankFaelle);
            row.CreateCell(6).SetCellValue(r.UnfallTage);
            row.CreateCell(7).SetCellValue(r.UnfallFaelle);
            row.CreateCell(8).SetCellValue(r.MuttVaterTage);
            row.CreateCell(9).SetCellValue(r.MuttVaterFaelle);
            row.CreateCell(10).SetCellValue(r.TotalTage);
            row.CreateCell(11).SetCellValue(r.TotalFaelle);
            row.CreateCell(12).SetCellValue(r.IsActive ? "aktiv" : "inaktiv");
        }
        for (int i = 0; i < headers.Length; i++) sheet.AutoSizeColumn(i);

        using var ms = new MemoryStream();
        wb.Write(ms);
        return ms.ToArray();
    }

    private static byte[] BuildCrossBranchXlsx(int year, List<CrossBranchRow> rows)
    {
        var wb = new XSSFWorkbook();
        var sheet = wb.CreateSheet($"Absenz-Auswertung {year}");

        var bold = wb.CreateFont(); bold.IsBold = true;
        var hdrStyle = wb.CreateCellStyle(); hdrStyle.SetFont(bold);
        hdrStyle.FillForegroundColor = IndexedColors.Grey25Percent.Index;
        hdrStyle.FillPattern = FillPattern.SolidForeground;

        var titleRow = sheet.CreateRow(0);
        var titleCell = titleRow.CreateCell(0);
        titleCell.SetCellValue($"Absenz-Auswertung {year} — Alle Filialen (Top-Liste)");
        titleCell.CellStyle = hdrStyle;

        var headers = new[] { "Rang", "Vorname", "Nachname", "Personal-Nr",
                              "Restaurant-Code", "Filiale", "Modell",
                              "Krank Tage", "Krank Fälle",
                              "Unfall Tage", "Unfall Fälle",
                              "Mutter/Vater Tage", "Mutter/Vater Fälle",
                              "Total Tage", "Total Fälle", "Status" };
        var hr = sheet.CreateRow(2);
        for (int i = 0; i < headers.Length; i++)
        {
            var c = hr.CreateCell(i);
            c.SetCellValue(headers[i]);
            c.CellStyle = hdrStyle;
        }
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var row = sheet.CreateRow(3 + i);
            row.CreateCell(0).SetCellValue(i + 1);
            row.CreateCell(1).SetCellValue(r.FirstName);
            row.CreateCell(2).SetCellValue(r.LastName);
            row.CreateCell(3).SetCellValue(r.EmployeeNumber ?? "");
            row.CreateCell(4).SetCellValue(r.RestaurantCode ?? "");
            row.CreateCell(5).SetCellValue(r.BranchName ?? "");
            row.CreateCell(6).SetCellValue(r.EmploymentModel ?? "");
            row.CreateCell(7).SetCellValue(r.KrankTage);
            row.CreateCell(8).SetCellValue(r.KrankFaelle);
            row.CreateCell(9).SetCellValue(r.UnfallTage);
            row.CreateCell(10).SetCellValue(r.UnfallFaelle);
            row.CreateCell(11).SetCellValue(r.MuttVaterTage);
            row.CreateCell(12).SetCellValue(r.MuttVaterFaelle);
            row.CreateCell(13).SetCellValue(r.TotalTage);
            row.CreateCell(14).SetCellValue(r.TotalFaelle);
            row.CreateCell(15).SetCellValue(r.IsActive ? "aktiv" : "inaktiv");
        }
        for (int i = 0; i < headers.Length; i++) sheet.AutoSizeColumn(i);

        using var ms = new MemoryStream();
        wb.Write(ms);
        return ms.ToArray();
    }

    // ── PDF-Builder (QuestPDF) ────────────────────────────────────────────────

    private static byte[] BuildBranchPdf(int year, List<ReportRow> rows, CompanyProfile? branch)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var subtitle = branch != null
            ? $"{(string.IsNullOrEmpty(branch.RestaurantCode) ? "" : "#" + branch.RestaurantCode + " · ")}{branch.BranchName ?? branch.CompanyName}"
            : "Filiale";

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(25);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text($"Absenz-Auswertung {year}").FontSize(18).SemiBold();
                    col.Item().Text(subtitle).FontSize(11).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(2).Text($"Krankheit · Unfall · Mutter-/Vaterschaft — sortiert nach Total-Tagen")
                              .FontSize(9).FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingTop(15).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);   // Name
                        c.RelativeColumn(2);   // Pers-Nr
                        c.RelativeColumn(1);   // Modell
                        c.RelativeColumn(2);   // Krank
                        c.RelativeColumn(2);   // Unfall
                        c.RelativeColumn(2);   // MV
                        c.RelativeColumn(1);   // Total
                    });

                    table.Header(header =>
                    {
                        static IContainer H(IContainer c) => c
                            .DefaultTextStyle(x => x.SemiBold().FontSize(9))
                            .Background(Colors.Grey.Lighten2).PaddingVertical(5).PaddingHorizontal(4);
                        header.Cell().Element(H).Text("Mitarbeiter:in");
                        header.Cell().Element(H).Text("Personal-Nr");
                        header.Cell().Element(H).Text("Modell");
                        header.Cell().Element(H).AlignRight().Text("Krank (T/F)");
                        header.Cell().Element(H).AlignRight().Text("Unfall (T/F)");
                        header.Cell().Element(H).AlignRight().Text("Mutter/Vater (T/F)");
                        header.Cell().Element(H).AlignRight().Text("Total T");
                    });

                    foreach (var r in rows)
                    {
                        static IContainer D(IContainer c) => c
                            .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .PaddingVertical(4).PaddingHorizontal(4);
                        table.Cell().Element(D).Text($"{r.FirstName} {r.LastName}");
                        table.Cell().Element(D).Text(r.EmployeeNumber ?? "");
                        table.Cell().Element(D).Text(r.EmploymentModel ?? "");
                        table.Cell().Element(D).AlignRight().Text(r.KrankFaelle    > 0 ? $"{r.KrankTage} ({r.KrankFaelle})"       : "–");
                        table.Cell().Element(D).AlignRight().Text(r.UnfallFaelle   > 0 ? $"{r.UnfallTage} ({r.UnfallFaelle})"     : "–");
                        table.Cell().Element(D).AlignRight().Text(r.MuttVaterFaelle> 0 ? $"{r.MuttVaterTage} ({r.MuttVaterFaelle})": "–");
                        table.Cell().Element(D).AlignRight().Text(r.TotalTage.ToString()).SemiBold();
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                    text.Span("Seite "); text.CurrentPageNumber(); text.Span(" / "); text.TotalPages();
                    text.Span($"   ·   Generiert {DateTime.Now:dd.MM.yyyy HH:mm}");
                });
            });
        });
        return doc.GeneratePdf();
    }

    private static byte[] BuildCrossBranchPdf(int year, List<CrossBranchRow> rows)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(25);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text($"Absenz-Auswertung {year} — Top-Liste alle Filialen").FontSize(17).SemiBold();
                    col.Item().Text("Krankheit · Unfall · Mutter-/Vaterschaft — sortiert nach Total-Tagen")
                              .FontSize(9).FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingTop(15).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(28);  // Rang
                        c.RelativeColumn(3);   // Name
                        c.RelativeColumn(1.6f);// Pers-Nr
                        c.RelativeColumn(2);   // Filiale
                        c.RelativeColumn(0.9f);// Modell
                        c.RelativeColumn(1.6f);// Krank
                        c.RelativeColumn(1.6f);// Unfall
                        c.RelativeColumn(1.7f);// MV
                        c.RelativeColumn(0.9f);// Total
                    });

                    table.Header(header =>
                    {
                        static IContainer H(IContainer c) => c
                            .DefaultTextStyle(x => x.SemiBold().FontSize(8.5f))
                            .Background(Colors.Grey.Lighten2).PaddingVertical(5).PaddingHorizontal(3);
                        header.Cell().Element(H).AlignCenter().Text("#");
                        header.Cell().Element(H).Text("Mitarbeiter:in");
                        header.Cell().Element(H).Text("Personal-Nr");
                        header.Cell().Element(H).Text("Filiale");
                        header.Cell().Element(H).Text("Modell");
                        header.Cell().Element(H).AlignRight().Text("Krank (T/F)");
                        header.Cell().Element(H).AlignRight().Text("Unfall (T/F)");
                        header.Cell().Element(H).AlignRight().Text("M./V. (T/F)");
                        header.Cell().Element(H).AlignRight().Text("Total");
                    });

                    int rank = 0;
                    foreach (var r in rows)
                    {
                        rank++;
                        static IContainer D(IContainer c) => c
                            .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .PaddingVertical(3).PaddingHorizontal(3);
                        var branchLabel = string.IsNullOrEmpty(r.RestaurantCode)
                            ? (r.BranchName ?? "")
                            : $"#{r.RestaurantCode} {r.BranchName ?? ""}".Trim();
                        table.Cell().Element(D).AlignCenter().Text(rank.ToString()).SemiBold();
                        table.Cell().Element(D).Text($"{r.FirstName} {r.LastName}");
                        table.Cell().Element(D).Text(r.EmployeeNumber ?? "");
                        table.Cell().Element(D).Text(branchLabel);
                        table.Cell().Element(D).Text(r.EmploymentModel ?? "");
                        table.Cell().Element(D).AlignRight().Text(r.KrankFaelle    > 0 ? $"{r.KrankTage} ({r.KrankFaelle})"       : "–");
                        table.Cell().Element(D).AlignRight().Text(r.UnfallFaelle   > 0 ? $"{r.UnfallTage} ({r.UnfallFaelle})"     : "–");
                        table.Cell().Element(D).AlignRight().Text(r.MuttVaterFaelle> 0 ? $"{r.MuttVaterTage} ({r.MuttVaterFaelle})": "–");
                        table.Cell().Element(D).AlignRight().Text(r.TotalTage.ToString()).SemiBold();
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                    text.Span("Seite "); text.CurrentPageNumber(); text.Span(" / "); text.TotalPages();
                    text.Span($"   ·   Generiert {DateTime.Now:dd.MM.yyyy HH:mm}");
                });
            });
        });
        return doc.GeneratePdf();
    }
}

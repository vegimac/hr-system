using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Fluktuation / Ein- &amp; Austritte über ALLE Filialen (Walter 26.07.2026).
/// Zeitraum frei wählbar. Austrittsgründe als Donut + namentliche Listen.
/// GET /api/reports/fluktuation?from=&amp;to= — rein lesend, kein LohnEditLock.
/// </summary>
[ApiController]
[Route("api/reports/fluktuation")]
[Authorize(Roles = "admin,superuser")]
public class FluktuationReportController : ControllerBase
{
    private readonly AppDbContext _db;
    public FluktuationReportController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? from, [FromQuery] string? to)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var fromD = ParseDate(from) ?? new DateOnly(today.Year, 1, 1);
        var toD = ParseDate(to) ?? today;
        if (toD < fromD) (fromD, toD) = (toD, fromD);

        var branchesRaw = await _db.CompanyProfiles.AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => new { b.Id, b.RestaurantCode, b.City, b.BranchName, b.CompanyName })
            .ToListAsync();
        var branchName = branchesRaw.ToDictionary(
            b => b.Id,
            b => $"{b.RestaurantCode} {(!string.IsNullOrWhiteSpace(b.City) ? b.City : (b.BranchName ?? b.CompanyName))}".Trim());

        // Pool: alle MA ausser Phantom — Eintritt/Austritt steuern die Periode.
        var emps = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsPayrollExcluded)
            .Select(e => new
            {
                e.Id,
                e.FirstName,
                e.LastName,
                e.EntryDate,
                e.ExitDate,
                e.KuendigungPer,
                e.KuendigungDurch,
                e.Austrittsgrund,
            })
            .ToListAsync();

        // Hauptfiliale = Filiale des ältesten Vertrags (wie Altersreport) — in Memory,
        // damit auch ausgetretene MA mit abgelaufenem Vertrag eine Filiale haben.
        var empBranchRaw = await _db.Employments.AsNoTracking()
            .Where(e => e.CompanyProfileId != null)
            .Select(e => new { e.EmployeeId, BranchId = e.CompanyProfileId!.Value, e.ContractStartDate })
            .ToListAsync();
        var branchByEmp = empBranchRaw
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.ContractStartDate).First().BranchId);

        static DateOnly? AsDate(DateTime? dt) =>
            dt.HasValue ? DateOnly.FromDateTime(dt.Value) : null;

        bool ActiveOn(DateOnly day, DateOnly? entry, DateOnly? exit) =>
            entry.HasValue && entry.Value <= day
            && (!exit.HasValue || exit.Value >= day);

        var dayBefore = fromD.AddDays(-1);
        var bestandAnfang = emps.Count(e => ActiveOn(dayBefore, AsDate(e.EntryDate), AsDate(e.ExitDate)));
        var bestandEnde = emps.Count(e => ActiveOn(toD, AsDate(e.EntryDate), AsDate(e.ExitDate)));

        var eintritte = emps
            .Where(e =>
            {
                var ed = AsDate(e.EntryDate);
                return ed.HasValue && ed.Value >= fromD && ed.Value <= toD;
            })
            .Select(e => new
            {
                id = e.Id,
                firstName = e.FirstName ?? "",
                lastName = e.LastName ?? "",
                name = $"{e.FirstName} {e.LastName}".Trim(),
                entryDate = AsDate(e.EntryDate)!.Value.ToString("yyyy-MM-dd"),
                branchId = branchByEmp.GetValueOrDefault(e.Id),
                branchName = branchByEmp.TryGetValue(e.Id, out var bid) && branchName.TryGetValue(bid, out var bn) ? bn : "—",
            })
            .OrderBy(x => x.firstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.lastName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var austritte = emps
            .Where(e =>
            {
                var xd = AsDate(e.ExitDate);
                return xd.HasValue && xd.Value >= fromD && xd.Value <= toD;
            })
            .Select(e =>
            {
                var entry = AsDate(e.EntryDate);
                var exit = AsDate(e.ExitDate)!.Value;
                int? monate = null;
                if (entry.HasValue && exit >= entry.Value)
                {
                    monate = (exit.Year - entry.Value.Year) * 12 + (exit.Month - entry.Value.Month);
                    if (exit.Day < entry.Value.Day) monate--;
                    if (monate < 0) monate = 0;
                }
                var durch = (e.KuendigungDurch ?? "").Trim().ToUpperInvariant();
                var durchLbl = durch == "AG" ? "durch uns"
                    : durch == "AN" ? "durch Mitarbeiter" : "—";
                var grundCode = string.IsNullOrWhiteSpace(e.Austrittsgrund)
                    ? null
                    : e.Austrittsgrund.Trim().ToUpperInvariant();
                return new
                {
                    id = e.Id,
                    firstName = e.FirstName ?? "",
                    lastName = e.LastName ?? "",
                    name = $"{e.FirstName} {e.LastName}".Trim(),
                    entryDate = entry?.ToString("yyyy-MM-dd"),
                    exitDate = exit.ToString("yyyy-MM-dd"),
                    kuendigungPer = AsDate(e.KuendigungPer)?.ToString("yyyy-MM-dd"),
                    kuendigungDurch = durchLbl,
                    austrittsgrundCode = grundCode,
                    austrittsgrund = grundCode == null ? "ohne Angabe" : AustrittsgrundCodes.LabelOf(grundCode),
                    verbleibMonate = monate,
                    branchId = branchByEmp.GetValueOrDefault(e.Id),
                    branchName = branchByEmp.TryGetValue(e.Id, out var bid) && branchName.TryGetValue(bid, out var bn) ? bn : "—",
                };
            })
            .OrderByDescending(x => x.exitDate)
            .ThenBy(x => x.firstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.lastName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Donut: nur Codes mit Label; «ohne Angabe» separat.
        var grundCounts = austritte
            .GroupBy(a => a.austrittsgrundCode ?? "")
            .Select(g => new
            {
                code = string.IsNullOrEmpty(g.Key) ? null : g.Key,
                label = string.IsNullOrEmpty(g.Key) ? "ohne Angabe" : AustrittsgrundCodes.LabelOf(g.Key),
                count = g.Count(),
            })
            .OrderByDescending(x => x.count)
            .ThenBy(x => x.label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var avgBestand = (bestandAnfang + bestandEnde) / 2.0;
        var rate = avgBestand > 0
            ? Math.Round(austritte.Count / avgBestand * 100.0, 1)
            : 0.0;

        var verbleibVals = austritte
            .Where(a => a.verbleibMonate.HasValue)
            .Select(a => (double)a.verbleibMonate!.Value)
            .ToList();
        double? avgVerbleib = verbleibVals.Count > 0
            ? Math.Round(verbleibVals.Average(), 1)
            : null;

        return Ok(new
        {
            from = fromD.ToString("yyyy-MM-dd"),
            to = toD.ToString("yyyy-MM-dd"),
            bestandAnfang,
            bestandEnde,
            eintritteCount = eintritte.Count,
            austritteCount = austritte.Count,
            fluktuationsratePct = rate,
            fluktuationsFormel = "Austritte ÷ Mittelwert(Bestand Anfang, Bestand Ende) × 100",
            avgVerbleibMonate = avgVerbleib,
            gruende = grundCounts,
            eintritte,
            austritte,
        });
    }

    private static DateOnly? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateOnly.TryParse(s.Trim(), out var d) ? d : null;
    }
}

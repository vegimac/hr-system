using HrSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Ø-Wochenstunden-Report (Walter-Vorgabe 25.08.2026): pro MA die
/// durchschnittlich gestempelten Stunden pro Woche in einem frei wählbaren
/// Zeitraum. Basis = Stempelzeiten (easy@work-Sync), Tag + Nacht absolut
/// (gleiche absH-Logik wie der Stempelzeiten-Tab). Der Schnitt wird pro MA
/// auf seinen EFFEKTIVEN Zeitraum bezogen (Eintritt/Austritt beschneiden
/// die Periode — ein Mitte-Zeitraum-Eintretender wird nicht verwässert).
/// GET /api/reports/wochenstunden?from=&amp;to=&amp;companyProfileId= — rein lesend.
/// </summary>
[ApiController]
[Route("api/reports/wochenstunden")]
[Authorize(Roles = "admin,superuser")]
public class WochenstundenReportController : ControllerBase
{
    private readonly AppDbContext _db;
    public WochenstundenReportController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int? companyProfileId = null)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var toD   = ParseDate(to) ?? today;
        var fromD = ParseDate(from) ?? toD.AddMonths(-3);
        if (toD < fromD) (fromD, toD) = (toD, fromD);
        var filterBranchId = companyProfileId is > 0 ? companyProfileId : null;

        var fromDt = fromD.ToDateTime(TimeOnly.MinValue);
        var toDt   = toD.ToDateTime(TimeOnly.MinValue);

        // Verträge, die den Zeitraum überlappen (optional auf Filiale gefiltert).
        var contracts = await _db.Employments.AsNoTracking()
            .Where(em => em.ContractStartDate <= toDt
                      && (em.ContractEndDate == null || em.ContractEndDate >= fromDt)
                      && (filterBranchId == null || em.CompanyProfileId == filterBranchId))
            .Select(em => new { em.EmployeeId, em.CompanyProfileId, em.ContractStartDate,
                                em.ContractEndDate, em.EmploymentModel,
                                em.WeeklyHours, em.GuaranteedHoursPerWeek })
            .ToListAsync();
        var empIds = contracts.Select(c => c.EmployeeId).Distinct().ToList();

        // Nur aktive MA — gleiche Regeln wie die Notfallkontakte-Liste
        // (Walter 25.08.2026): erfasste Kündigung/Aufhebung → raus; gesetzter
        // Austritt → raus, AUSSER er liegt ~6 Monate nach Eintritt (±30 Tage
        // = typische Befristung); Zweifelsfall → drin.
        var empsRaw = await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id) && !e.IsPayrollExcluded && !e.IsHidden
                     && e.IsActive
                     && e.KuendigungPer == null
                     && e.KuendigungAusgesprochenAm == null
                     && (e.Austrittsgrund == null || e.Austrittsgrund == ""))
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
                               e.EntryDate, e.ExitDate })
            .ToListAsync();
        var emps = empsRaw.Where(e =>
        {
            if (e.ExitDate == null) return true;                 // kein Austritt
            if (e.EntryDate == null) return true;                // Zweifelsfall → drin
            var sechsMonate = e.EntryDate.Value.AddMonths(6);
            return Math.Abs((e.ExitDate.Value - sechsMonate).TotalDays) <= 30; // Befristung
        }).ToList();

        // Stempelzeiten im Zeitraum, pro MA aufsummiert (Tag + Nacht absolut —
        // gleiche Logik wie absH im Stempelzeiten-Tab: totalHours war in
        // Alt-Daten oft nur der Tag-Anteil).
        var ids = emps.Select(e => e.Id).ToList();
        var punches = await _db.EmployeeTimeEntries.AsNoTracking()
            .Where(t => ids.Contains(t.EmployeeId)
                     && t.EntryDate >= fromD && t.EntryDate <= toD)
            .Select(t => new { t.EmployeeId, t.DurationHours, t.NightHours, t.TotalHours })
            .ToListAsync();
        static decimal AbsH(decimal? dh, decimal? nh, decimal? th)
        {
            var d = dh ?? 0m; var n = nh ?? 0m; var t = th ?? 0m;
            if (d > 0 || n > 0)
            {
                var parts = d + n;
                if (t >= parts - 0.05m) return t;
                if (n > 0 && Math.Abs(t - d) <= 0.05m) return parts;
                return Math.Max(t, parts);
            }
            return t;
        }
        var stdByEmp = punches
            .GroupBy(p => p.EmployeeId)
            .ToDictionary(g => g.Key,
                          g => g.Sum(p => AbsH(p.DurationHours, p.NightHours, p.TotalHours)));

        var contractsByEmp = contracts.ToLookup(c => c.EmployeeId);
        var rows = emps
            .Select(e =>
            {
                // Effektiver Zeitraum des MA: durch Eintritt/Austritt beschnitten.
                var eff0 = fromD;
                var eff1 = toD;
                if (e.EntryDate.HasValue)
                {
                    var d0 = DateOnly.FromDateTime(e.EntryDate.Value);
                    if (d0 > eff0) eff0 = d0;
                }
                if (e.ExitDate.HasValue)
                {
                    var d1 = DateOnly.FromDateTime(e.ExitDate.Value);
                    if (d1 < eff1) eff1 = d1;
                }
                if (eff1 < eff0) { eff0 = fromD; eff1 = toD; } // Datenfehler → voller Zeitraum
                var tage = eff1.DayNumber - eff0.DayNumber + 1;
                var wochen = Math.Max(tage / 7.0, 1.0 / 7.0);

                // Jüngster überlappender Vertrag → Modell + vertragliche h/Wo.
                var c = contractsByEmp[e.Id]
                    .OrderByDescending(x => x.ContractStartDate)
                    .FirstOrDefault();
                var total = stdByEmp.GetValueOrDefault(e.Id, 0m);

                return new
                {
                    empNr    = e.EmployeeNumber,
                    vorname  = e.FirstName,
                    name     = e.LastName,
                    modell   = c?.EmploymentModel,
                    vertragH = c?.GuaranteedHoursPerWeek ?? c?.WeeklyHours,
                    totalH   = Math.Round(total, 2),
                    wochen   = Math.Round((decimal)wochen, 1),
                    avgH     = Math.Round(total / (decimal)wochen, 2)
                };
            })
            // MA ganz ohne Stempel im Zeitraum weglassen? NEIN — 0.00 ist
            // eine relevante Aussage (z.B. FLEX ohne Einsätze). Bleiben drin.
            .OrderBy(r => r.vorname ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.name ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(new
        {
            from = fromD.ToString("yyyy-MM-dd"),
            to = toD.ToString("yyyy-MM-dd"),
            anzahlMa = rows.Count,
            summeStunden = Math.Round(rows.Sum(r => r.totalH), 2),
            rows
        });
    }

    private static DateOnly? ParseDate(string? s)
        => DateOnly.TryParse(s, out var d) ? d : null;
}

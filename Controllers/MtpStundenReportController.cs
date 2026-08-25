using System.Text.Json;
using System.Text.Json.Nodes;
using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// MTP-Stunden-Kontrolle (Walter-Vorgabe 25.08.2026): pro MTP-MA eine
/// Wochenspalte (ISO Mo–So) mit gestempelten Stunden PLUS angerechneten
/// Absenz-Stunden, letzte Spalte Ø — Vergleich gegen die garantierten
/// Wochenstunden (keine Toleranz). Sortierung: grösstes Minus zuoberst,
/// grösstes Plus zuunterst (Walter 25.08.2026 v2).
/// Absenz-Anrechnung spiegelt die MTP-Lohnlogik (PayrollCalculationEngine):
///   Ferien/unbez. Urlaub = 1/7-Kalender · Krank/Unfall = 1/5 pro geplantem
///   Tag (worked_days, Fallback Mo–Fr, × Prozent) · EO/Militär/Zivilschutz =
///   Divisor aus dem Absenz-Typ-Katalog (ZaehlweiseMtp KALENDER→7, sonst 5).
/// Nur VOLLE Wochen bis heute; Wochen vor Eintritt/nach Austritt = «–»
/// (zählen nicht in den Ø). GET-only, rein lesend; /pdf = A4-quer-Ausdruck.
/// </summary>
[ApiController]
[Route("api/reports/mtp-stunden")]
[Authorize(Roles = "admin,superuser")]
public class MtpStundenReportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly MtpStundenPdfService _pdf;
    private readonly PayrollCalculationEngine _calcEngine;
    public MtpStundenReportController(AppDbContext db, MtpStundenPdfService pdf,
        PayrollCalculationEngine calcEngine)
    {
        _db = db;
        _pdf = pdf;
        _calcEngine = calcEngine;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int companyProfileId)
    {
        if (companyProfileId <= 0)
            return BadRequest(new { error = "FILIALE_FEHLT", message = "Bitte eine Filiale wählen." });

        var d = await BuildAsync(from, to, companyProfileId);
        return Ok(new
        {
            from = d.From.ToString("yyyy-MM-dd"),
            to = d.To.ToString("yyyy-MM-dd"),
            weeks = d.Wochen.Select(mo => new
            {
                monday = mo.ToString("yyyy-MM-dd"),
                kw = System.Globalization.ISOWeek.GetWeekOfYear(mo.ToDateTime(TimeOnly.MinValue))
            }),
            rows = d.Rows.Select(r => new
            {
                vorname = r.Vorname,
                name = r.Name,
                schwanger = r.Schwanger,
                mutterschutz = r.Mutterschutz,
                garantiertH = r.GarantiertH,
                weeks = r.Weeks.Select(w => w == null ? null : new
                {
                    total = w.Total, gearbeitet = w.Gearbeitet, absenz = w.Absenz
                }),
                avg = r.Avg,
                saldoAktuell = r.SaldoAktuell
            })
        });
    }

    /// <summary>GET /api/reports/mtp-stunden/pdf — A4 quer, gleiche Daten.</summary>
    [HttpGet("pdf")]
    public async Task<IActionResult> GetPdf(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int companyProfileId)
    {
        if (companyProfileId <= 0)
            return BadRequest(new { error = "FILIALE_FEHLT", message = "Bitte eine Filiale wählen." });

        var cp = await _db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == companyProfileId);
        if (cp is null)
            return NotFound(new { error = "FILIALE_NICHT_GEFUNDEN", message = "Filiale nicht gefunden." });
        var titel = string.Join(" ", new[] { cp.RestaurantCode, cp.City }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (string.IsNullOrWhiteSpace(titel)) titel = cp.BranchName ?? "";

        var d = await BuildAsync(from, to, companyProfileId);
        byte[] bytes;
        try
        {
            bytes = _pdf.Generate(d, titel);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "MTP_STUNDEN_PDF_FEHLER",
                message = "PDF konnte nicht erzeugt werden.",
                detail = ex.GetBaseException().Message
            });
        }
        return File(bytes, "application/pdf", "MTP-Stunden-Kontrolle.pdf");
    }

    // ── gemeinsame Daten-Berechnung für JSON + PDF ──────────────────────────
    private async Task<MtpStundenData> BuildAsync(string? from, string? to, int companyProfileId)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var toD   = ParseDate(to) ?? today;
        var fromD = ParseDate(from) ?? toD.AddMonths(-2);
        if (toD < fromD) (fromD, toD) = (toD, fromD);

        // Volle ISO-Wochen: Start = Montag der Von-Woche; eine Woche zählt nur,
        // wenn ihr Sonntag ≤ min(Bis, heute) liegt (keine angebrochenen Wochen).
        var start = fromD.AddDays(-(((int)fromD.DayOfWeek + 6) % 7)); // Montag
        var endCap = toD < today ? toD : today;
        var wochen = new List<DateOnly>();
        for (var mo = start; mo.AddDays(6) <= endCap; mo = mo.AddDays(7))
            wochen.Add(mo);
        if (wochen.Count == 0)
            return new MtpStundenData(fromD, toD, wochen, new List<MtpRow>());
        var w0 = wochen[0];
        var w1 = wochen[^1].AddDays(6);
        var w0Dt = w0.ToDateTime(TimeOnly.MinValue);
        var w1Dt = w1.ToDateTime(TimeOnly.MinValue);

        // MTP-Verträge der Filiale, die den Wochenbereich überlappen.
        var contracts = await _db.Employments.AsNoTracking()
            .Where(em => em.CompanyProfileId == companyProfileId
                      && em.EmploymentModel == "MTP"
                      && em.ContractStartDate <= w1Dt
                      && (em.ContractEndDate == null || em.ContractEndDate >= w0Dt))
            .Select(em => new { em.EmployeeId, em.ContractStartDate, em.ContractEndDate,
                                em.GuaranteedHoursPerWeek })
            .ToListAsync();
        var empIds = contracts.Select(c => c.EmployeeId).Distinct().ToList();

        // Aktiv-Filter wie Notfall-Liste (Walter 25.08.2026): Kündigungen raus,
        // Austritte raus ausser 6-Monats-Befristungsmuster (±30 Tage).
        var empsRaw = await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id) && !e.IsPayrollExcluded && !e.IsHidden
                     && e.IsActive
                     && e.KuendigungPer == null
                     && e.KuendigungAusgesprochenAm == null
                     && (e.Austrittsgrund == null || e.Austrittsgrund == ""))
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EntryDate, e.ExitDate })
            .ToListAsync();
        var emps = empsRaw.Where(e =>
        {
            if (e.ExitDate == null) return true;
            if (e.EntryDate == null) return true;
            return Math.Abs((e.ExitDate.Value - e.EntryDate.Value.AddMonths(6)).TotalDays) <= 30;
        }).ToList();
        var ids = emps.Select(e => e.Id).ToList();

        // Stempelzeiten (Tag + Nacht absolut, gleiche absH-Logik wie überall).
        var punches = await _db.EmployeeTimeEntries.AsNoTracking()
            .Where(t => ids.Contains(t.EmployeeId) && t.EntryDate >= w0 && t.EntryDate <= w1)
            .Select(t => new { t.EmployeeId, t.EntryDate, t.DurationHours, t.NightHours, t.TotalHours })
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

        // Absenzen im Bereich + Katalog-Divisoren.
        var absences = await _db.Absences.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId) && a.DateFrom <= w1 && a.DateTo >= w0)
            .Select(a => new { a.EmployeeId, a.AbsenceType, a.DateFrom, a.DateTo,
                               a.WorkedDays, a.Prozent })
            .ToListAsync();
        var typZaehlweise = (await _db.AbsenzTypen.AsNoTracking()
                .Select(t => new { t.Code, t.ZaehlweiseMtp })
                .ToListAsync())
            .GroupBy(t => t.Code).ToDictionary(g => g.Key, g => g.First().ZaehlweiseMtp);

        // Schwangerschaft/Mutterschutz-Badge (Walter 25.08.2026) — gleiche
        // Fenster-Logik wie die MA-Liste (16 Wochen nach Geburt/ET).
        var pregRaw = await _db.EmployeePregnancies.AsNoTracking()
            .Where(p => p.IsActive && ids.Contains(p.EmployeeId))
            .Select(p => new { p.EmployeeId, p.Geburtsdatum, p.ErrechneterTermin })
            .ToListAsync();
        var pregWindow = pregRaw
            .Where(p => (p.Geburtsdatum ?? p.ErrechneterTermin).AddDays(16 * 7) >= today)
            .ToList();
        var maternitySet = pregWindow.Where(p => p.Geburtsdatum != null).Select(p => p.EmployeeId).ToHashSet();
        var pregnantSet  = pregWindow.Where(p => p.Geburtsdatum == null).Select(p => p.EmployeeId)
            .Where(id => !maternitySet.Contains(id)).ToHashSet();

        var punchLookup = punches.ToLookup(p => p.EmployeeId);
        var absLookup = absences.ToLookup(a => a.EmployeeId);
        var contractsByEmp = contracts.ToLookup(c => c.EmployeeId);

        // Stunden-Saldo «aktuell» pro MA (wie MA-Maske) — Engine-Rechnung.
        var saldoAktuell = await BuildSaldoAktuellAsync(ids, companyProfileId);

        var rows = emps.Select(e =>
        {
            var c = contractsByEmp[e.Id].OrderByDescending(x => x.ContractStartDate).First();
            var garantiertH = c.GuaranteedHoursPerWeek ?? 0m;

            // MA-Gültigkeitsfenster: Eintritt bis Austritt — Wochen ausserhalb
            // zeigen «–» und zählen nicht in den Ø.
            var gueltigAb = e.EntryDate.HasValue ? DateOnly.FromDateTime(e.EntryDate.Value) : DateOnly.MinValue;
            var gueltigBis = e.ExitDate.HasValue ? DateOnly.FromDateTime(e.ExitDate.Value) : DateOnly.MaxValue;

            // Krank/Unfall: geplante Tage (worked_days JSON, Fallback Mo–Fr).
            HashSet<DateOnly> GeplantTage(string typ)
            {
                var set = new HashSet<DateOnly>();
                foreach (var ab in absLookup[e.Id].Where(x => x.AbsenceType == typ))
                {
                    List<DateOnly>? days = null;
                    if (!string.IsNullOrWhiteSpace(ab.WorkedDays))
                    {
                        try
                        {
                            days = System.Text.Json.JsonSerializer
                                .Deserialize<string[]>(ab.WorkedDays)!
                                .Select(s => DateOnly.TryParse(s, out var d) ? d : (DateOnly?)null)
                                .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                        }
                        catch { days = null; }
                    }
                    if (days is { Count: > 0 }) { foreach (var d in days) set.Add(d); }
                    else
                    {
                        for (var d = ab.DateFrom; d <= ab.DateTo; d = d.AddDays(1))
                            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                                set.Add(d);
                    }
                }
                return set;
            }
            var krankGeplant  = GeplantTage("KRANK");
            var unfallGeplant = GeplantTage("UNFALL");

            var weekVals = new List<MtpWeekCell?>();
            decimal sum = 0m; int cnt = 0;
            foreach (var mo in wochen)
            {
                var so = mo.AddDays(6);
                // Woche komplett ausserhalb der Anstellung → «–» (nicht in Ø).
                if (so < gueltigAb || mo > gueltigBis) { weekVals.Add(null); continue; }

                decimal gearbeitet = punchLookup[e.Id]
                    .Where(p => p.EntryDate >= mo && p.EntryDate <= so)
                    .Sum(p => AbsH(p.DurationHours, p.NightHours, p.TotalHours));

                decimal absenz = 0m;
                foreach (var a in absLookup[e.Id])
                {
                    var f = a.DateFrom > mo ? a.DateFrom : mo;
                    var t = a.DateTo < so ? a.DateTo : so;
                    if (t < f) continue;
                    switch (a.AbsenceType)
                    {
                        case "FERIEN":
                        case "UNBEZ_URLAUB":
                            absenz += (t.DayNumber - f.DayNumber + 1) * garantiertH / 7m;
                            break;
                        case "KRANK":
                        case "UNFALL":
                        {
                            var geplant = a.AbsenceType == "KRANK" ? krankGeplant : unfallGeplant;
                            int tage = 0;
                            for (var d = f; d <= t; d = d.AddDays(1))
                                if (geplant.Contains(d)) tage++;
                            absenz += tage * garantiertH / 5m * (a.Prozent / 100m);
                            break;
                        }
                        case "MUTT_VATER":
                        case "MUTTERSCHAFT":
                        case "VATERSCHAFT":
                        case "MILITAER":
                        case "ZIVILSCHUTZ":
                        {
                            var divisor = typZaehlweise.GetValueOrDefault(a.AbsenceType) == "KALENDER" ? 7m : 5m;
                            absenz += (t.DayNumber - f.DayNumber + 1) * garantiertH / divisor;
                            break;
                        }
                        // FEIERTAG u.a.: keine Zeitgutschrift bei MTP.
                    }
                }

                var tot = Math.Round(gearbeitet + absenz, 2);
                weekVals.Add(new MtpWeekCell(tot, Math.Round(gearbeitet, 2), Math.Round(absenz, 2)));
                sum += tot; cnt++;
            }

            return new MtpRow(
                e.FirstName, e.LastName,
                pregnantSet.Contains(e.Id), maternitySet.Contains(e.Id),
                garantiertH, weekVals,
                cnt > 0 ? Math.Round(sum / cnt, 2) : null,
                saldoAktuell.TryGetValue(e.Id, out var sa) ? sa : null);
        })
        // Sortierung (Walter 25.08.2026 v2): grösstes MINUS (Ø − Garantie)
        // zuoberst, grösstes Plus zuunterst; MA ohne Ø ganz unten.
        .OrderBy(r => r.Avg == null ? decimal.MaxValue : r.Avg.Value - r.GarantiertH)
        .ThenBy(r => r.Vorname ?? "", StringComparer.OrdinalIgnoreCase)
        .ToList();

        return new MtpStundenData(fromD, toD, wochen, rows);
    }

    /// <summary>
    /// Stunden-Saldo «aktuell» pro MA — SPIEGELT den Stichtag-Block von
    /// PayrollController.SollstundenReport (stSaldo): Vormonats-Saldo +
    /// (gearbeitet bis heute + anteilige Absenz-Gutschrift − anteiliges
    /// reduziertes Soll). Gleiche Engine (CalculateAsync), gleiche Formeln —
    /// bei Änderungen dort BEIDE Stellen nachführen.
    /// </summary>
    private async Task<Dictionary<int, decimal>> BuildSaldoAktuellAsync(List<int> ids, int companyProfileId)
    {
        var result = new Dictionary<int, decimal>();
        if (ids.Count == 0) return result;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var (periodFrom, periodTo) = PayrollCalculations.CalcPeriod(today.Year, today.Month);
        var stich = today;
        if (stich > periodTo) stich = periodTo;
        int daysInMonth    = periodTo.DayNumber - periodFrom.DayNumber + 1;
        int daysToStichtag = stich.DayNumber - periodFrom.DayNumber + 1;
        decimal dayRatio   = daysInMonth > 0 ? (decimal)daysToStichtag / daysInMonth : 1m;

        var workedToStich = (await _db.EmployeeTimeEntries.AsNoTracking()
                .Where(t => ids.Contains(t.EmployeeId)
                         && t.EntryDate >= periodFrom && t.EntryDate <= stich)
                .ToListAsync())
            .GroupBy(t => t.EmployeeId)
            .ToDictionary(g => g.Key, g => TimeEntryHours.SumAbsolute(g));

        var absInPeriod = await _db.Absences.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId)
                     && a.DateFrom <= periodTo && a.DateTo >= periodFrom)
            .ToListAsync();
        var absByEmp = absInPeriod.GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        foreach (var id in ids)
        {
            IActionResult calc;
            try { calc = await _calcEngine.CalculateAsync(id, today.Year, today.Month, companyProfileId); }
            catch { continue; }
            if (calc is not OkObjectResult ok || ok.Value is null) continue;
            var node = JsonNode.Parse(JsonSerializer.Serialize(ok.Value, camel));
            decimal Dv(string key)
            {
                var v = node?[key];
                if (v is null) return 0m;
                try { return v.GetValue<decimal>(); }
                catch { try { return (decimal)v.GetValue<double>(); } catch { return 0m; } }
            }
            decimal sollBrutto  = Dv("sollStundenVoll");     // MTP: Soll vor Absenz-Reduktion
            decimal sollNetto   = Dv("sollStunden");
            decimal reduktion   = Math.Max(0, sollBrutto - sollNetto);
            decimal absGutMonat = Dv("absenzGutschrift");
            decimal vormonat    = Dv("vormonatHourSaldo");

            decimal absFrac = 0m;
            if (absByEmp.TryGetValue(id, out var aList))
            {
                int dFull = aList.Sum(a => PayrollCalculations.CountAbsenceDaysInPeriod(a, periodFrom, periodTo));
                int dUpTo = aList.Sum(a => PayrollCalculations.CountAbsenceDaysInPeriod(a, periodFrom, stich));
                absFrac = dFull > 0 ? (decimal)dUpTo / dFull : 0m;
            }
            decimal stGearb      = workedToStich.TryGetValue(id, out var w) ? w : 0m;
            decimal stSollRedRaw = sollBrutto * dayRatio - reduktion * absFrac;
            decimal stAbsGut     = absGutMonat * absFrac;
            result[id] = Math.Round(vormonat + (stGearb + stAbsGut - stSollRedRaw), 2);
        }
        return result;
    }

    private static DateOnly? ParseDate(string? s)
        => DateOnly.TryParse(s, out var d) ? d : null;
}

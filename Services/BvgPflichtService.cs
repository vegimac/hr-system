using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// BVG-Versicherungspflicht pro Person (Walter 04.10.2026): Status aus
/// employee_bvg_pflicht + Vorschlag aus Vertrag bzw. Lohnmonaten
/// (Rechnung in <see cref="BvgPflichtVorschlag"/>).
/// </summary>
public class BvgPflichtService
{
    private static readonly HashSet<string> KrankUnfallCodes = new(StringComparer.OrdinalIgnoreCase)
        { "70.1", "70.2", "75.1", "60.2", "60.3", "65.1" };

    private readonly AppDbContext _db;
    public BvgPflichtService(AppDbContext db) { _db = db; }

    /// <summary>
    /// Status in der Lohnperiode: ein «versichert»-Eintrag, der die Periode berührt,
    /// gewinnt (Ein-/Austritt im Monat); sonst «nicht versichert»; ohne Eintrag null.
    /// </summary>
    public static bool? StatusInPeriode(IEnumerable<EmployeeBvgPflicht> eintraege, DateOnly von, DateOnly bis)
    {
        var treffer = eintraege.Where(e => e.Ueberlappt(von, bis)).ToList();
        if (treffer.Count == 0) return null;
        return treffer.Any(e => e.Versichert);
    }

    public async Task<bool?> StatusInPeriodeAsync(int employeeId, DateOnly von, DateOnly bis)
    {
        var eintraege = await _db.EmployeeBvgPflichten.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId && e.GueltigAb <= bis && (e.GueltigBis == null || e.GueltigBis >= von))
            .ToListAsync();
        return StatusInPeriode(eintraege, von, bis);
    }

    public async Task<BvgPflichtVorschlag.Ergebnis?> VorschlagAsync(int employeeId, DateOnly stichtag)
    {
        var r = await VorschlaegeAsync(new[] { employeeId }, stichtag);
        return r.TryGetValue(employeeId, out var v) ? v : null;
    }

    /// <summary>
    /// Vorschläge für viele MA in wenigen Abfragen. Vertrag = der am Stichtag gültige,
    /// sonst der nächste künftige. MA ohne solchen Vertrag fehlen im Ergebnis.
    /// </summary>
    public async Task<Dictionary<int, BvgPflichtVorschlag.Ergebnis>> VorschlaegeAsync(
        IReadOnlyCollection<int> employeeIds, DateOnly stichtag)
    {
        var result = new Dictionary<int, BvgPflichtVorschlag.Ergebnis>();
        if (employeeIds.Count == 0) return result;
        var ids = employeeIds.Distinct().ToList();
        var tag = stichtag.ToDateTime(TimeOnly.MinValue);

        var vertraege = await _db.Employments.AsNoTracking()
            .Where(e => ids.Contains(e.EmployeeId) && (e.ContractEndDate == null || e.ContractEndDate >= tag))
            .Select(e => new { e.EmployeeId, e.CompanyProfileId, e.EmploymentModel, e.ContractStartDate,
                               e.MonthlySalary, e.GuaranteedHoursPerWeek, e.HourlyRate, e.ThirteenthSalary })
            .ToListAsync();
        var firmen = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.DefaultThirteenthSalaryPercent })
            .ToDictionaryAsync(c => c.Id, c => c.DefaultThirteenthSalaryPercent ?? 8.33m);
        var schwellen = await _db.SocialInsuranceRates.AsNoTracking()
            .Where(r => r.IsActive && r.Code == "BVG" && r.EntryThresholdYearly != null && r.EntryThresholdYearly > 0
                     && r.ValidFrom <= stichtag && (r.ValidTo == null || r.ValidTo >= stichtag))
            .Select(r => new { r.CompanyProfileId, r.EntryThresholdYearly })
            .ToListAsync();

        var vonMonat = new DateOnly(stichtag.Year, stichtag.Month, 1).AddMonths(-MaxMonateZurueck);
        var snaps = await _db.PayrollSnapshots.AsNoTracking()
            .Where(s => ids.Contains(s.EmployeeId) && s.Status != "STORNIERT"
                     && s.Periode != null && s.Periode.PeriodFrom >= vonMonat && s.Periode.PeriodFrom <= stichtag)
            .Select(s => new { s.EmployeeId, s.Periode!.Year, s.Periode.Month, s.SvBasisBvg, s.SlipJson })
            .ToListAsync();
        var monateJeMa = snaps
            .GroupBy(s => s.EmployeeId)
            .ToDictionary(g => g.Key, g => g
                // Mehrere Filialen im selben Monat (Übertritt) zusammenzählen.
                .GroupBy(s => (s.Year, s.Month))
                .Select(m =>
                {
                    decimal basis = 0m; bool krank = false;
                    foreach (var s in m)
                    {
                        var (dreizehnter, k) = LeseSlip(s.SlipJson);
                        basis += s.SvBasisBvg - dreizehnter;
                        krank |= k;
                    }
                    return new BvgPflichtVorschlag.LohnMonat(m.Key.Year, m.Key.Month, basis, krank);
                }).ToList());

        foreach (var id in ids)
        {
            var v = vertraege.Where(e => e.EmployeeId == id && DateOnly.FromDateTime(e.ContractStartDate) <= stichtag)
                        .OrderByDescending(e => e.ContractStartDate).FirstOrDefault()
                 ?? vertraege.Where(e => e.EmployeeId == id).OrderBy(e => e.ContractStartDate).FirstOrDefault();
            if (v == null) continue;
            var cp = v.CompanyProfileId ?? 0;
            var schwelle = schwellen.Where(s => s.CompanyProfileId == cp).Select(s => s.EntryThresholdYearly).FirstOrDefault()
                        ?? schwellen.Where(s => s.CompanyProfileId == null).Select(s => s.EntryThresholdYearly).FirstOrDefault()
                        ?? 0m;
            var pct = firmen.TryGetValue(cp, out var p) ? p : 8.33m;
            result[id] = BvgPflichtVorschlag.Berechne(
                v.EmploymentModel, v.MonthlySalary, v.GuaranteedHoursPerWeek, v.HourlyRate,
                v.ThirteenthSalary, pct, schwelle,
                monateJeMa.TryGetValue(id, out var mon) ? mon : new List<BvgPflichtVorschlag.LohnMonat>());
        }
        return result;
    }

    private const int MaxMonateZurueck = 14;

    /// <summary>13.-ML-Betrag (180.x) und ob Krankheit/Unfall im Monat vorkam.</summary>
    public static (decimal Dreizehnter, bool KrankOderUnfall) LeseSlip(string? slipJson)
    {
        if (string.IsNullOrWhiteSpace(slipJson) || slipJson == "{}") return (0m, false);
        try
        {
            using var doc = JsonDocument.Parse(slipJson);
            if (!doc.RootElement.TryGetProperty("lohnLines", out var lines) || lines.ValueKind != JsonValueKind.Array)
                return (0m, false);
            decimal dreizehnter = 0m; bool krank = false;
            foreach (var z in lines.EnumerateArray())
            {
                var code = z.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? (c.GetString() ?? "").Trim() : "";
                decimal betrag = z.TryGetProperty("betrag", out var b) && b.ValueKind == JsonValueKind.Number && b.TryGetDecimal(out var d) ? d : 0m;
                if (code.StartsWith("180.")) dreizehnter += betrag;
                else if (KrankUnfallCodes.Contains(code) && betrag != 0) krank = true;
            }
            return (dreizehnter, krank);
        }
        catch (JsonException) { return (0m, false); }
    }
}

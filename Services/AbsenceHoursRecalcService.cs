using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Nachrechnung von absence.hours_credited (+ worked_days) wenn ein AbsenzTyp
/// umkonfiguriert wird (z.B. 1/5 → 1/7). Walter-Vorgabe 31.07.2026:
/// alle MA, alle Filialen — aber NUR Absenzen, die noch NICHT «In Lohn
/// verwendet» sind (gleiche Badge-Regel wie Absenzen-Tab:
/// DateFrom/DateTo vor FirstAllowedDate der Filiale).
/// </summary>
public class AbsenceHoursRecalcService
{
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;

    public AbsenceHoursRecalcService(AppDbContext db, LohnEditLockService editLock)
    {
        _db       = db;
        _editLock = editLock;
    }

    public record RecalcResult(int Updated, int SkippedLocked, int SkippedNoChange);

    /// <summary>
    /// Rechnet alle Absenzen dieses Typs neu. Gibt Zähler zurück.
    /// </summary>
    public async Task<RecalcResult> RecalcForTypeAsync(AbsenzTyp typ)
    {
        if (typ is null || string.IsNullOrWhiteSpace(typ.Code))
            return new RecalcResult(0, 0, 0);

        var code = typ.Code.Trim().ToUpperInvariant();
        var absences = await _db.Absences
            .Where(a => a.AbsenceType == code)
            .ToListAsync();
        if (absences.Count == 0) return new RecalcResult(0, 0, 0);

        var empIds = absences.Select(a => a.EmployeeId).Distinct().ToList();
        var employments = await _db.Employments.AsNoTracking()
            .Where(e => empIds.Contains(e.EmployeeId))
            .ToListAsync();
        var branchIds = employments
            .Where(e => e.CompanyProfileId.HasValue && e.CompanyProfileId.Value > 0)
            .Select(e => e.CompanyProfileId!.Value)
            .Distinct()
            .ToList();
        var profiles = await _db.CompanyProfiles.AsNoTracking()
            .Where(p => branchIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        // FirstAllowedDate pro Filiale (UI-Badge «In Lohn verwendet»)
        var firstAllowedByBranch = new Dictionary<int, DateOnly?>();
        foreach (var bid in branchIds)
            firstAllowedByBranch[bid] = await _editLock.GetFirstAllowedDateAsync(null, bid);

        int updated = 0, skippedLocked = 0, skippedNoChange = 0;

        foreach (var a in absences)
        {
            var emp = ResolveEmployment(employments, a);
            var branchId = emp?.CompanyProfileId ?? 0;
            if (branchId > 0
                && firstAllowedByBranch.TryGetValue(branchId, out var first)
                && first is DateOnly fa
                && (a.DateFrom < fa || a.DateTo < fa))
            {
                skippedLocked++;
                continue;
            }

            profiles.TryGetValue(branchId, out var profile);
            var model = emp?.EmploymentModel ?? "";

            // WorkedDays: KRANK/UNFALL behalten User-Auswahl; sonst nach Modus neu.
            string? newWorkedJson = a.WorkedDays;
            List<string> dayList;
            if (code is "KRANK" or "UNFALL")
            {
                dayList = ParseWorkedDays(a.WorkedDays, a.DateFrom, a.DateTo);
            }
            else
            {
                dayList = BuildDaysForModus(a.DateFrom, a.DateTo, typ.GutschriftModus);
                newWorkedJson = JsonSerializer.Serialize(dayList);
            }

            var newHours = ComputeHours(code, model, typ, profile, emp, dayList.Count, a.Prozent);

            bool changed = a.HoursCredited != newHours
                        || !string.Equals(a.WorkedDays ?? "", newWorkedJson ?? "", StringComparison.Ordinal);
            if (!changed)
            {
                skippedNoChange++;
                continue;
            }

            a.HoursCredited = newHours;
            a.WorkedDays    = newWorkedJson;
            a.UpdatedAt     = DateTime.Now;
            updated++;
        }

        if (updated > 0)
            await _db.SaveChangesAsync();

        return new RecalcResult(updated, skippedLocked, skippedNoChange);
    }

    /// <summary>
    /// Einmalige Altbestand-Bereinigung (Walter 13.08.2026): KRANK/UNFALL-
    /// Absenzen aus Alt-Importen zeigen hours_credited mit ALLEN Kalendertagen
    /// (z.B. 16.80 statt 8.40), obwohl die Tagesauswahl (worked_days) weniger
    /// «hätte gearbeitet»-Tage enthält. Es werden NUR die Stunden aus der
    /// BESTEHENDEN Tagesauswahl neu berechnet — die Auswahl selbst bleibt
    /// unangetastet (Dienstplan-Regel: Sa/So NICHT pauschal entfernen, in der
    /// Gastro wird auch am Wochenende gearbeitet; leer = Mo–Fr-Fallback).
    /// hours_credited ist NICHT lohnwirksam (die Engine rechnet dynamisch) —
    /// darum werden bewusst AUCH «In Lohn verwendet»-Absenzen korrigiert.
    /// Idempotent: zweiter Lauf ändert nichts mehr.
    /// </summary>
    public async Task<RecalcResult> FixKrankUnfallHoursAsync()
    {
        var absences = await _db.Absences
            .Where(a => a.AbsenceType == "KRANK" || a.AbsenceType == "UNFALL")
            .ToListAsync();
        if (absences.Count == 0) return new RecalcResult(0, 0, 0);

        var typen = await _db.AbsenzTypen.AsNoTracking().ToListAsync();
        var empIds = absences.Select(a => a.EmployeeId).Distinct().ToList();
        var employments = await _db.Employments.AsNoTracking()
            .Where(e => empIds.Contains(e.EmployeeId))
            .ToListAsync();
        var profiles = await _db.CompanyProfiles.AsNoTracking().ToDictionaryAsync(p => p.Id);

        int updated = 0, skippedNoChange = 0;
        foreach (var a in absences)
        {
            var typ = typen.FirstOrDefault(t => t.Code == a.AbsenceType);
            if (typ == null) continue;
            var emp = ResolveEmployment(employments, a);
            profiles.TryGetValue(emp?.CompanyProfileId ?? 0, out var profile);

            // Tagesauswahl UNVERÄNDERT übernehmen (leer → Mo–Fr-Fallback).
            var dayList = ParseWorkedDays(a.WorkedDays, a.DateFrom, a.DateTo);
            var newHours = ComputeHours(a.AbsenceType, emp?.EmploymentModel ?? "",
                                        typ, profile, emp, dayList.Count, a.Prozent);

            if (a.HoursCredited == newHours) { skippedNoChange++; continue; }

            a.HoursCredited = newHours;
            a.UpdatedAt     = DateTime.Now;
            updated++;
        }
        if (updated > 0) await _db.SaveChangesAsync();
        return new RecalcResult(updated, 0, skippedNoChange);
    }

    private static Employment? ResolveEmployment(List<Employment> all, Absence a)
    {
        var fromDt = a.DateFrom.ToDateTime(TimeOnly.MinValue);
        var toDt   = a.DateTo.ToDateTime(TimeOnly.MinValue);
        return all
            .Where(e => e.EmployeeId == a.EmployeeId
                     && e.ContractStartDate <= toDt
                     && (e.ContractEndDate == null || e.ContractEndDate >= fromDt))
            .OrderByDescending(e => e.IsActive)
            .ThenByDescending(e => e.ContractStartDate)
            .FirstOrDefault()
            ?? all.Where(e => e.EmployeeId == a.EmployeeId)
                  .OrderByDescending(e => e.IsActive)
                  .ThenByDescending(e => e.ContractStartDate)
                  .FirstOrDefault();
    }

    private static List<string> ParseWorkedDays(string? json, DateOnly from, DateOnly to)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var arr = JsonSerializer.Deserialize<string[]>(json);
                if (arr is { Length: > 0 })
                    return arr.Where(s => DateOnly.TryParse(s, out _)).ToList();
            }
            catch { /* fall through */ }
        }
        return BuildDaysForModus(from, to, "1/5");
    }

    /// <summary>
    /// 1/7 → alle Kalendertage; 1/5 (Default) → nur Mo–Fr.
    /// </summary>
    private static List<string> BuildDaysForModus(DateOnly from, DateOnly to, string? modus)
    {
        var all = new List<string>();
        if (to < from) return all;
        bool calendar = string.Equals(modus, "1/7", StringComparison.OrdinalIgnoreCase);
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (!calendar && (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday))
                continue;
            all.Add(d.ToString("yyyy-MM-dd"));
        }
        return all;
    }

    /// <summary>
    /// Spiegelt PayrollCalculationEngine.ComputeAbsenzHours + UI-Sonderfälle
    /// (MTP/FLEX Ferien = 0, FLEX ohne UtpAuszahlung = 0, Unbezahlt = 0).
    /// </summary>
    private static decimal ComputeHours(
        string absenceType, string empModel, AbsenzTyp typ,
        CompanyProfile? profile, Employment? emp, int dayCount, decimal prozent)
    {
        if (dayCount <= 0) return 0m;

        if (absenceType == "UNBEZ_URLAUB") return 0m;

        if ((empModel == "MTP" || empModel == "FLEX")
            && (absenceType == "FERIEN" || absenceType == "FEIERTAG"))
            return 0m;

        if (empModel == "FLEX" && !typ.UtpAuszahlung) return 0m;

        decimal betriebWeekly = profile?.NormalWeeklyHours ?? 42m;
        decimal weeklyH       = betriebWeekly;
        decimal pct           = emp?.EmploymentPercentage ?? 100m;

        // MTP: immer Garantie-Wochenstunden (wie PayrollCalculationEngine).
        if (empModel == "MTP")
        {
            weeklyH = emp?.GuaranteedHoursPerWeek
                   ?? emp?.WeeklyHours
                   ?? betriebWeekly;
        }
        else if (string.Equals(typ.BasisStunden, "VERTRAG", StringComparison.OrdinalIgnoreCase)
                 && (empModel == "FIX" || empModel == "FIX-M")
                 && (absenceType == "FERIEN" || absenceType == "FEIERTAG"))
        {
            weeklyH = Math.Round(betriebWeekly * pct / 100m, 2);
        }

        string modus = typ.GutschriftModus ?? "1/5";
        decimal divisor = string.Equals(modus, "1/7", StringComparison.OrdinalIgnoreCase) ? 7m : 5m;
        decimal p = prozent > 0 ? prozent : 100m;
        return Math.Round(dayCount * weeklyH / divisor * p / 100m, 2);
    }
}

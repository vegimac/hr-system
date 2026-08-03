using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Absolute gestempelte Stunden (Walter-Vorgabe 03.08.2026):
/// Total = gesamte Anwesenheitszeit (Tag + Nacht), nicht nur der Tagesanteil.
/// Nachtstunden bleiben separat für den Nacht-Saldo (10 %-Zuschlag /
/// NACHT_KOMP-Auszahlung) — sie sind aber Teil der bezahlten IST-Stunden.
/// </summary>
public static class TimeEntryHours
{
    /// <summary>
    /// Absolute Arbeitsstunden eines Stempel-Eintrags.
    /// Wanduhr (In/Out), TotalHours und Tag+Nacht werden zusammengeführt —
    /// Alt-Daten mit Total/Wanduhr = nur Tag + NightHours separat werden
    /// auf Tag+Nacht angehoben (Walter-Bug 03.08.2026: 116.59 statt 125.60).
    /// </summary>
    public static decimal AbsoluteHours(EmployeeTimeEntry t)
    {
        if (t == null) return 0m;
        return AbsoluteHours(t.TimeIn, t.TimeOut, t.TotalHours, t.DurationHours, t.NightHours);
    }

    public static decimal AbsoluteHours(
        DateTime timeIn, DateTime? timeOut,
        decimal? totalHours, decimal? durationHours, decimal? nightHours)
    {
        decimal? wall = null;
        if (timeOut.HasValue && timeOut.Value > timeIn)
            wall = Math.Round((decimal)(timeOut.Value - timeIn).TotalHours, 2);

        // Grösster vertrauenswürdiger Total-Kandidat (Sync-Total oder Wanduhr).
        // Danach dieselbe Tag+Nacht-Korrektur wie ohne Zeitstempel — sonst
        // würde ein In/Out, das nur den Tag-Anteil abbildet, die Nacht
        // verschlucken (genau der Live-Bug: Lohn 116.59, Stempel-Total 125.60).
        decimal? preferredTotal = totalHours;
        if (wall.HasValue)
            preferredTotal = preferredTotal.HasValue
                ? Math.Max(preferredTotal.Value, wall.Value)
                : wall;

        return AbsoluteHours(preferredTotal, durationHours, nightHours);
    }

    /// <summary>
    /// Fallback ohne Zeitstempel — robust gegen Alt-Daten, in denen
    /// <c>TotalHours</c> fälschlich nur dem Tag-Anteil entspricht.
    /// </summary>
    public static decimal AbsoluteHours(decimal? totalHours, decimal? durationHours, decimal? nightHours)
    {
        var night = nightHours ?? 0m;
        var dur = durationHours ?? 0m;
        var tot = totalHours ?? 0m;
        var parts = dur + night;

        if (parts <= 0m) return tot;
        if (tot <= 0m) return Math.Round(parts, 2);

        // Total bereits = Tag+Nacht (Sync-korrekt) → Total
        if (tot >= parts - 0.05m) return tot;

        // Total ≈ nur Tag, Nacht > 0 → Tag+Nacht (Alt-Daten-Bug)
        if (night > 0m && Math.Abs(tot - dur) <= 0.05m)
            return Math.Round(parts, 2);

        return Math.Round(Math.Max(tot, parts), 2);
    }

    public static decimal SumAbsolute(IEnumerable<EmployeeTimeEntry> entries)
        => Math.Round(entries.Sum(AbsoluteHours), 2);
}

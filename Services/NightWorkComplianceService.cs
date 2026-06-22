namespace HrSystem.Services;

/// <summary>
/// Nachtarbeit-Compliance (Walter-Vorgabe 22.06.2026, ArGV1 Art. 30).
///
/// NEUE Regel (ersetzt „≥ 25 Nächte/Jahr"): Gewarnt wird, wenn in einem
/// rollierenden 6-Wochen-Fenster (42 Kalendertage) MEHR ALS 18 gearbeitete
/// Nächte liegen — also <c>maxNightsIn42Days &gt; 18</c> (strikt grösser, NICHT
/// ≥ 18). Eine „Nacht" = Kalendertag mit <c>employee_time_entry.night_hours &gt; 0</c>.
///
/// Reine Rechenlogik, seiteneffektfrei (alle Daten als Parameter) → unit-testbar.
/// </summary>
public static class NightWorkComplianceService
{
    /// <summary>Fenstergrösse in Kalendertagen (6 Wochen).</summary>
    public const int WindowDays = 42;
    /// <summary>Schwelle: Warnfall ist &gt; 18 (nicht ≥ 18).</summary>
    public const int Threshold = 18;

    public record Result(int MaxNightsInSixWeeks, DateOnly? WindowFrom, DateOnly? WindowTo, bool RequiresDocuments);

    /// <summary>
    /// Maximale Anzahl Nacht-Tage in irgendeinem 42-Tage-Fenster bestimmen.
    /// Algorithmus: distinct + sortiert; für jeden Nacht-Tag das Fenster
    /// [date, date+41] zählen; Maximum merken. <paramref name="asOf"/> begrenzt
    /// nach oben (zukünftige Tage zählen nicht).
    /// </summary>
    public static Result Evaluate(IEnumerable<DateOnly> nightDates, DateOnly asOf)
    {
        var dates = nightDates.Where(d => d <= asOf).Distinct().OrderBy(d => d).ToList();
        if (dates.Count == 0) return new Result(0, null, null, false);

        int max = 0;
        DateOnly bestFrom = dates[0], bestTo = dates[0].AddDays(WindowDays - 1);
        foreach (var from in dates)
        {
            var to = from.AddDays(WindowDays - 1);
            int count = dates.Count(d => d >= from && d <= to);
            if (count > max) { max = count; bestFrom = from; bestTo = to; }
        }
        return new Result(max, bestFrom, bestTo, max > Threshold);
    }
}

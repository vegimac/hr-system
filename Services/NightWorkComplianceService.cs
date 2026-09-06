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

    /// <summary>Betrachtungszeitraum: die letzten 3 Monate bis asOf (Walter 06.09.2026).</summary>
    public const int LookbackMonths = 3;

    /// <summary>
    /// Maximale Anzahl Nacht-Tage in irgendeinem 42-Tage-Fenster INNERHALB der
    /// letzten 3 Monate bestimmen. Walter 06.09.2026 (Fall «23 Nächte / 6
    /// Wochen» ohne Nachtarbeit seit Monaten): bisher galt das Maximum über
    /// 12 Monate — ein Nacht-Block vom letzten Herbst hielt die Untersuch-
    /// Pflicht ein Jahr lang am Leben. Nur die letzten 6 Wochen zu zählen wäre
    /// aber ein Flip-Flop (Ferien → Pflicht weg → Pflicht wieder da), darum
    /// 3 Monate Datenfenster mit rollierendem 6-Wochen-Maximum darin.
    /// </summary>
    public static Result Evaluate(IEnumerable<DateOnly> nightDates, DateOnly asOf)
    {
        var lookFrom = asOf.AddMonths(-LookbackMonths).AddDays(1);
        var dates = nightDates.Where(d => d >= lookFrom && d <= asOf).Distinct().OrderBy(d => d).ToList();
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

    /// <summary>
    /// Gekündigte / austretende MA (Austrittsdatum ODER «Kündigung per»
    /// erfasst) unterliegen in OneCrew keiner Untersuch-Pflicht mehr — ein
    /// neues Arztzeugnis lohnt sich nicht mehr (Walter 06.09.2026, ersetzt die
    /// frühere 30-Tage-Grenze).
    /// </summary>
    public static bool Ausgenommen(DateTime? exitDate, DateTime? kuendigungPer)
        => exitDate.HasValue || kuendigungPer.HasValue;
}

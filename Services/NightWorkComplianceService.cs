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
    /// Nacht-Tage im AKTUELLEN 6-Wochen-Fenster zählen: [asOf−41, asOf].
    /// Walter 06.09.2026 (Fall «23 Nächte / 6 Wochen» ohne Nachtarbeit seit
    /// Monaten): bisher galt das MAXIMUM über alle 42-Tage-Fenster der letzten
    /// 12 Monate — ein Nacht-Block vom letzten Herbst hielt die Untersuch-
    /// Pflicht ein Jahr lang am Leben. Die Pflicht besteht aber nur, solange
    /// der MA tatsächlich regelmässig nachts arbeitet — massgebend sind die
    /// letzten 6 Wochen. Selbstheilend in beide Richtungen.
    /// </summary>
    public static Result Evaluate(IEnumerable<DateOnly> nightDates, DateOnly asOf)
    {
        var winFrom = asOf.AddDays(-(WindowDays - 1));
        var dates = nightDates.Where(d => d >= winFrom && d <= asOf).Distinct().ToList();
        if (dates.Count == 0) return new Result(0, null, null, false);
        return new Result(dates.Count, winFrom, asOf, dates.Count > Threshold);
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

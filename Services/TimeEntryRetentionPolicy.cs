namespace HrSystem.Services;

/// <summary>
/// Reine, seiteneffektfreie Logik für die Stempelzeiten-Aufbewahrung
/// (Walter-Vorgabe 21.06.2026). DB- und Zeit-frei → unit-testbar.
///
///   • Stempelzeiten älter als X Jahre werden gelöscht (Default X = 5).
///   • Lauf nur am 1. des Monats.
///   • Sicherheits-Riegel: NICHT löschen, wenn X &lt; 5 — ausser explizit
///     erlaubt (AllowShortRetention).
/// </summary>
public static class TimeEntryRetentionPolicy
{
    /// <summary>Absolute Untergrenze für die Aufbewahrung (Jahre).</summary>
    public const int MinRetentionYears = 5;

    /// <summary>
    /// Cutoff-Datum: alle Stempelzeiten mit entry_date &lt; Cutoff werden gelöscht.
    /// Entspricht SQL „current_date - interval 'X years'".
    /// </summary>
    public static DateOnly ComputeCutoff(DateOnly today, int retentionYears)
        => today.AddYears(-retentionYears);

    /// <summary>Der Retention-Lauf findet nur am 1. eines Monats statt.</summary>
    public static bool IsRunDay(DateOnly date) => date.Day == 1;

    /// <summary>
    /// Effektive Aufbewahrungs-Jahre: gespeicherter Wert (UI) oder Config-Default.
    /// </summary>
    public static int EffectiveYears(int? storedValue, int configDefault)
        => storedValue ?? configDefault;

    /// <summary>
    /// Löschen erlaubt? Nur wenn die Aufbewahrung mindestens
    /// <see cref="MinRetentionYears"/> beträgt — oder kürzere Werte explizit
    /// freigegeben sind (AllowShortRetention).
    /// </summary>
    public static bool IsRetentionAllowed(int retentionYears, bool allowShort)
        => retentionYears >= MinRetentionYears || allowShort;
}

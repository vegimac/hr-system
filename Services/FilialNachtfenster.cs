namespace HrSystem.Services;

/// <summary>
/// Nachtfenster aus <c>company_profile.night_start_time</c> / <c>night_end_time</c>
/// (VARCHAR HH:mm, Ortszeit Zürich — kein Zeitstempel, keine Sommerzeit-Verschiebung).
/// Leer oder ungültig = 00:00–07:00, gleich wie der easy@work-Stempel-Sync.
/// </summary>
public static class FilialNachtfenster
{
    public static TimeSpan Beginn(string? hhmm) => Parse(hhmm, TimeSpan.Zero);
    public static TimeSpan Ende(string? hhmm) => Parse(hhmm, new TimeSpan(7, 0, 0));

    public static (TimeSpan Von, TimeSpan Bis) Aus(string? start, string? end) =>
        (Beginn(start), Ende(end));

    static TimeSpan Parse(string? s, TimeSpan fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        return TimeSpan.TryParse(s.Trim(), out var ts) ? ts : fallback;
    }
}

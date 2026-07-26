namespace HrSystem.Models;

/// <summary>
/// Austrittsgrund am MA (Walter 26.07.2026) — kurze Codes für Statistik.
/// Getrennt von «Kündigung durch» (AG/AN).
/// </summary>
public static class AustrittsgrundCodes
{
    public static readonly (string Code, string Label)[] All =
    [
        ("AUSBILDUNG", "Ausbildung"),
        ("ANDERER_JOB", "Anderer Job"),
        ("UMZUG", "Umzug"),
        ("FAMILIE", "Familie"),
        ("GESUNDHEIT", "Gesundheit"),
        ("ARBEITSZEITEN", "Arbeitszeiten"),
        ("LOHN", "Lohn/Pensum"),
        ("TEAM", "Team/Führung"),
        ("PROBEZEIT", "Probezeit"),
        ("LEISTUNG", "Leistung"),
        ("VERFUEGBARKEIT", "Verfügbarkeit"),
        ("VERHALTEN", "Verhalten"),
        ("BEFRISTUNG", "Befristung"),
        ("DIVERS", "Divers"),
    ];

    private static readonly HashSet<string> Codes =
        new(All.Select(x => x.Code), StringComparer.OrdinalIgnoreCase);

    public static bool IsValid(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Codes.Contains(code.Trim());

    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = code.Trim().ToUpperInvariant();
        return Codes.Contains(c) ? c : null;
    }

    public static string LabelOf(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "—";
        var c = code.Trim().ToUpperInvariant();
        foreach (var (k, lbl) in All)
            if (k == c) return lbl;
        return c;
    }
}

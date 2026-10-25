namespace HrSystem.Services;

/// <summary>
/// BVG-Lösung (Swissdec 8.6.2 / 8.6.3): Code bis 32 Stellen, eine Lohnart
/// gehört höchstens einer Lösung. Ohne Code an der Lohnart zählt sie wie bisher
/// für jede Lösung.
/// </summary>
public static class BvgLoesung
{
    public const int MaxStellen = 32;

    public static bool IstBvgSatz(string? svCode)
        => string.Equals((svCode ?? "").Trim(), "BVG", StringComparison.OrdinalIgnoreCase);

    public static string? CodeFehler(string? svCode, string? loesungsCode)
    {
        if (!IstBvgSatz(svCode) || string.IsNullOrWhiteSpace(loesungsCode)) return null;
        if (loesungsCode.Trim().Length <= MaxStellen) return null;
        return "Der BVG-Code hat höchstens 32 Stellen (z.B. 1101 Basisplan, 2020 Kaderplan).";
    }

    /// <summary>
    /// Zählt der Betrag zur BVG-Basis dieser Person? Ohne Lösung an der Lohnart: ja,
    /// sobald die Lohnart BVG-pflichtig ist. Mit Lösung: nur wenn die Person denselben Code hat.
    /// Fehlende Lohnart bleibt pflichtig (bisheriger Fallback).
    /// </summary>
    public static bool Zaehlt(Models.Lohnposition? lp, string? personenCode)
    {
        if (lp == null) return true;
        if (!lp.BvgPflichtig) return false;
        if (string.IsNullOrWhiteSpace(lp.BvgLoesungsCode)) return true;
        var person = (personenCode ?? "").Trim();
        if (person.Length == 0) return false;
        return string.Equals(lp.BvgLoesungsCode.Trim(), person, StringComparison.OrdinalIgnoreCase);
    }
}

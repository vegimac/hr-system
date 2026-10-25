namespace HrSystem.Services;

/// <summary>
/// UVGZ-Versicherungslösung auf dem SV-Satz (Swissdec 8.4.1).
/// Zwei Stellen: Personengruppe und Kategorie, je A–Z oder 0–9.
/// Beispiele: 10, 11, A2, B1. Leer bleibt erlaubt (Zeile ohne Code).
/// </summary>
public static class UvgzLoesung
{
    public static bool IstUvgzSatz(string? svCode)
        => string.Equals((svCode ?? "").Trim(), "UVGZ", StringComparison.OrdinalIgnoreCase);

    /// <summary>null = leer oder zwei Stellen A–Z/0–9. Sonst die Masken-Meldung.</summary>
    public static string? CodeFehler(string? loesungsCode)
        => ZweiStellenFehler("UVGZ-Code", loesungsCode);

    /// <summary>Gleiche Zwei-Stellen-Regel für UVGZ und KTG. begriff steht in der Meldung, z.B. «KTG-Code».</summary>
    public static string? ZweiStellenFehler(string begriff, string? loesungsCode)
    {
        if (string.IsNullOrWhiteSpace(loesungsCode)) return null;
        var c = loesungsCode.Trim().ToUpperInvariant();
        if (c.Length == 2 && Stelle(c[0]) && Stelle(c[1])) return null;
        return $"Der {begriff} hat zwei Stellen. Jede Stelle ist ein Buchstabe A–Z oder eine Ziffer 0–9 (z.B. 11, A2, B1).";
    }

    public static string? LoesungsCodeFehler(string? svCode, string? loesungsCode)
        => IstUvgzSatz(svCode) ? CodeFehler(loesungsCode) : null;

    private static bool Stelle(char c) => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
}

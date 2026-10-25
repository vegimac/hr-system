namespace HrSystem.Services;

/// <summary>
/// UVG-Versicherungslösung auf dem SV-Satz (Swissdec 8.3.2).
/// Die erste Stelle ist der Betriebsteil (A–Z), z.B. «A» Gesamtes Personal.
/// Die Ziffer 0–3 hängt an der Person und wird hier nicht verlangt.
/// </summary>
public static class UvgLoesung
{
    public static bool IstUvgSatz(string? svCode)
    {
        var c = (svCode ?? "").Trim().ToUpperInvariant();
        return c is "NBUV" or "BUV";
    }

    /// <summary>null = leer oder erste Stelle A–Z. Sonst die Masken-Meldung.</summary>
    public static string? ErsteStelleFehler(string? loesungsCode)
    {
        if (string.IsNullOrWhiteSpace(loesungsCode)) return null;
        var c = loesungsCode.Trim().ToUpperInvariant()[0];
        if (c >= 'A' && c <= 'Z') return null;
        return "Die erste Stelle des UVG-Codes muss ein Buchstabe A–Z sein (Betriebsteil, z.B. A Gesamtes Personal).";
    }

    public static string? LoesungsCodeFehler(string? svCode, string? loesungsCode)
        => IstUvgSatz(svCode) ? ErsteStelleFehler(loesungsCode) : null;
}

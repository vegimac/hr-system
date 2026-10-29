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

    public const string CodeFehlt =
        "UVG-Code fehlt. Betriebsteil und Ziffer 0, 1, 2 oder 3 angeben.";

    /// <summary>
    /// Personencode = Betriebsteil vom Unternehmen + Ziffer 0–3.
    /// 4–9 sind keine gültige zweite Stelle.
    /// </summary>
    public static string? PersonenCodeFehler(string? code, IEnumerable<string?>? unternehmensCodes)
    {
        if (string.IsNullOrWhiteSpace(code)) return CodeFehlt;
        var c = code.Trim().ToUpperInvariant();
        if (c.Length != 2 || c[0] < 'A' || c[0] > 'Z')
            return "Der UVG-Code hat zwei Stellen: der Betriebsteil vom Unternehmen und die Ziffer 0, 1, 2 oder 3.";
        if (c[1] < '0' || c[1] > '3')
            return "Die zweite Stelle des UVG-Codes darf nur 0, 1, 2 oder 3 sein.";
        var teile = new HashSet<char>();
        if (unternehmensCodes != null)
            foreach (var raw in unternehmensCodes)
            {
                var t = (raw ?? "").Trim().ToUpperInvariant();
                if (t.Length > 0 && t[0] >= 'A' && t[0] <= 'Z') teile.Add(t[0]);
            }
        if (!teile.Contains(c[0]))
            return $"Der Betriebsteil «{c[0]}» ist am Unternehmen nicht hinterlegt.";
        return null;
    }
}

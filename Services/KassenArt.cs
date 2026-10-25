namespace HrSystem.Services;

/// <summary>
/// Kantonale Ausgleichskasse oder Verbandsausgleichskasse, aus der AK-Nummer
/// der Swissdec-Empfängerliste (Walter 25.10.2026).
/// 001–025 und 150 (Jura) sind kantonal. Ab 028 ist es ein Verband.
/// 026 ist die eidgenössische Ausgleichskasse und bleibt ohne Zuordnung.
/// </summary>
public static class KassenArt
{
    public const string Kantonal = "KANTONAL";
    public const string Verband = "VERBAND";

    /// <summary>Nur Ausgleichskasse und FAK. Sonst leer.</summary>
    public static string? AusNummer(string? art, string? nummer)
    {
        if (art is not ("AUSGLEICHSKASSE" or "FAK")) return null;
        if (string.IsNullOrWhiteSpace(nummer)) return null;
        var block = nummer.Trim().Split('.')[0].Trim();
        if (block.Length == 0 || block.Any(c => !char.IsDigit(c))) return null;
        if (!int.TryParse(block, out var n)) return null;
        if ((n >= 1 && n <= 25) || n == 150) return Kantonal;
        if (n >= 28) return Verband;
        return null;
    }
}

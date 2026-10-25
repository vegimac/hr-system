namespace HrSystem.Services;

/// <summary>
/// KTG-Versicherungslösung auf dem SV-Satz (Swissdec 8.5.1).
/// Zwei Stellen wie beim UVGZ. Der Höchstlohn ist entweder der UVG-Höchstlohn
/// oder eine vertraglich definierte Obergrenze; der Lohn rechnet mit den Beträgen.
/// </summary>
public static class KtgLoesung
{
    public const string Uvgh = "UVG";
    public const string Vertrag = "VERTRAG";

    public static bool IstKtgSatz(string? svCode)
        => string.Equals((svCode ?? "").Trim(), "KTG", StringComparison.OrdinalIgnoreCase);

    public static string? LoesungsCodeFehler(string? svCode, string? loesungsCode)
        => IstKtgSatz(svCode) ? UvgzLoesung.ZweiStellenFehler("KTG-Code", loesungsCode) : null;

    /// <summary>Nur bei KTG: UVG oder VERTRAG. Sonst leer.</summary>
    public static string? HoechstlohnArt(string? svCode, string? art)
    {
        if (!IstKtgSatz(svCode)) return null;
        return string.Equals((art ?? "").Trim(), Vertrag, StringComparison.OrdinalIgnoreCase) ? Vertrag : Uvgh;
    }
}

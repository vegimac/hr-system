namespace HrSystem.Services;

/// <summary>
/// Logo pro Server (Walter 02.11.2026): mit <c>BRANDING_OHNE_BOGEN=1</c> zeigt OneCrew überall den
/// Schriftzug ohne Bogen (Abstand zu McDonald's-Marken, Einsatz ausserhalb McDonald's). Gleiches
/// Programm für alle Server — die Einstellung steht in der env-Datei des Servers, z.B. test.env.
/// </summary>
public static class Branding
{
    public static bool OhneBogen
    {
        get
        {
            var v = (Environment.GetEnvironmentVariable("BRANDING_OHNE_BOGEN") ?? "").Trim();
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase)
                            || v.Equals("ja", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>PNG-Dateiname des Logos für Server-PDFs und den E-Mail-Kopf.</summary>
    public static string LogoPng => OhneBogen ? "onecrew-schriftzug.png" : "onecrew-logo.png";

    /// <summary>
    /// Webseiten verweisen weiter auf <c>/img/onecrew-logo.*</c>; ohne Bogen liefert der Server
    /// unter derselben Adresse den Schriftzug. NULL = Pfad unverändert.
    /// </summary>
    public static string? Umleitung(string? pfad, bool ohneBogen)
    {
        if (!ohneBogen || string.IsNullOrEmpty(pfad)) return null;
        return pfad.ToLowerInvariant() switch
        {
            "/img/onecrew-logo.webp" => "/img/onecrew-schriftzug.webp",
            "/img/onecrew-logo.png"  => "/img/onecrew-schriftzug.png",
            "/img/onecrew-logo-hell.png" => "/img/onecrew-schriftzug-hell.png",
            _ => null,
        };
    }
}

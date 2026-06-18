namespace HrSystem.Services.EasyAtWork;

/// <summary>
/// Konfiguration für die easy@work-API-Integration. Wird aus
/// appsettings.json (Section "EasyAtWork") ODER aus ENV-Variablen
/// (`EASYATWORK_CLIENT_ID`, `EASYATWORK_CLIENT_SECRET`, `EASYATWORK_BASE_URL`)
/// geladen. Bewusst KEIN hardgecodeter Fallback — wenn die Werte fehlen,
/// startet die Integration nicht (analog Walter-Vorgabe 13.06.2026 für
/// JWT-Secret + DB-Passwort).
///
/// easy@work-Staging (laut Support 17.06.2026):
///   BaseUrl  = https://app.mfs.eatw.io
///   ClientId = 64
/// Produktion:
///   BaseUrl  = (von easy@work zu bestätigen, evtl. https://app.easyatwork.com)
///   ClientId = 2144
/// </summary>
public class EasyAtWorkSettings
{
    /// <summary>API-Basis-URL, OHNE Trailing-Slash.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>OAuth2-Client-ID.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>OAuth2-Client-Secret (NIEMALS ins Repo committen).</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>True wenn alle drei Pflichtfelder gesetzt sind.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);
}

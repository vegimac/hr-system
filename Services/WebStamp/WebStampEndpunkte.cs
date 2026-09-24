namespace HrSystem.Services.WebStamp;

/// <summary>
/// Adressen des Webservice WebStamp der Schweizerischen Post (SOAP V6,
/// Walter 24.09.2026). Laut Schnittstellenbeschreibung gibt es zwei
/// Testumgebungen; für Integratoren empfiehlt die Post die STABILE
/// (wsredesignint2, Releasestand der Produktion) — int1 ist die laufend
/// erweiterte Vorab-Version und bleibt hier bewusst weg.
///
/// Wie bei Swissdec (<c>ElmEndpunkte</c>) stehen die Adressen im Code und
/// nicht in einem Eingabefeld: die Einstellungen wählen nur «test» oder
/// «prod».
/// </summary>
public static class WebStampEndpunkte
{
    /// <summary>Stabile Testumgebung (Releasestand der Produktion) — kostenlos, nichts wird gedruckt.</summary>
    public const string TestUrl = "https://wsredesignint2.post.ch/wsws/soap/v6";

    /// <summary>Produktives System — Bestellungen kosten echtes Porto und werden gedruckt/verschickt.</summary>
    public const string ProdUrl = "https://webstamp.post.ch/wsws/soap/v6";

    public static bool IstProd(string? umgebung)
        => string.Equals((umgebung ?? "").Trim(), "prod", StringComparison.OrdinalIgnoreCase);

    public static string Url(string? umgebung) => IstProd(umgebung) ? ProdUrl : TestUrl;

    /// <summary>
    /// Testphase (Walter 24.09.2026): kostenpflichtige Bestellungen (<c>new_order</c>)
    /// NUR gegen die Testumgebung. Die kostenlose Vorschau geht überall. Für den
    /// Echtbetrieb wird diese Sperre bewusst per Code-Änderung aufgehoben — erst
    /// nach Abnahmeprotokoll + Integrationsvertrag der Post.
    /// </summary>
    public static bool BestellungErlaubt(string? umgebung) => !IstProd(umgebung);
}

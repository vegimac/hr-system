namespace HrSystem.Models;

/// <summary>
/// WebStamp-Konfiguration (Briefpost über die Schweizerische Post, Walter
/// 24.09.2026). Singleton-Tabelle mit Id=1, analog <see cref="EcallSetting"/>.
///
/// Das WSWS-Passwort liegt AES-verschlüsselt in <see cref="PasswordEncrypted"/>
/// (SimpleAesService) und wird in der API nie ausgegeben — nur das Flag
/// <c>HasPassword</c>.
/// </summary>
public class WebStampSetting
{
    public int Id { get; set; }                          // Singleton — immer 1

    /// <summary>«test» (stabile Testumgebung der Post) oder «prod».</summary>
    public string Umgebung { get; set; } = "test";

    /// <summary>Von der Post pro Applikation vergeben (max. 32 Zeichen).</summary>
    public string? ApplicationId { get; set; }

    /// <summary>WS-Kunden-ID aus dem WebStamp-Konto (max. 10 Zeichen).</summary>
    public string? KundenId { get; set; }

    public string? PasswordEncrypted { get; set; }

    /// <summary>Vorgewähltes Produkt (WebStamp-interne Nummer aus get_products).</summary>
    public int? ProduktNummer { get; set; }

    /// <summary>Adresse im rechten statt linken Couvert-Fenster.</summary>
    public bool FensterRechts { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;  // Lokalzeit (timestamp without time zone)
}

namespace HrSystem.Models;

/// <summary>
/// eCall-SMS-Konfiguration (F24 Schweiz, REST-API). Singleton-Tabelle:
/// es gibt immer nur Row mit Id=1. Analog <see cref="SmtpSetting"/>.
///
/// Das API-Passwort wird AES-verschlüsselt in <see cref="PasswordEncrypted"/>
/// gespeichert (SimpleAesService, Schlüssel = Jwt:Secret). In der API wird
/// es nie ausgegeben — stattdessen kommt nur ein Flag <c>HasPassword</c>
/// zurück. Ein neues Passwort wird nur dann gespeichert, wenn das PUT-DTO
/// das Feld nicht-leer schickt.
///
/// Sender: der eCall-Absender (bis 16 numerisch ODER bis 11 alphanumerisch,
/// z.B. «OneCrew»).
/// </summary>
public class EcallSetting
{
    public int Id { get; set; }                              // Singleton — immer 1
    public bool Enabled { get; set; }                        // false = SMS-Versand deaktiviert
    public string? Username { get; set; }                    // eCall API-Benutzer
    public string? PasswordEncrypted { get; set; }           // AES-verschlüsselt, Base64
    public string? Sender { get; set; }                      // der «from»-Absender

    /// <summary>
    /// Test-Umleitung (analog SmtpSetting.TestRedirectTo): solange hier eine
    /// Nummer steht, gehen ALLE SMS an diese Nummer statt an den echten
    /// Empfänger; der Text bekommt den Präfix «[TEST → originalnummer]».
    /// Leer/NULL = Echtbetrieb.
    /// </summary>
    public string? TestRedirectTo { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;  // Lokalzeit (timestamp without time zone)
}

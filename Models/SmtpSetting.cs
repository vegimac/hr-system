namespace HrSystem.Models;

/// <summary>
/// SMTP-Konfiguration für den Mail-Versand. Singleton-Tabelle: es gibt
/// immer nur Row mit Id=1. Vorher lebte das in appsettings.json:Smtp,
/// jetzt im Admin-UI editierbar (inkl. Test-Mail-Button).
///
/// Passwort wird AES-verschlüsselt in <see cref="PasswordEncrypted"/>
/// gespeichert (Schlüssel = Jwt:Secret aus appsettings.json).
/// In der API wird es nie ausgegeben — stattdessen kommt nur ein Flag
/// <c>HasPassword</c> zurück. Ein neues Passwort wird nur dann
/// gespeichert, wenn das PUT-DTO das Feld nicht-leer schickt.
///
/// TestRedirectTo: wenn gesetzt, gehen ALLE Mails an diese Adresse
/// (Subject-Prefix "[TEST → original@adresse]"). Leer = Echtbetrieb.
/// </summary>
public class SmtpSetting
{
    public int Id { get; set; }                              // Singleton — immer 1
    public string Host { get; set; } = "";                   // mail.infomaniak.com / smtp.hostfactory.ch / …
    public int Port { get; set; } = 587;                     // 587 STARTTLS / 465 SSL
    public string Username { get; set; } = "";               // typischerweise = FromAddress
    public string PasswordEncrypted { get; set; } = "";      // AES-verschlüsselt, Base64
    public string FromName { get; set; } = "Schaub HR";
    public string FromAddress { get; set; } = "";
    public string? TestRedirectTo { get; set; }              // null/leer = Echtbetrieb
    public string SiteUrl { get; set; } = "https://onecrew.ch/";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int?    UpdatedByUserId { get; set; }
}

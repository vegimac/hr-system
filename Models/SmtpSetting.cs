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

    // ── Rückläufer-Postfach (Walter-Vorgabe 01.09.2026) ───────────────────
    // Bewusst ein EIGENES Postfach (bounce@…) und nicht das HR-Postfach:
    // OneCrew braucht zum Lesen volle Anmeldedaten und hätte damit Zugriff
    // auf die gesamte HR-Korrespondenz. Im Bounce-Postfach liegen dagegen
    // ausschliesslich automatische Zustellmeldungen.

    /// <summary>
    /// Adresse, die als Rücksendeadresse (Return-Path) auf jeder Mail steht.
    /// NICHT der sichtbare Absender — der bleibt FromAddress. Leer = alles
    /// wie bisher, Rückläufer gehen an FromAddress.
    /// </summary>
    public string? BounceAddress { get; set; }

    /// <summary>IMAP-Server des Rückläufer-Postfachs, z.B. mail.hostfactory.ch.</summary>
    public string? BounceImapHost { get; set; }

    /// <summary>993 = SSL (Normalfall), 143 = STARTTLS.</summary>
    public int BounceImapPort { get; set; } = 993;

    /// <summary>Anmeldename, meist gleich wie BounceAddress.</summary>
    public string? BounceImapUser { get; set; }

    /// <summary>AES-verschlüsselt, Base64 — wie das SMTP-Passwort.</summary>
    public string? BounceImapPasswordEncrypted { get; set; }

    /// <summary>
    /// Schalter für den automatischen Abruf. Aus = OneCrew fasst das
    /// Postfach nicht an; der Knopf «Jetzt abrufen» bleibt trotzdem nutzbar,
    /// damit man vor dem Scharfschalten testen kann.
    /// </summary>
    public bool BounceAbrufAktiv { get; set; } = false;

    /// <summary>Letzter erfolgreicher Abruf — steht in der Maske als Kontrolle.</summary>
    public DateTime? BounceLetzterAbruf { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int?    UpdatedByUserId { get; set; }
}

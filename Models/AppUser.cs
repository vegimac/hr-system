namespace HrSystem.Models;

public class AppUser
{
    public int Id { get; set; }

    public string Username { get; set; } = "";

    /// <summary>Vorname</summary>
    public string? FirstName { get; set; }

    /// <summary>Nachname</summary>
    public string? LastName { get; set; }

    public string Email { get; set; } = "";

    /// <summary>Telefon / Mobile</summary>
    public string? Phone { get; set; }

    public string PasswordHash { get; set; } = "";

    /// <summary>admin | superuser | user</summary>
    public string Role { get; set; } = "user";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Letzter erfolgreicher Login (UTC). NULL = noch nie eingeloggt.</summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>UI-Theme-Präferenz: "light" (Default) oder "dark".</summary>
    public string Theme { get; set; } = "light";

    /// <summary>
    /// Bevorzugte UI-Sprache: "de" (Default) oder "en". Wird beim Login an
    /// das Frontend zurückgegeben — der Flag-Toggle in der Top-Bar
    /// überschreibt den Wert sessionweise; explizites Speichern persistiert
    /// die neue Wahl in dieses Feld.
    /// </summary>
    public string PreferredLanguage { get; set; } = "de";

    /// <summary>
    /// Persönliche Startseite beim Anmelden (Walter 14.08.2026):
    /// dashboard | todos | mitarbeiter | lohn | manager-dienstplan.
    /// NULL = Dashboard (bisheriges Verhalten). Pflege im
    /// «⚙ Meine Einstellungen»-Dialog im Sidebar-Footer.
    /// </summary>
    public string? StartPage { get; set; }

    /// <summary>
    /// Unterschrift als Bild (PNG/JPG-Bytes). Wird auf Formularen eingebettet
    /// (z.B. QST-Anmeldung, RAV-Zwischenverdienst). NULL = keine Unterschrift
    /// hinterlegt; Formulare bleiben dann an dieser Stelle leer.
    /// </summary>
    public byte[]? SignaturePng { get; set; }

    /// <summary>
    /// Mitglied vom HR-Team. Wer true gesetzt ist, sieht das gemeinsame
    /// HR-Postfach (z.B. Pat Wackernagel, Treuhänder). Wirkt zusätzlich zur
    /// `Role` — die HR-Team-Mitgliedschaft ist davon unabhängig.
    /// </summary>
    public bool IsHrTeam { get; set; } = false;

    /// <summary>
    /// Zugriff auf die Filial-Dokumente (Policen/AHV/QST) — pro Benutzer
    /// vergeben (Walter 06.08.2026); admin immer.
    /// </summary>
    public bool CanCompanyDokumente { get; set; } = false;

    /// <summary>
    /// Empfänger des täglichen Mirus-Änderungsdigests (Walter 23.07.2026).
    /// Jeden Morgen 06:00 Europe/Zurich: Mail mit lohnkritischen OneCrew-
    /// Änderungen der letzten 24 h (Stammdaten/Vertrag/Bank/QST/…), gefiltert
    /// auf die Filialen des Users. Unabhängig von Role/IsHrTeam.
    /// </summary>
    public bool ReceivesMirusChangeDigest { get; set; } = false;

    /// <summary>
    /// Super-Admin (Walter-Vorgabe 15.05.2026): Schutzstatus oberhalb von admin.
    /// Wirkt UNABHÄNGIG von der Role:
    ///   • Kann NIEMALS gelöscht werden (auch nicht vom Super-Admin selbst).
    ///   • Nur Super-Admins dürfen Administratoren löschen.
    /// Wird ausschliesslich per SQL/Seed gesetzt, NIE über die API.
    /// </summary>
    public bool IsSuperAdmin { get; set; } = false;

    /// <summary>
    /// Sichtbare Programm-Bereiche pro Benutzer (Walter-Vorgabe 28.06.2026),
    /// komma-separierte Liste der Menü-/Bereichs-Schlüssel:
    /// mitarbeiter, posteingang, auswertungen, roster-absence-import, lohn,
    /// hr-hub, fibu, admin-hub. „dashboard" ist immer sichtbar (nicht gelistet).
    ///   • NULL  = noch nicht definiert → bestehende Rollen-Sichtbarkeit greift.
    ///   • ""    = sieht nur das Dashboard (alle 8 Bereiche abgewählt).
    /// Steuert aktuell die MENÜ-/Anzeige-Sichtbarkeit; harte Server-Sperre pro
    /// Bereich folgt als eigener Schritt.
    /// </summary>
    public string? AllowedAreas { get; set; }

    /// <summary>
    /// Verknüpfung zum Mitarbeiter-Datensatz bei MA-Postfach-Accounts
    /// (Rolle "employee"). NULL bei klassischen Backoffice-Usern (admin/
    /// superuser/user). Pro MA max. ein Account (UNIQUE-Index).
    /// </summary>
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>
    /// TRUE = der User muss beim nächsten Login ein neues Passwort setzen.
    /// Wird gesetzt nach Account-Erstellung (Initial-Passwort) und nach
    /// Admin-Passwort-Reset.
    /// </summary>
    public bool MustChangePassword { get; set; } = false;

    /// <summary>Anzahl aufeinanderfolgender Login-Fehler. Reset nach erfolgreichem Login.</summary>
    public int FailedLoginCount { get; set; } = 0;

    /// <summary>NULL = Account aktiv. Sonst gesperrt bis zu diesem Zeitpunkt (UTC).</summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>
    /// Benutzerbezogene Sicherheits-Policy (Walter-Vorgabe 21.06.2026).
    /// Automatischer Logout nach X Minuten Inaktivität. NULL = Rollen-Default
    /// (employee 15, alle übrigen 30). Gültiger Bereich 5–1440.
    /// </summary>
    public int? IdleTimeoutMinutes { get; set; }

    /// <summary>
    /// Maximale Session-Dauer in Minuten ab Login. NULL = Rollen-Default
    /// (employee 30, alle übrigen 480). Bestimmt zugleich die JWT-Ablaufzeit.
    /// Gültiger Bereich 5–1440.
    /// </summary>
    public int? MaxSessionMinutes { get; set; }

    /// <summary>
    /// Admin-Abmeldung (Walter 04.09.2026): alle Tokens mit login_at vor diesem
    /// Zeitpunkt (UTC) sind ungültig → 401 beim nächsten Zugriff. NULL = kein
    /// Sperrvermerk. Wird unter System › Aktive Sitzungen gesetzt.
    /// </summary>
    public DateTime? SessionRevokedBefore { get; set; }

    public List<UserBranchAccess> BranchAccess { get; set; } = new();

    /// <summary>Anzeigename: Vor- + Nachname, Fallback: Username</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName)
            ? Username
            : $"{FirstName} {LastName}".Trim();
}

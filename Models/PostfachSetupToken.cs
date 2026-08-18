namespace HrSystem.Models;

/// <summary>
/// Einmal-Token für Onboarding / Passwort-Reset des MA-Postfachs (Walter 01.07.2026).
/// HR erzeugt einen QR-Code / Link; der MA öffnet ihn, setzt sein Passwort und
/// wird direkt eingeloggt. Der Klartext-Token steht NUR im Link — in der DB liegt
/// nur der SHA-256-Hash. Einmal verwendbar (UsedAt) und zeitlich begrenzt (ExpiresAt).
/// </summary>
public class PostfachSetupToken
{
    public int Id { get; set; }

    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }

    /// <summary>SHA-256-Hex des Einmal-Tokens.</summary>
    public string TokenHash { get; set; } = "";

    /// <summary>„onboarding" | „reset".</summary>
    public string Purpose { get; set; } = "onboarding";

    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }

    /// <summary>Erster Aufruf der Setup-Seite (= «Link geöffnet», Walter 18.08.2026).</summary>
    public DateTime? OpenedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }
}

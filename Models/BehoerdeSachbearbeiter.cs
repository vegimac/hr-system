namespace HrSystem.Models;

/// <summary>
/// Sachbearbeiter im Stamm einer Behörde (Walter 02.08.2026).
/// Zahlung (IBAN) bleibt an der Behörde; Korrespondenz/Lohnausweis-Mail
/// geht an den am MA gewählten Sachbearbeiter (E-Mail/Telefon).
/// </summary>
public class BehoerdeSachbearbeiter
{
    public int Id { get; set; }
    public int BehoerdeId { get; set; }

    /// <summary>Name, z.B. «Jana Hrdinka».</summary>
    public string Name { get; set; } = "";

    /// <summary>Funktion/Rolle, z.B. «Sachbearbeiterin».</summary>
    public string? Rolle { get; set; }

    public string? Telefon { get; set; }
    public string? Handy { get; set; }
    public string? Email { get; set; }

    /// <summary>Erreichbarkeit als Freitext, z.B. «Mo–Fr 08:00–11:45».</summary>
    public string? Erreichbarkeit { get; set; }

    public string? Bemerkung { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public Behoerde? Behoerde { get; set; }
}

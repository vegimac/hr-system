namespace HrSystem.Models;

/// <summary>
/// Erfasste Zulagen oder Abzüge pro Mitarbeiter und Lohnperiode.
/// Der Typ wird direkt durch eine Lohnposition definiert (kein separates LohnZulagTyp mehr).
/// </summary>
public class LohnZulage
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }

    /// <summary>Format: YYYY-MM, z.B. "2026-03"</summary>
    public string Periode { get; set; } = "";

    /// <summary>Referenz auf die Lohnposition (Typ ZULAGE oder ABZUG)</summary>
    public int LohnpositionId { get; set; }

    /// <summary>CHF-Betrag (immer positiv gespeichert; ob Zulage oder Abzug bestimmt Lohnposition.Typ)</summary>
    public decimal Betrag { get; set; }

    /// <summary>Optionale Bemerkung, z.B. "312 km × CHF 0.70"</summary>
    public string? Bemerkung { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Employee? Employee { get; set; }
    public Lohnposition? Lohnposition { get; set; }
}

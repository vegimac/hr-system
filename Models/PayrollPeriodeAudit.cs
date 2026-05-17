namespace HrSystem.Models;

/// <summary>
/// Audit-Eintrag für jeden Status-Übergang einer Lohnperiode. Macht den
/// Lohnlauf-Prozess revisionssicher: wer hat wann welchen Schritt gemacht,
/// und mit welcher Bemerkung.
/// </summary>
public class PayrollPeriodeAudit
{
    public int Id { get; set; }

    public int PayrollPeriodeId { get; set; }

    /// <summary>FK auf app_user (kann null sein wenn User später gelöscht wurde).</summary>
    public int? UserId { get; set; }

    /// <summary>Klartext-Name des Users zur Lauf-Zeit (denormalisiert für Historie).</summary>
    public string UserName { get; set; } = "";

    /// <summary>
    /// Aktion-Code, einer von:
    ///   PROVISORISCH_ABGESCHLOSSEN
    ///   AN_GF_GESENDET
    ///   ZURUECK_AN_GF
    ///   DEFINITIV_ABGESCHLOSSEN
    ///   WIEDER_GEOEFFNET
    /// </summary>
    public string Action { get; set; } = "";

    /// <summary>Optionale Bemerkung (z.B. „Lohn von Müller korrigiert").</summary>
    public string? Bemerkung { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public PayrollPeriode? PayrollPeriode { get; set; }
}

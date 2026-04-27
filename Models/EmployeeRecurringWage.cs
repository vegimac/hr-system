namespace HrSystem.Models;

/// <summary>
/// Wiederkehrende Zulage oder Abzug pro Mitarbeiter mit Gültigkeitszeitraum.
/// Wird bei jedem Lohnlauf automatisch berücksichtigt, solange die Periode
/// innerhalb [ValidFrom, ValidTo] liegt (ValidTo = NULL heisst unbefristet).
///
/// Typische Anwendungsfälle: Fahrzeugzulage, Handy-Pauschale, Parkplatz-Abzug.
///
/// Im Gegensatz zur einmaligen <see cref="LohnZulage"/> (pro Periode erfasst)
/// lebt diese hier als Stammdatum am Mitarbeiter — Single Source of Truth.
/// </summary>
public class EmployeeRecurringWage
{
    public int      Id             { get; set; }
    public int      EmployeeId     { get; set; }

    /// <summary>Referenz auf die Lohnposition (Typ ZULAGE oder ABZUG).</summary>
    public int      LohnpositionId { get; set; }

    /// <summary>CHF-Betrag pro Periode (immer positiv).</summary>
    public decimal  Betrag         { get; set; }

    /// <summary>Erster Tag der Gültigkeit (inklusiv).</summary>
    public DateOnly ValidFrom      { get; set; }

    /// <summary>Letzter Tag der Gültigkeit (inklusiv). NULL = unbefristet.</summary>
    public DateOnly? ValidTo       { get; set; }

    /// <summary>Optionale Bemerkung, z. B. "Dienstwagen Skoda Octavia".</summary>
    public string?  Bemerkung      { get; set; }

    public DateTime CreatedAt      { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt      { get; set; } = DateTime.UtcNow;

    public Employee?     Employee      { get; set; }
    public Lohnposition? Lohnposition  { get; set; }
}

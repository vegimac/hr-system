namespace HrSystem.Models;

/// <summary>
/// Pro Vertragstyp (FIX / FIX-M / MTP / UTP) eine Zuordnung zu einer
/// Lohnposition mit optionalem Default-Parameter (z.B. Prozentsatz).
///
/// Definiert, welche Kern-Komponenten bei welchem Vertragstyp anfallen —
/// z.B. MTP hat "Festlohn", "Zusatzstunden", "Feiertagsentschädigung",
/// "Ferienentschädigung" und "13. ML"; UTP hat "Stundenlohn",
/// "Stundenlohn Feiertage", "Ferienentschädigung" und "13. ML".
///
/// Phase 1 (heute): Stammdaten + Admin-UI.
/// Phase 2 (später): PayrollController liest diese Tabelle statt
/// hardcoded zu rechnen.
/// </summary>
public class EmploymentModelComponent
{
    public int Id { get; set; }

    /// <summary>Vertragstyp-Code: FIX | FIX-M | MTP | UTP</summary>
    public string EmploymentModelCode { get; set; } = "";

    /// <summary>FK auf Lohnposition (z.B. "10.1 Festlohn")</summary>
    public int LohnpositionId { get; set; }

    /// <summary>
    /// Default-Prozentsatz für diese Komponente (z.B. 3.595 für
    /// Feiertagsentschädigung UTP, 8.33 für 13. ML). NULL = kein Satz,
    /// Betrag wird aus anderer Quelle bestimmt (z.B. Stundenlohn * Stunden).
    /// </summary>
    public decimal? Rate { get; set; }

    public bool IsActive { get; set; } = true;
    public int  SortOrder { get; set; } = 99;

    /// <summary>Optionale Bemerkung / Erklärung (wird im Admin-UI angezeigt).</summary>
    public string? Bemerkung { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Lohnposition? Lohnposition { get; set; }
}

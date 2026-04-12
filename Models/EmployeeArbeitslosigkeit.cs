namespace HrSystem.Models;

/// <summary>
/// Erfasst Phasen der Arbeitslosenmeldung eines Mitarbeiters.
/// Wird für die Bescheinigung über Zwischenverdienst (ALV-Formular 716.105) benötigt.
/// </summary>
public class EmployeeArbeitslosigkeit
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>Datum der Anmeldung beim RAV</summary>
    public DateOnly AngemeldetSeit { get; set; }

    /// <summary>Datum der Abmeldung (null = noch aktiv arbeitslos gemeldet)</summary>
    public DateOnly? AbgemeldetAm { get; set; }

    /// <summary>RAV-Stelle, z.B. "RAV Luzern" oder "RAV Willisau"</summary>
    public string? RavStelle { get; set; }

    /// <summary>Kundennummer beim RAV (optional)</summary>
    public string? RavKundennummer { get; set; }

    /// <summary>Zuständige Arbeitslosenkasse (UNIA, Umschu etc.)</summary>
    public string? Arbeitslosenkasse { get; set; }

    public string? Bemerkung { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

namespace HrSystem.Models;

public class Absence
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }

    /// <summary>KRANK | UNFALL | SCHULUNG | FERIEN</summary>
    public string AbsenceType { get; set; } = "";

    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo   { get; set; }

    /// <summary>
    /// JSON-Array der angerechneten Tage (yyyy-MM-dd).
    /// KRANK/UNFALL/SCHULUNG: vom User ausgewählte Arbeitstage.
    /// FERIEN: alle Tage der Periode.
    /// </summary>
    public string? WorkedDays { get; set; }

    /// <summary>Berechnete Stunden (immer positiv gespeichert, inkl. Prozent-Reduktion).</summary>
    public decimal HoursCredited { get; set; }

    /// <summary>
    /// Ausfall-Prozent (1–100). 100 = voll krank/abwesend, 50 = halb krank etc.
    /// Wird auf Stunden- und Lohn-Berechnung multiplikativ angewendet.
    /// </summary>
    public decimal Prozent { get; set; } = 100;

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Employee? Employee { get; set; }
}

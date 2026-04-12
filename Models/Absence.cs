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

    /// <summary>Berechnete Stunden (immer positiv gespeichert).</summary>
    public decimal HoursCredited { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Employee? Employee { get; set; }
}

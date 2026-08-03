namespace HrSystem.Models;

/// <summary>
/// Uniformen-Depot pro MA (Walter Aug 2026).
/// Beim 1. Lohn: CHF 50 Abzug → Status EINBEHALTEN (Geld bleibt als Depot
/// beim MA). Bei Austritt mit bestätigter Rückgabe → Rückerstattung auf dem
/// Lohnzettel und Status ZURUECKBEZAHLT. Ohne ordentliche Rückgabe →
/// VERFALLEN (kein Refund, Depot = 0).
/// Backfill: Eintritt vor 01.07.2026 → EINBEHALTEN ohne Lohn-Abzug
/// (historisch bereits in Mirus abgezogen).
/// </summary>
public class EmployeeUniformDepot
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    /// <summary>Aktueller Depot-Saldo (0 oder 50).</summary>
    public decimal Balance { get; set; }

    /// <summary>
    /// EINBEHALTEN | ZURUECKBEZAHLT | VERFALLEN
    /// </summary>
    public string Status { get; set; } = "EINBEHALTEN";

    /// <summary>Periode des Abzugs (YYYY-MM) oder «BACKFILL».</summary>
    public string? ChargedPeriode { get; set; }

    /// <summary>Periode der Rückerstattung (YYYY-MM), falls erfolgt.</summary>
    public string? RefundPeriode { get; set; }

    /// <summary>
    /// Austritts-Entscheidung: true = Uniform zurück → Refund;
    /// false = nicht ordentlich → Verfall; null = noch nicht entschieden.
    /// </summary>
    public bool? ReturnConfirmed { get; set; }

    public DateTime? ReturnConfirmedAt { get; set; }
    public int?      ReturnConfirmedBy { get; set; }

    public string? Bemerkung { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public Employee? Employee { get; set; }
}

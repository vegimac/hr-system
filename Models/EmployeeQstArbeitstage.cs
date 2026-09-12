namespace HrSystem.Models;

/// <summary>
/// Arbeitstage pro Lohnmonat für die Quellensteuer bei Wohnsitz im Ausland
/// (Grenzgänger, internationale Wochenaufenthalter) — Walter 11.09.2026.
/// Steuerbar in der Schweiz ist nur der Anteil der in der Schweiz geleisteten
/// Arbeitstage: QST-Basis = Bruttolohn × TageCh / TageEffektiv. Der Satz wird
/// weiterhin vom vollen satzbestimmenden Lohn genommen.
/// Swissdec: PersonEffectiveWorkingDays / PersonWorkingDaysCH (TF28 Arbenz:
/// 15 von 20 Tagen → 4'500 von 6'000 steuerbar, Satz auf 8'000).
/// Ohne Eintrag oder bei Wohnsitz CH: voller Lohn steuerbar (wie bisher).
/// </summary>
public class EmployeeQstArbeitstage
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    /// <summary>Effektive Arbeitstage im Monat (alle).</summary>
    public decimal TageEffektiv { get; set; }
    /// <summary>Davon in der Schweiz geleistete Arbeitstage.</summary>
    public decimal TageCh { get; set; }
    public string? Bemerkung { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public Employee? Employee { get; set; }
}

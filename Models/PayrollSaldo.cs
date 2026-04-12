namespace HrSystem.Models;

/// <summary>
/// Monatlicher Saldo pro Mitarbeiter: Über-/Fehlstunden, Nacht-Zeitzuschlag, 13. ML.
/// </summary>
public class PayrollSaldo
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int CompanyProfileId { get; set; }

    public int PeriodYear  { get; set; }
    public int PeriodMonth { get; set; }

    /// <summary>
    /// MTP-Stundensaldo am Ende dieser Periode (negativ = Fehlstunden).
    /// Wird in die nächste Periode übertragen.
    /// </summary>
    public decimal HourSaldo { get; set; } = 0;

    /// <summary>
    /// Nacht-Zeitzuschlag-Saldo: akkumulierte Kompensationsstunden aus Nachtarbeit (10% je Nachtstunde).
    /// Wird monatlich weitergegeben; sinkt wenn NACHT_KOMP-Absenzen eingetragen werden.
    /// </summary>
    public decimal NachtSaldo { get; set; } = 0;

    /// <summary>In dieser Periode aufgelaufene Nachtstunden (nur Info)</summary>
    public decimal NightHoursWorked { get; set; } = 0;

    /// <summary>Monatliche Rückstellung 13. ML</summary>
    public decimal ThirteenthMonthMonthly { get; set; } = 0;

    /// <summary>Kumulierter 13. ML seit Jahresbeginn</summary>
    public decimal ThirteenthMonthAccumulated { get; set; } = 0;

    /// <summary>
    /// Akkumuliertes Ferien-Guthaben in CHF (nur UTP/MTP).
    /// Ferienentschädigung % wird nicht monatlich ausbezahlt, sondern hier angesammelt.
    /// Wird bei effektivem Ferienbezug proportional ausbezahlt.
    /// </summary>
    public decimal FerienGeldSaldo { get; set; } = 0;

    /// <summary>
    /// Ferien-Tage-Saldo (alle Vertragsmodelle).
    /// +1/12 der Jahresferientage pro Monat; −Tage bei FERIEN-Absenz.
    /// 5 Wochen = 35 Tage/Jahr → +2.9167/Monat; 6 Wochen = 42 Tage/Jahr → +3.5/Monat
    /// </summary>
    public decimal FerienTageSaldo { get; set; } = 0;

    /// <summary>Bruttolohn dieser Periode</summary>
    public decimal GrossAmount { get; set; } = 0;

    /// <summary>Nettolohn dieser Periode</summary>
    public decimal NetAmount { get; set; } = 0;

    /// <summary>"draft" oder "finalized"</summary>
    public string Status { get; set; } = "draft";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Employee? Employee { get; set; }
}

namespace HrSystem.Models;

/// <summary>
/// Ferien auszahlen ohne Bezug (Walter-Vorgabe 05.10.2026). Wie die
/// Ferienkürzung bewusst KEINE Absence-Zeile (kein Abwesenheitstag) — wirkt
/// nur im Lohnlauf-Monat von <see cref="Datum"/>:
///   • Art TAGE: <see cref="Tage"/> auszahlen, Geld im Verhältnis aus dem Topf.
///   • Art VORJAHR: Saldo per 31.12. Vorjahr (Tage + Topf) auszahlen.
/// Rechnung: <see cref="HrSystem.Services.FerienAuszahlungRechnung"/>.
/// </summary>
public class FerienAuszahlungEintrag
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    /// <summary>Buchungsdatum: bestimmt den Lohnlauf-Monat (muss offen sein).</summary>
    public DateOnly Datum { get; set; }
    public string Art { get; set; } = "TAGE";
    /// <summary>Nur bei Art TAGE.</summary>
    public decimal? Tage { get; set; }
    public string? Bemerkung { get; set; }
    public string? ErstelltVon { get; set; }
    /// <summary>Lokalzeit (timestamp without time zone).</summary>
    public DateTime ErstelltAm { get; set; } = DateTime.Now;
}

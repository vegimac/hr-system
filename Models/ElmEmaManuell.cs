namespace HrSystem.Models;

/// <summary>
/// EMA-Ereignis, das RL 8.6.8 dem Anwender überlässt: Arbeitsunterbruch, Teilpensionierung,
/// Arbeitsunfähigkeit (Walter 04.11.2026). Art «Fit» trägt die Antwort auf die Frage nach der
/// 100-%-Arbeitsfähigkeit zu einem Eintritt oder Austritt (RL 8.6.8.2): Grund «ja» oder «nein».
/// </summary>
public class ElmEmaManuell
{
    public const string ArtFit = "Fit";

    public int Id { get; set; }
    public int EmployeeId { get; set; }
    /// <summary>Erster des Monats, in dem das Ereignis gemeldet wird.</summary>
    public DateOnly Monat { get; set; }
    /// <summary>Entry, Mutation, Withdrawal oder Fit.</summary>
    public string Art { get; set; } = "";
    public string Grund { get; set; } = "";
    /// <summary>Leer = Regel des Ereignisses (Mutation am 1. des Monats). Bei Fit das Datum des Ereignisses.</summary>
    public DateOnly? ValidAsOf { get; set; }
    public DateTime ErfasstAm { get; set; } = DateTime.Now;
    public int? ErfasstVonUserId { get; set; }
    public string? ErfasstVon { get; set; }
}

namespace HrSystem.Models;

/// <summary>
/// Zivilstand-Historie (Walter 04.09.2026): Zivilstand mit Gültig-ab —
/// analog Wohnort-Historie. Speist die QST-Erfassung zum Stichtag (ein
/// Alt-Eintrag «damals verheiratet» braucht den damaligen Zivilstand) und
/// die Herleitung. Wird beim Zivilstand-Wechsel (easy@work-Sync, MA-Maske)
/// automatisch nachgeführt; HR kann Einträge ergänzen/korrigieren.
/// GueltigAb = null bedeutet «seit jeher» (Anfangsstand).
/// </summary>
public class EmployeeZivilstandHistory
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    /// <summary>ledig | verheiratet | geschieden | verwitwet | getrennt | eingetragene_partnerschaft …</summary>
    public string Zivilstand { get; set; } = "";
    public DateOnly? GueltigAb { get; set; }
    public string? Bemerkung { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

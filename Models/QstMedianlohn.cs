namespace HrSystem.Models;

/// <summary>
/// Median der effektiven Lohneinkünfte pro Kanton und Monat.
/// Untergrenze für den satzbestimmenden Lohn, wenn weitere Arbeitgeber
/// oder Ersatzeinkünfte erfasst sind, Betrag und Beschäftigungsgrad
/// beide unbekannt sind und der eigene Grad nicht ermittelt werden kann.
/// </summary>
public class QstMedianlohn
{
    public int Id { get; set; }
    public string Kanton { get; set; } = "";
    public decimal Betrag { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
}

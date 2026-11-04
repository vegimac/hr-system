namespace HrSystem.Models;

/// <summary>
/// Aufteilung eines negativen AHV-Lohnes (Richtlinien 8.7.5.3): Betrag und Periode des Vorjahres,
/// den Anteil des laufenden Jahres ergibt die Meldung. Ist die Aufteilung nicht möglich, steht das
/// Datum der manuellen Mitteilung an die Ausgleichskasse.
/// </summary>
public class EmployeeAhvSplit
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int Jahr { get; set; }
    public DateOnly? VorjahrVon { get; set; }
    public DateOnly? VorjahrBis { get; set; }
    public decimal? VorjahrLohn { get; set; }
    public DateOnly? NachlieferungAm { get; set; }
}

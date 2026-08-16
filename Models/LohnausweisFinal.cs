namespace HrSystem.Models;

/// <summary>
/// Finalisierter Jahres-Lohnausweis (Walter-Vorgabe 16.08.2026):
/// Beim ersten «Final erzeugen» werden DocID (echte UUID) und CreationDate
/// EINMALIG vergeben und hier persistiert — jeder spätere Wiederdruck
/// desselben Lohnausweises (MA + Jahr) trägt exakt dieselbe DocID und
/// dasselbe CreationDate (Swissdec: DocID identifiziert das Dokument).
/// Entwürfe erscheinen mit DocID «Entwurf - Brouillon - Bozza» und werden
/// hier NICHT gespeichert.
/// </summary>
public class LohnausweisFinal
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int Year { get; set; }
    public Guid DocId { get; set; }
    public DateTime CreationDate { get; set; } = DateTime.Now;
    public string? CreatedBy { get; set; }

    public Employee? Employee { get; set; }
}

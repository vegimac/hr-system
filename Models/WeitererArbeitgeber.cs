namespace HrSystem.Models;

/// <summary>
/// Weiterer Arbeitgeber eines MA (Walter-Vorgabe 23.09.2026) — vorerst NUR
/// Information für die Checkliste Personaladministration («Erlaubnis
/// Hauptarbeitgeber»). Bewusst NICHT mit der Quellensteuer verbunden
/// (EmployeeQuellensteuer.WeitereAg*/GesamtpensumWeitereAg bleiben die Quelle
/// für QST, Lohnlauf und Swissdec) — keine Übernahme, keine Synchronisation.
/// </summary>
public class WeitererArbeitgeber
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Name { get; set; } = "";
    public string? Strasse { get; set; }
    public string? Plz { get; set; }
    public string? Ort { get; set; }
    public string? Kanton { get; set; }
    public string? Land { get; set; }
    public decimal? PensumProzent { get; set; }
    public decimal? StundenProWoche { get; set; }
    public DateOnly? GueltigVon { get; set; }
    public DateOnly? GueltigBis { get; set; }
    /// <summary>true = DIESER andere Arbeitgeber ist Hauptarbeitgeber (wir = Nebenerwerb).</summary>
    public bool IstHauptarbeitgeber { get; set; }
    /// <summary>Erlaubnis des Hauptarbeitgebers als verknüpftes Dokument.</summary>
    public int? ErlaubnisDokumentId { get; set; }
    public string? Bemerkung { get; set; }
    /// <summary>Lokalzeit (timestamp without time zone).</summary>
    public DateTime ErstelltAm { get; set; } = DateTime.Now;
}

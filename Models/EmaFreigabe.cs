namespace HrSystem.Models;

/// <summary>
/// Freigabe einer vorbereiteten EMA-Zeile (BVG oder Quellensteuer) zur Übermittlung.
/// Die ELM-Datei selbst ist Etappe E7. Hier gibt der Anwender die erkannte Zeile frei.
/// </summary>
public class EmaFreigabe
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int CompanyProfileId { get; set; }
    /// <summary>BVG oder QST.</summary>
    public string Domaene { get; set; } = "";
    public string Grund { get; set; } = "";
    public DateOnly Stichtag { get; set; }
    public DateTime ErfasstAm { get; set; } = DateTime.Now;
    public int? ErfasstVonUserId { get; set; }
    public string? ErfasstVon { get; set; }
}

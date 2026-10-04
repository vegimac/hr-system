namespace HrSystem.Models;

/// <summary>
/// Lohnkonto aus dem Vorsystem (Mirus), ein Betrag pro MA / Filiale / Monat / Lohnart.
/// Grundlage für den Vergleich OneCrew ↔ Mirus und für unterjährige Starts
/// (Lohnausweis, Jahresausgleich, KTG-Schnitt brauchen die Monate vor OneCrew).
/// Code = Mirus-Nummer «Nr.Sub» (z.B. 250.1 Bruttolohn), Sektion AN = Lohnzettel,
/// AG = Arbeitgeber-Beiträge, Tage und Saldi.
/// </summary>
public class VorsystemLohnkonto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int CompanyProfileId { get; set; }
    public int Jahr { get; set; }
    public int Monat { get; set; }
    public string Sektion { get; set; } = "AN";
    public string Code { get; set; } = "";
    public string Bezeichnung { get; set; } = "";
    public decimal Betrag { get; set; }
    public string Quelle { get; set; } = "MIRUS";
    public string? Dateiname { get; set; }
    public DateTime ImportiertAm { get; set; } = DateTime.Now;
    public string? ImportiertVon { get; set; }
}

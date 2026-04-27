namespace HrSystem.Models;

/// <summary>
/// Schweizer Bank-Stammdaten (SIX Interbank Clearing Bank-Master-Liste).
/// Pro Institut-ID (IID, Stellen 5–9 einer CH/LI-IBAN) ein Eintrag.
/// Wird per CSV-Upload über Admin-UI aktualisiert.
/// </summary>
public class BankMaster
{
    /// <summary>5-stellige Institut-ID. Primary Key.</summary>
    public string   Iid        { get; set; } = "";
    public string?  Bic        { get; set; }
    public string   Name       { get; set; } = "";
    public string?  Ort        { get; set; }
    public string?  Strasse    { get; set; }
    public string?  Plz        { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}

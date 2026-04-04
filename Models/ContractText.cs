namespace HrSystem.Models;

/// <summary>
/// Mehrsprachige Vertragstexte aus der Excel Parameters-Tabelle.
/// TextKey = Zeilennummer aus Excel (E-Spalte), z.B. "15", "17", "48"
/// ContractTypes = kommasepariert: "ALL", "FIX", "MTP", "UTP", "FIX-M"
/// LanguageCode = "de" | "fr" | "it"
/// </summary>
public class ContractText
{
    public int      Id            { get; set; }
    public string   TextKey       { get; set; } = "";
    public string   ContractTypes { get; set; } = "ALL";
    public string   LanguageCode  { get; set; } = "de";
    public string   Content       { get; set; } = "";
    public bool     IsActive      { get; set; } = true;
    public DateTime ValidFrom     { get; set; } = new DateTime(2026, 1, 1);
    public DateTime? ValidTo      { get; set; }
}

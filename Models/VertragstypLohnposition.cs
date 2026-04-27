namespace HrSystem.Models;

/// <summary>
/// Mapping: welche Lohnpositionen gehören standardmässig zu welchem
/// Vertragstyp (FIX, FIX-M, MTP, UTP). Wird beim Anlegen eines neuen
/// Vertrags genutzt, um die Standard-Positionen automatisch dem
/// Mitarbeiter zuzuordnen (EmployeeLohnAssignment).
/// </summary>
public class VertragstypLohnposition
{
    public int Id { get; set; }

    /// <summary>'FIX' | 'FIX-M' | 'MTP' | 'UTP'</summary>
    public string VertragstypCode { get; set; } = "";

    /// <summary>Lohnposition-Code (z.B. "10" Festlohn, "70" Krankheit).</summary>
    public string LohnpositionCode { get; set; } = "";

    /// <summary>
    /// true = Position kann nicht entfernt werden (Pflicht-Position für
    /// dieses Modell, z.B. Festlohn bei FIX, Stundenlohn bei UTP).
    /// </summary>
    public bool IsRequired { get; set; } = false;

    /// <summary>
    /// true = Position wird beim Anlegen eines neuen Vertrags automatisch
    /// dem Mitarbeiter zugeordnet (default aktiv).
    /// </summary>
    public bool IsDefaultActive { get; set; } = true;

    public int SortOrder { get; set; } = 99;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

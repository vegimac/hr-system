namespace HrSystem.Models;

public class JobGroup
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Kader-Funktion: bekommt FIX-M (Kaderversicherung mit BVG-Zusatz)
    /// bei Fix-Verträgen. Sonst FIX (normaler Festlohn).
    /// </summary>
    public bool IsKader { get; set; }

    /// <summary>
    /// Kommaseparierte Liste der Mirus-CSV-Funktion-Strings, die auf
    /// diese Job-Gruppe gemappt werden (case-insensitive).
    /// </summary>
    public string? MirusFunktionAliases { get; set; }
}
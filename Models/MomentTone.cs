namespace HrSystem.Models;

/// <summary>
/// Emotionsgrad einer Moment-Vorlage (Walter-Vorgabe 01.07.2026),
/// z.B. schlicht, herzlich, sehr persönlich, kurz. Zusammen mit dem
/// <see cref="MomentType"/> bildet er die Kombination, für die in
/// <see cref="MomentText"/> Texte hinterlegt werden.
/// </summary>
public class MomentTone
{
    public int Id { get; set; }

    /// <summary>Technischer Code, z.B. „herzlich".</summary>
    public string Code { get; set; } = "";

    /// <summary>Anzeigename, z.B. „Herzlich".</summary>
    public string Name { get; set; } = "";

    /// <summary>Kurzbeschreibung des Emotionsgrads.</summary>
    public string? Description { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

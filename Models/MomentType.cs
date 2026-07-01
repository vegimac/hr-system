namespace HrSystem.Models;

/// <summary>
/// Ein Moment-Typ (Walter-Vorgabe 01.07.2026). Datengetrieben statt hartcodiert,
/// damit die zugelassenen Momente pflegbar sind. <see cref="ConsentCategory"/>
/// steuert das Freigabe-Enforcement (birthday | appreciation | care) und muss
/// zu einer der Unterkategorien in <see cref="EmployeeMomentConsent"/> passen.
/// </summary>
public class MomentType
{
    public int Id { get; set; }

    /// <summary>Technischer Code, z.B. „EmployeeBirthday". Stabil, wird im Moment gespeichert.</summary>
    public string Code { get; set; } = "";

    /// <summary>Anzeigename, z.B. „Geburtstag".</summary>
    public string Name { get; set; } = "";

    /// <summary>Kurzbeschreibung des Moment-Typs.</summary>
    public string? Description { get; set; }

    /// <summary>Consent-Unterkategorie: birthday | appreciation | care.</summary>
    public string ConsentCategory { get; set; } = "";

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

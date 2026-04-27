namespace HrSystem.Models;

/// <summary>
/// Schweizer PLZ-/Gemeinden-Stammdaten aus dem Amtlichen Ortschaftenverzeichnis
/// der Schweizerischen Post (AMTOVZ). Wird für den PLZ-Lookup beim Mitarbeiter-
/// Stamm verwendet: PLZ eingeben → Gemeinde + Kanton werden vorgeschlagen.
///
/// Pro Kombination (PLZ, BFS-Gemeinde) existiert genau ein Eintrag.
/// Eine PLZ kann mehrere Gemeinden referenzieren (z.B. 8580 → Amriswil,
/// Hefenhofen, Muolen, Salmsach, Sommeri).
/// </summary>
public class SwissLocation
{
    public int     Id             { get; set; }
    public string  Plz4           { get; set; } = "";
    public string  Gemeindename   { get; set; } = "";
    public int     BfsNr          { get; set; }
    public string  Kantonskuerzel { get; set; } = "";
}

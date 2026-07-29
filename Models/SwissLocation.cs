namespace HrSystem.Models;

/// <summary>
/// Schweizer PLZ-/Ortschafts-Stammdaten aus dem Amtlichen Ortschaftenverzeichnis
/// (swisstopo AMTOVZ). PLZ eingeben → Ortschaft + Kanton werden vorgeschlagen.
///
/// Wichtig (Walter 29.07.2026): Adress-Ort = <see cref="Ortschaftsname"/>
/// (Post-Ortschaft, z.B. «Bützberg»), NICHT die politische Gemeinde
/// («Thunstetten»). Pro (PLZ, Ortschaft) genau ein Eintrag; Gemeindename =
/// politische Gemeinde mit dem höchsten Adressenanteil.
/// </summary>
public class SwissLocation
{
    public int     Id             { get; set; }
    public string  Plz4           { get; set; } = "";
    /// <summary>Post-Ortschaft — das ist der Ort in der Postadresse.</summary>
    public string  Ortschaftsname { get; set; } = "";
    /// <summary>Politische Gemeinde (BFS), informativ / höchster Adressenanteil.</summary>
    public string  Gemeindename   { get; set; } = "";
    public int     BfsNr          { get; set; }
    public string  Kantonskuerzel { get; set; } = "";
}

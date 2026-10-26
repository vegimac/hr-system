namespace HrSystem.Models;

/// <summary>
/// Ausland-PLZ der ESTV-Grenzregionen (italienische Grenzgemeinden und weitere).
/// Liegt bewusst nicht in <see cref="SwissLocation"/>: der AMTOVZ-Import leert
/// diese Tabelle bei jedem Laden der Schweizer Gemeinden.
/// </summary>
public class Grenzregion
{
    public int    Id      { get; set; }
    public string Plz     { get; set; } = "";
    public string Ort     { get; set; } = "";
    public string Region  { get; set; } = "";
    /// <summary>ISO-3166 Alpha-2, z.B. IT.</summary>
    public string LandIso { get; set; } = "";
}

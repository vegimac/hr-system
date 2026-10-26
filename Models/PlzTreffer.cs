namespace HrSystem.Models;

/// <summary>
/// Ein Treffer der PLZ-Suche. Kommt entweder aus dem Schweizer
/// Ortschaftenverzeichnis (<see cref="Quelle"/> = ch) oder aus der
/// Grenzregion-Liste (grenz).
/// </summary>
public class PlzTreffer
{
    public string Plz4 { get; set; } = "";
    public string Ortschaftsname { get; set; } = "";
    public string Gemeindename { get; set; } = "";
    public string PolitischeGemeinde { get; set; } = "";
    public int? BfsNr { get; set; }
    public string Kantonskuerzel { get; set; } = "";
    public string Region { get; set; } = "";
    public string Land { get; set; } = "CH";
    public string Quelle { get; set; } = "ch";
}

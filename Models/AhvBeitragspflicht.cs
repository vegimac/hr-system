namespace HrSystem.Models;

/// <summary>
/// Zentrale AHV-Beitragspflicht (Swissdec 8.1.1). Eine Zeile für alle Filialen.
/// Der Lohnlauf liest sie; die Alter-Spalte der SV-Sätze wählt nur die Zeile.
/// </summary>
public class AhvBeitragspflicht
{
    public int Id { get; set; } = 1;
    public int BeginnAlter { get; set; } = 18;
    public int EndeMaenner { get; set; } = 65;
    public int EndeFrauen { get; set; } = 65;
    public DateTime GeaendertAm { get; set; } = DateTime.Now;
}

/// <summary>
/// Übergang AHV 21 für Frauen. Jahrgänge ohne Zeile nehmen das Ende für Frauen.
/// </summary>
public class AhvBeitragspflichtUebergang
{
    public int Id { get; set; }
    public int? JahrgangVon { get; set; }
    public int? JahrgangBis { get; set; }
    public int Jahre { get; set; }
    public int Monate { get; set; }
}

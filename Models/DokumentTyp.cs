namespace HrSystem.Models;

/// <summary>
/// Dokument-Typ innerhalb einer Kategorie.
/// Beispiele für Kategorie "Persönliche Angaben":
///   "Aufenthaltsbewilligung", "Bewerbungsunterlagen", "Mitarbeiterfoto".
/// Entspricht der "Dokumentkategorie" im d.velop-Aktenplan.
/// </summary>
public class DokumentTyp
{
    public int Id { get; set; }
    public int KategorieId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; } = 99;
    public bool Aktiv { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

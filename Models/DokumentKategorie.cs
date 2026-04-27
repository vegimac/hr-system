namespace HrSystem.Models;

/// <summary>
/// Top-Level-Kategorie der Dokumentenverwaltung.
/// Beispiele: "Persönliche Angaben", "Vertragsunterlagen", "Absenzen".
/// Entspricht der "Dokumentenart" im d.velop-Aktenplan.
/// </summary>
public class DokumentKategorie
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; } = 99;
    public bool Aktiv { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

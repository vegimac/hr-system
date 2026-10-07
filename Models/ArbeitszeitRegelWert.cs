namespace HrSystem.Models;

/// <summary>
/// Eingestellter Wert einer Arbeitszeit-Regel (Walter 07.10.2026). Genau eines von
/// HauptsitzId (Vorlage) oder CompanyProfileId (Abweichung der Filiale) ist gesetzt.
/// Fehlt eine Zeile, gilt die Vorlage bzw. der gesetzliche Standard aus
/// <see cref="HrSystem.Services.ArbeitszeitRegelKatalog"/>. Schluessel «aktiv» (Wert 0/1),
/// «text» (eigene Erklärung in Text) oder ein Parameter der Regel.
/// </summary>
public class ArbeitszeitRegelWert
{
    public int Id { get; set; }
    public int? HauptsitzId { get; set; }
    public int? CompanyProfileId { get; set; }
    public string Regel { get; set; } = "";
    public string Schluessel { get; set; } = "";
    public decimal? Wert { get; set; }
    public string? Text { get; set; }
    public DateTime GeaendertAm { get; set; } = DateTime.Now;
    public string? GeaendertVon { get; set; }
}

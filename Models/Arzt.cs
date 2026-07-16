namespace HrSystem.Models;

/// <summary>
/// Ärzte-Verzeichnis (Walter-Vorgabe 16.07.2026): behandelnde Ärztinnen und
/// Ärzte der Mitarbeitenden — z.B. für den «Brief an den behandelnden Arzt»
/// (medizinische Eignungsuntersuchung Mutterschutz). Gepflegt in den
/// Systemeinstellungen; im Mutterschafts-Modul auswählbar.
/// </summary>
public class Arzt
{
    public int Id { get; set; }

    /// <summary>z.B. «Dr. med.»</summary>
    public string? Titel { get; set; }
    public string Vorname { get; set; } = "";
    public string Nachname { get; set; } = "";

    /// <summary>z.B. «Gynäkologie/Geburtshilfe»</summary>
    public string? Fachgebiet { get; set; }

    /// <summary>Praxis/Institution, z.B. «Frauenzentrum Sursee»</summary>
    public string? PraxisName { get; set; }

    public string? Strasse { get; set; }
    public string? Plz { get; set; }
    public string? Ort { get; set; }
    public string? Telefon { get; set; }
    public string? Email { get; set; }
    public string? Bemerkung { get; set; }

    public bool Aktiv { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

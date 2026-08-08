namespace HrSystem.Models;

/// <summary>
/// Wohnort-Historie pro MA (Walter-Vorgabe 07.08.2026): NUR PLZ/Ort/Kanton
/// (nicht die Strasse) mit Gültig-ab — z.B. «Reiden LU» bis 31.07.2026,
/// «Zofingen AG» ab 01.08.2026. Das «bis» ergibt sich implizit aus dem
/// nächsten Eintrag (Gültig-ab − 1 Tag).
///
/// Zweck: Umzugs-ZEITPUNKT festhalten, v.a. für die Quellensteuer — bei
/// Kantonswechsel zahlt der angebrochene Monat noch im ALTEN Kanton, ab dem
/// 1. des Folgemonats gilt der neue (der Umzugs-Endpoint legt automatisch
/// die passende QST-Folge-Version an).
/// </summary>
public class EmployeeWohnortHistory
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public string? Plz { get; set; }
    public string? Ort { get; set; }
    public string? KantonCode { get; set; }

    /// <summary>NULL = «seit jeher» (initialer Eintrag aus der Bestandsadresse).</summary>
    public DateOnly? GueltigAb { get; set; }

    /// <summary>true = Adresse kam aus easy@work, das UMZUGSDATUM ist noch
    /// nicht bestätigt (GueltigAb = Sync-Tag als Platzhalter). Dashboard-ToDo
    /// «Umzugsdatum bestätigen»; die QST-Automatik läuft erst bei Bestätigung.</summary>
    public bool DatumOffen { get; set; }

    public string? Bemerkung { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

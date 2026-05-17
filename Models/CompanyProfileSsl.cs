namespace HrSystem.Models;

/// <summary>
/// SSL-Nummer (Quellensteuer-Schuldner-Nummer, kantonal auch "PersID" genannt)
/// der Filiale für einen bestimmten Kanton.
///
/// In der Schweiz ist die SSL-Nummer kantonal strukturiert: Arbeitgeber müssen
/// sich in jedem Kanton, in dem sie quellensteuerpflichtige Mitarbeitende
/// beschäftigen, separat anmelden und erhalten dort eine eigene Nummer.
///
/// Eine Filiale kann daher mehrere SSL-Nummern haben — typischerweise eine
/// für den eigenen Sitzkanton plus weitere für Kantone, in denen Mitarbeitende
/// wohnen (Wohnsitz-Kanton ist massgeblich für die QST-Abrechnung).
///
/// Eindeutigkeit: pro (Filiale, Kanton) gibt es genau einen Eintrag —
/// erzwungen über UNIQUE-Index auf (company_profile_id, kanton_code).
/// </summary>
public class CompanyProfileSsl
{
    public int Id { get; set; }

    public int CompanyProfileId { get; set; }
    public CompanyProfile? CompanyProfile { get; set; }

    /// <summary>2-Zeichen-Kantonscode (LU, AG, ZH …).</summary>
    public string KantonCode { get; set; } = "";

    /// <summary>SSL-Nummer wie vom kantonalen Steueramt vergeben (z.B. "1773819").</summary>
    public string SslNummer  { get; set; } = "";

    /// <summary>Optionale Bemerkung, z.B. "registriert seit 2023" oder Sachbearbeiter-Hinweis.</summary>
    public string? Bemerkung { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

namespace HrSystem.Models;

/// <summary>
/// Stammdaten: Typen von Zulagen und Abzügen (z.B. Km-Entschädigung, Vorschuss-Rückzahlung).
/// Einmalig konfigurierbar; wird bei der Erfassung von Zulagen/Abzügen pro Mitarbeiter referenziert.
/// </summary>
public class LohnZulagTyp
{
    public int Id { get; set; }

    /// <summary>Bezeichnung, z.B. "Km-Entschädigung", "Vorschuss-Rückzahlung"</summary>
    public string Bezeichnung { get; set; } = "";

    /// <summary>ZULAGE | ABZUG</summary>
    public string Typ { get; set; } = "ZULAGE";

    /// <summary>
    /// true = Betrag fliesst in den AHV/IV/EO/ALV-pflichtigen Lohn (wird zu totalLohn addiert).
    /// false = Betrag wird separat nach Nettoberechnung ausgewiesen.
    /// </summary>
    public bool SvPflichtig { get; set; } = false;

    /// <summary>
    /// true = Betrag wird in die Quellensteuer-Berechnungsbasis einbezogen.
    /// Relevant nur wenn SvPflichtig = true (dann automatisch Teil von totalLohn).
    /// </summary>
    public bool QstPflichtig { get; set; } = false;

    /// <summary>
    /// Optionale Verknüpfung zum Lohnraster (Lohnposition.Code, z.B. "190.1").
    /// Wenn gesetzt → granulare SV-Flags aus Lohnposition werden verwendet.
    /// Wenn null    → Fallback auf binäres SvPflichtig-Flag (Rückwärtskompatibilität).
    /// </summary>
    public string? LohnpositionCode { get; set; }

    public int SortOrder { get; set; } = 99;
    public bool Aktiv { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

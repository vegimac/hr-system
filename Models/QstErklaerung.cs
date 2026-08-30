namespace HrSystem.Models;

/// <summary>
/// Erklär-Baustein zur Quellensteuer (Walter-Vorgabe 30.08.2026).
///
/// Bewusst KEINE KI zur Laufzeit: die Erklärung zu einem Tarif hängt nur an
/// Merkmalen (Tarifbuchstabe, Kinderziffer, Kirchensteuer, Zivilstand,
/// Befreiungsgrund) — nicht an der Person. Die Texte werden EINMAL geschrieben
/// und hier abgelegt; zur Laufzeit setzt der QstErklaerungController die
/// passenden Bausteine zusammen. Damit verlässt kein einziges Personendatum
/// das Haus, es entstehen keine API-Kosten und keine Wartezeit.
///
/// Code-Schema (Bausteintyp.Schlüssel):
///   tarif.A … tarif.Q      — der Tarifbuchstabe
///   kinder.0 / kinder.n    — Kinderziffer
///   kirche.Y / kirche.N    — Kirchensteuer-Suffix
///   lage.getrennt          — Zivilstand-Besonderheiten
///   lage.konkubinat
///   lage.wochenaufenthalt
///   lage.speziell_bewilligt
///   befreiung.*            — warum jemand NICHT QST-pflichtig ist
/// </summary>
public class QstErklaerung
{
    public int Id { get; set; }

    /// <summary>Baustein-Schlüssel, z.B. "tarif.H" oder "lage.getrennt".</summary>
    public string Code { get; set; } = "";

    /// <summary>Sprachcode ("de", "en", …). Fallback ist immer "de".</summary>
    public string Sprache { get; set; } = "de";

    public string Titel { get; set; } = "";

    /// <summary>Fliesstext, eine Erklärung in Alltagssprache.</summary>
    public string Text { get; set; } = "";

    /// <summary>Reihenfolge in der zusammengesetzten Antwort.</summary>
    public int SortOrder { get; set; }
}

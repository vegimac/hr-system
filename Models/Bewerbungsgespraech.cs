namespace HrSystem.Models;

/// <summary>
/// Gesprächsmodus Bewerbungsgespräch (Walter 03.09.2026): der GF erfasst das
/// Gespräch direkt in OneCrew — eine Frage pro Bildschirm, jede Antwort wird
/// sofort gespeichert. Das Gespräch startet bei null (kein Kandidat, keine
/// Bewerbung nötig) und legt sich beim ersten Feld selbst an.
///
/// Die Antworten liegen als EIN JSON-Dokument in <see cref="AntwortenJson"/>
/// (Schlüssel = Feldname des Fragenflusses in js/gespraech.js). Bewusst kein
/// Feld pro Spalte: der Fragenkatalog darf sich ändern, ohne dass die
/// Tabelle wandert. <see cref="Revision"/> ist der Schutz gegen zwei
/// gleichzeitig offene Fenster — ein veralteter Stand wird mit 409 abgelehnt
/// statt still überschrieben (wie xmin beim Lohnlauf).
///
/// Vorname/Nachname/Geburtsdatum sind denormalisierte Kopien aus dem JSON
/// für Listen und Dubletten-Suche.
/// </summary>
public class Bewerbungsgespraech
{
    public int Id { get; set; }
    public int CompanyProfileId { get; set; }

    /// <summary>in_arbeit | abgeschlossen</summary>
    public string Status { get; set; } = "in_arbeit";

    /// <summary>Zusage | Absage | Rueckstellung (erst beim Abschluss).</summary>
    public string? Entscheid { get; set; }

    public string? Vorname { get; set; }
    public string? Nachname { get; set; }
    public DateOnly? Geburtsdatum { get; set; }

    /// <summary>Schlüssel des zuletzt bearbeiteten Schritts — für den Wiedereinstieg.</summary>
    public string? Schritt { get; set; }

    public int Revision { get; set; } = 0;

    /// <summary>JSON-Objekt aller Antworten (Feld → Wert).</summary>
    public string AntwortenJson { get; set; } = "{}";

    public DateTime GestartetAm { get; set; } = DateTime.Now;
    public string? GestartetVon { get; set; }
    public DateTime GeaendertAm { get; set; } = DateTime.Now;
    public DateTime? AbgeschlossenAm { get; set; }
    public string? AbgeschlossenVon { get; set; }

    public CompanyProfile? CompanyProfile { get; set; }
}

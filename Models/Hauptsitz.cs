namespace HrSystem.Models;

/// <summary>
/// Hauptsitz = Rechtseinheit (Walter-Vorgabe 29.08.2026): eigene Verwaltung
/// neben den Filialen. Mehrere Hauptsitze möglich (Lizenznehmer mit zwei
/// GmbHs); jede Filiale wird per <see cref="CompanyProfile.HauptsitzId"/>
/// ihrem Hauptsitz zugeordnet. Die Swissdec-Meldung läuft PRO Hauptsitz
/// (Meldeeinheit = Rechtseinheit, UID im Meldungskopf).
/// </summary>
public class Hauptsitz
{
    public int Id { get; set; }

    /// <summary>Firmenname der Rechtseinheit, z.B. «Schaub Restaurants GmbH».</summary>
    public string Name { get; set; } = "";

    /// <summary>UID des Hauptsitzes (CHE-XXX.XXX.XXX) — Meldungskopf.</summary>
    public string? Uid { get; set; }

    public string? Strasse { get; set; }
    public string? Plz { get; set; }
    public string? Ort { get; set; }
    public string? KantonCode { get; set; }

    public string? Bemerkung { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

namespace HrSystem.Models;

/// <summary>
/// Lauftext eines Briefs (Briefverwaltung). Kopf, Adresse, Datum, Gruss und
/// Unterschrift setzt der PDF-Brief, nicht dieser Text. Frankiert wird er
/// über die Briefpost (WebStamp).
/// </summary>
public class BriefVorlage
{
    public int Id { get; set; }

    /// <summary>Stabiler Schlüssel, z.B. AUSTRITT. Weitere Briefe bekommen einen eigenen Code.</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string Betreff { get; set; } = "";

    /// <summary>Absätze durch eine Leerzeile getrennt. Platzhalter als {{name}}.</summary>
    public string Text { get; set; } = "";

    public DateTime GeaendertAm { get; set; } = DateTime.Now;
}

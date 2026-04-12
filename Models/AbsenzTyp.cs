namespace HrSystem.Models;

/// <summary>
/// Konfigurierbare Absenz-Typen.
/// Steuert ob eine Absenz eine automatische Zeitgutschrift auslöst
/// und wie diese berechnet wird (1/5 Arbeitstag oder 1/7 Kalendertag).
/// </summary>
public class AbsenzTyp
{
    public int Id { get; set; }

    /// <summary>Interner Code, z.B. KRANK, FERIEN, NACHT_KOMP</summary>
    public string Code { get; set; } = "";

    /// <summary>Angezeigte Bezeichnung, z.B. "Krankheit"</summary>
    public string Bezeichnung { get; set; } = "";

    /// <summary>
    /// true = FIX/MTP bekommen automatisch Stunden gutgeschrieben.
    /// false = keine automatische Gutschrift (z.B. Feiertag ausbezahlt).
    /// UTP bekommt NIE automatisch Gutschrift (separate Businessregel).
    /// </summary>
    public bool Zeitgutschrift { get; set; } = true;

    /// <summary>
    /// '1/5' = Wochenstunden / 5 pro Arbeitstag (Standard für Krank, Unfall etc.)
    /// '1/7' = Wochenstunden / 7 pro Kalendertag (für Ferien)
    /// null  = keine Zeitgutschrift (Zeitgutschrift = false)
    /// </summary>
    public string? GutschriftModus { get; set; }

    public int SortOrder { get; set; } = 99;
    public bool Aktiv { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

namespace HrSystem.Models;

/// <summary>
/// Globaler Key/Value-Store für app-weite Einstellungen, die im Admin-Bereich
/// editierbar sein sollen (Walter-Vorgabe 21.06.2026). Erste Verwendung:
/// Aufbewahrungsdauer der Stempelzeiten in Jahren
/// (Key "TimeEntries.RetentionYears").
/// </summary>
public class AppSetting
{
    /// <summary>Eindeutiger Schlüssel (Primary Key), z.B. "TimeEntries.RetentionYears".</summary>
    public string Key { get; set; } = "";

    /// <summary>Wert als String (wird je nach Schlüssel geparst).</summary>
    public string Value { get; set; } = "";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

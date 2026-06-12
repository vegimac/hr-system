namespace HrSystem.Models;

public class Nationality
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    /// <summary>
    /// Walter-Vorgabe 07.06.2026: optionaler alternativer Code für
    /// Drittsysteme, die abweichende Codes verwenden. Beispiel Kosovo:
    /// offiziell ISO XK, Mirus liefert „XZ" (Post-/Zolldienst-Code).
    /// Bei Importen wird gegen Code UND Code2 gesucht — Code bleibt
    /// die kanonische Anzeige.
    /// </summary>
    public string? Code2 { get; set; }
    /// <summary>
    /// Walter-Vorgabe 13.06.2026: deutscher Klartext-Name aus der DB.
    /// Vorher griff der NationalitiesController nur auf die statische
    /// Fallback-Tabelle `CountryNamesDe` zu — Änderungen an `name_de`
    /// in der DB blieben unsichtbar. Jetzt: DB-Name hat Vorrang vor
    /// dem statischen Fallback (nach AppText-Override).
    /// </summary>
    public string? NameDe { get; set; }
    public bool IsActive { get; set; } = true;
}
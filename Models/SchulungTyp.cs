namespace HrSystem.Models;

/// <summary>
/// Verzeichnis «Schulungen &amp; Ausbildungen» (Walter-Vorgabe 01.10.2026):
/// welche Schulung für wen Pflicht ist, wie lange sie gilt und bis wann sie
/// nach dem Eintritt erledigt sein muss. Verknüpft wird IMMER über
/// <see cref="Code"/>, nie über den Namen.
/// </summary>
public class SchulungTyp
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Auffrischung alle … Monate. NULL = gilt unbegrenzt, warnt nie wegen Ablauf.</summary>
    public int? RefreshMonate { get; set; }
    /// <summary>Frist ab Eintritt in Tagen (1 = am Eintrittstag). NULL = keine Frist ab Eintritt.</summary>
    public int? FristTage { get; set; }
    /// <summary>ALLE / FIXM / GF / LGAV — siehe <see cref="Services.SchulungStatus"/>.</summary>
    public string Zielgruppe { get; set; } = "ALLE";
    /// <summary>Darf als «in FRED erledigt» ohne Dokument abgehakt werden.</summary>
    public bool FredMoeglich { get; set; }
    /// <summary>Vorlauf der Ablauf-Warnung in Tagen. NULL = diese Schulung warnt nicht.</summary>
    public int? WarnenAbTage { get; set; } = 60;
    public bool Aktiv { get; set; } = true;
    public int SortOrder { get; set; }
    public string? Beschreibung { get; set; }
    /// <summary>Lokalzeit. Fristen ab Eintritt mahnen nur Eintritte ab diesem Tag (kein Altbestand).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Ein erledigter Schulungs-/Ausbildungs-Eintrag eines MA (Historie). Genau ein
/// Nachweis pro Eintrag: entweder «in FRED» (ohne Dokument) oder ein
/// verknüpftes Dokument. Eine Auffrischung ist ein NEUER Eintrag.
/// </summary>
public class EmployeeSchulung
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int SchulungTypId { get; set; }
    public SchulungTyp? SchulungTyp { get; set; }
    public DateOnly Datum { get; set; }
    /// <summary>FRED / DOKUMENT / UEBERNOMMEN (aus McAdmin, ohne Nachweis).</summary>
    public string Art { get; set; } = "DOKUMENT";
    public int? DokumentId { get; set; }
    /// <summary>Z.B. «EFZ Restaurationsfachfrau» bei der Gastro-Ausbildung.</summary>
    public string? Titel { get; set; }
    public string? Bemerkung { get; set; }
    public string? ErfasstVon { get; set; }
    /// <summary>Lokalzeit (timestamp without time zone).</summary>
    public DateTime ErfasstAm { get; set; } = DateTime.Now;
}

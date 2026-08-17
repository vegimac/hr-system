namespace HrSystem.Models;

/// <summary>
/// ELM-Lohnraster-Referenzkatalog (Walter-Vorgabe 17.08.2026): das komplette
/// Lohnraster (309 Positionen — Lohnarten, SV-Abzuege, Absenzarten, Summen)
/// als dauerhaftes Archiv in OneCrew. Quelle: Lohnraster-Export; ab 2027
/// existiert das Quellsystem nicht mehr — dieses Archiv (inkl. AttrsJson =
/// verlustfreie Rohdaten) ist dann die einzige Referenz.
///
/// «PickList»-Prinzip: Eintraege werden hier NUR gelesen; wer eine Position
/// in OneCrew nutzen will, «uebernimmt» sie — das legt eine Lohnposition an
/// und verlinkt sie (VerwendetLohnpositionId). Nichts wird automatisch aktiv.
/// </summary>
public class ElmLohnrasterEintrag
{
    public int     Id { get; set; }
    public string  Code { get; set; } = "";          // «10.2», «530.101» …
    public string  Pos { get; set; } = "";
    public string? Sub { get; set; }
    public string  Bezeichnung { get; set; } = "";
    public string? Gruppe { get; set; }              // Hauptposition-Name («Festlohn»)
    public string  Typ { get; set; } = "LOHNART";    // LOHNART | SV_ABZUG | ABSENZ | SUMME
    public string? Text { get; set; }
    public string? UebersetzungIt { get; set; }
    public string? UebersetzungFr { get; set; }
    public string? Lohnausweisfeld { get; set; }     // «1.  Lohn», «9.  Beiträge …»
    public string? StatistikCode { get; set; }
    public string? Steuerung { get; set; }           // Positiv | Negativ
    public string? BetragProzent { get; set; }
    public bool    Inaktiv { get; set; }
    public bool?   Ahv { get; set; }
    public bool?   Qst { get; set; }
    public bool?   QstPeriodisch { get; set; }
    public bool?   Bvg { get; set; }
    public bool?   Uvg { get; set; }
    public bool?   Uvgz { get; set; }
    public bool?   Ktg { get; set; }
    public bool?   Ml13 { get; set; }
    /// <summary>Verlustfreies Vollarchiv: alle Attribut-Zeilen des Exports als JSON.</summary>
    public string  AttrsJson { get; set; } = "[]";
    /// <summary>Gesetzt, sobald die Position in OneCrew uebernommen/verknuepft wurde.</summary>
    public int?    VerwendetLohnpositionId { get; set; }
    public Lohnposition? VerwendetLohnposition { get; set; }
}

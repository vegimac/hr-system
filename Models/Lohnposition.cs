namespace HrSystem.Models;

/// <summary>
/// Lohnart / Lohnposition — definiert für jede Lohnart welche SV-Abzüge anfallen.
/// Ersetzt die vereinfachte lohn_zulag_typ-Tabelle mit granularen Flags pro Versicherungstyp.
/// Nur durch Administratoren verwaltbar.
/// </summary>
public class Lohnposition
{
    public int     Id              { get; set; }
    public string  Code            { get; set; } = "";      // z.B. "10.1", "55.1", "195.2"
    public string  Bezeichnung     { get; set; } = "";      // z.B. "Festlohn", "Überstunden 25%"
    public string  Kategorie       { get; set; } = "";      // z.B. "Festlohn", "Überstunden", "Spesen"
    public string  Typ             { get; set; } = "ZULAGE"; // ZULAGE (+) | ABZUG (-)

    // SV-Steuerung (Raster-Flags)
    public bool    AhvAlvPflichtig { get; set; } = true;   // AHV / IV / EO + ALV
    public bool    NbuvPflichtig   { get; set; } = true;   // Nichtberufsunfallversicherung
    public bool    KtgPflichtig    { get; set; } = true;   // Krankentaggeldversicherung
    public bool    BvgPflichtig    { get; set; } = true;   // Berufliche Vorsorge (BVG)
    public bool    QstPflichtig    { get; set; } = true;   // Quellensteuer-pflichtig

    public string? LohnausweisCode { get; set; }           // Lohnausweisposition: I, P, Y, K, O, 7, 13.2.3 …

    /// <summary>
    /// true = Der eingegebene Betrag wird automatisch in Basis (12/13) + 13. ML (1/13 = 8.33%)
    /// gesplitted. Beide Zeilen erscheinen auf dem Lohnzettel.
    /// Typisch für: Bonus, Prämie, McBonus usw.
    /// </summary>
    public bool    DreijehnterMlPflichtig { get; set; } = false;

    // ── Basis-Flags (Datenbank-getriebene Lohnart-Regeln) ─────────────
    // Ersetzen die hart verdrahtete Logik im PayrollController.
    // "true" = die Beträge dieser Lohnart fliessen in die jeweilige
    // Bemessungsgrundlage ein.

    /// <summary>Zählt zur Bemessungsgrundlage der Feiertagsentschädigung.</summary>
    public bool    ZaehltAlsBasisFeiertag { get; set; } = false;

    /// <summary>Zählt zur Bemessungsgrundlage der Ferienentschädigung.</summary>
    public bool    ZaehltAlsBasisFerien   { get; set; } = false;

    /// <summary>Zählt zur Bemessungsgrundlage des 13. Monatslohns.</summary>
    public bool    ZaehltAlsBasis13ml     { get; set; } = false;

    // ── Mirus-inspirierte Erweiterungen ─────────────────────────────────
    // Reichere Konfiguration pro Lohnposition für Lohnausweis, Statistik
    // und besondere Berechnungen (Karenz BVG-Korrektur, 13. ML, Tagessatz).

    /// <summary>
    /// Lohnausweis-Feld: "1" Lohn, "2" Sozialleistungen, "3" Unregelmässige
    /// Leistungen, "4" Kapitalleistungen, "5" Beteiligungsrechte, "6" VR-Honorare,
    /// "7" Andere Leistungen, "9" Spesen-Pauschale, "13" Effektive Spesen,
    /// "15" Andere Bemerkungen. Null = nicht auf Lohnausweis.
    /// </summary>
    public string? Lohnausweisfeld { get; set; }

    /// <summary>
    /// Kreuz/Markierung im Lohnausweis-Feld (z.B. für gewährte Naturalleistungen).
    /// </summary>
    public bool LohnausweisKreuz { get; set; } = false;

    /// <summary>
    /// BFS-Statistik-Code (z.B. "I" Bruttolohn-Statistik, "II" Sozialleistungen).
    /// </summary>
    public string? StatistikCode { get; set; }

    /// <summary>
    /// Diese Lohnposition wird auf dem Lohnzettel NICHT gedruckt, wenn der
    /// Betrag 0 ist. Hilft, leere Zeilen wegzulassen (z.B. wenn keine Krankheit).
    /// </summary>
    public bool NichtDruckenWennNull { get; set; } = true;

    /// <summary>
    /// Diese Lohnposition wird im Arbeitsvertrag NICHT gedruckt
    /// (z.B. interne Korrekturpositionen).
    /// </summary>
    public bool NichtImVertragDrucken { get; set; } = false;

    /// <summary>
    /// BVG: Berechne den BVG-Beitrag immer auf 100% des theoretischen Lohns,
    /// nicht auf dem effektiv ausgezahlten Betrag. Wichtig bei Karenz-Tagen,
    /// damit die Vorsorge-Beiträge nicht zurückgehen.
    /// </summary>
    public bool BvgAuf100Rechnen { get; set; } = false;

    /// <summary>
    /// Position im 13.-ML-Akkumulator: 0 = Standard (zählt zur Basis und wird
    /// im Auszahlungsmonat anteilig ausgezahlt). Andere Werte für Spezialfälle
    /// (z.B. McBonus mit eigenem 13.-ML-Anteil).
    /// </summary>
    public int Position13ml { get; set; } = 0;

    /// <summary>
    /// Diese Lohnposition zählt für die Berechnung des durchschnittlichen
    /// Tagesverdienstes (KTG-Tagessatz, Karenz-Berechnung).
    /// </summary>
    public bool ZaehltFuerTagessatz { get; set; } = true;

    public int     SortOrder       { get; set; } = 99;
    public bool    IsActive        { get; set; } = true;
    public DateTime CreatedAt      { get; set; } = DateTime.UtcNow;
}

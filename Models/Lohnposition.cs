using System.ComponentModel.DataAnnotations.Schema;

namespace HrSystem.Models;

/// <summary>
/// Lohnart / Lohnposition — definiert für jede Lohnart welche SV-Abzüge anfallen.
/// Ersetzt die vereinfachte lohn_zulag_typ-Tabelle mit granularen Flags pro Versicherungstyp.
/// Nur durch Administratoren verwaltbar.
/// </summary>
public class Lohnposition
{
    public int     Id              { get; set; }
    /// <summary>Primärschlüssel-Code = Swissdec-Treuhand-Nr. (1000, 1005, 2002 …); Betrieb/Auslöser: 600.x / 180.3.</summary>
    public string  Code            { get; set; } = "";
    public string  Bezeichnung     { get; set; } = "";      // z.B. "Festlohn", "Überstunden 25%"
    public string  Typ             { get; set; } = "ZULAGE"; // ZULAGE (+) | ABZUG (-)

    /// <summary>Mirus-Altcode nur für Parallelvergleich / Kontoplan-Parse (Walter 19.10.2026).</summary>
    public string? MirusCode       { get; set; }

    /// <summary>Formelwerk-Id (siehe Services/LohnFormelwerk), null = keine Formel / manuell.</summary>
    public string? FormelId        { get; set; }

    // SV-Steuerung (Raster-Flags)
    public bool    AhvAlvPflichtig { get; set; } = true;   // AHV / IV / EO + ALV
    public bool    NbuvPflichtig   { get; set; } = true;   // Nichtberufsunfallversicherung
    /// <summary>UVG (Berufsunfall). Startwert = NBU, damit der UVG-Lohn gleich bleibt.</summary>
    public bool    UvgPflichtig    { get; set; } = true;
    /// <summary>UVG-Zusatz. Startwert = NBU, der Zusatz rechnet heute auf demselben Lohn.</summary>
    public bool    UvgzPflichtig   { get; set; } = true;
    public bool    KtgPflichtig    { get; set; } = true;   // Krankentaggeldversicherung
    public bool    BvgPflichtig    { get; set; } = true;   // Berufliche Vorsorge (BVG)
    public bool    QstPflichtig    { get; set; } = true;   // Quellensteuer-pflichtig

    public string? LohnausweisCode { get; set; }
    /// <summary>
    /// Swissdec-Lohnart (Musterlohnartenstamm, vierstellig: 1000 Monatslohn,
    /// 1005 Stundenlohn, 1161 Feiertagsentschädigung, 2030 Unfall-Taggeld …).
    /// Bedeutungs-Zuordnung für ELM-Meldung und Testmandant; die interne Nummer
    /// (Code) bleibt Schlüssel für Lohnschema/FIBU. Mehrere Positionen dürfen
    /// dieselbe Swissdec-Lohnart tragen (195.2 + 195.4 → 1161). Walter 08.09.2026.
    /// </summary>
    public string? SwissdecLohnart { get; set; }           // Lohnausweisposition: I, P, Y, K, O, 7, 13.2.3 …

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

    /// <summary>
    /// QST: periodische Lohnart (Walter 21.09.2026, statt fest im Code). true = läuft
    /// monatlich (Lohn, Zulagen, Kinderzulage): im Kurzmonat/Nebenerwerb auf den vollen
    /// Monat hochrechnen, bei Wohnsitz Ausland mit dem CH-Tage-Verhältnis des MONATS
    /// ausscheiden. false = einmalig (Bonus, Gratifikation, Abgangsentschädigung,
    /// Beteiligungen, Geburtszulage, VR-Honorar): satzbestimmend 1:1, Ausscheidung mit
    /// Σ CH-Tage seit Jahresbeginn (Anhang 1 Y31, RefXML TF28/TF29).
    /// Seed aus dem Swissdec-Katalog: <see cref="SwissdecEinmalig"/>.
    /// </summary>
    public bool QstPeriodisch { get; set; } = true;

    /// <summary>
    /// QST-Charakter: «monatlich» oder «jaehrlich». Seed folgt der Periodizität,
    /// deshalb bleibt der Lohn gleich, bis jemand die beiden Steuerungen trennt.
    /// </summary>
    public string QstCharakter { get; set; } = "monatlich";

    /// <summary>«rueckwirkend», «voraussichtlich» oder «keine». «keine» zählt nicht zur BVG-Basis.</summary>
    public string BvgBetrachtung { get; set; } = "rueckwirkend";

    /// <summary>Faktor der voraussichtlichen Betrachtung, z.B. 12. Bei Faktor x steht hier 0 und <see cref="BvgFaktorX"/>.</summary>
    public decimal BvgFaktor { get; set; } = 1m;

    /// <summary>Swissdec-Faktor «x» beim Monatslohn: 13 mit 13. Monatslohn, sonst 12.</summary>
    public bool BvgFaktorX { get; set; }

    /// <summary>Rückwirkende Gruppe: 0 keine, 1 Stundenlohn, 2 Lektionenlohn, 3 übrige, 4 Taggeld.</summary>
    public int BvgRueckCode { get; set; }

    /// <summary>
    /// BVG-Lösung, der diese Lohnart gehört (z.B. 1101). Leer = zählt für jede Lösung.
    /// Eine Lohnart darf nur einer Lösung gehören (Swissdec 8.6.2).
    /// </summary>
    public string? BvgLoesungsCode { get; set; }

    /// <summary>Statistik-Topf der Monatswerte (Bruttolohn, Zulagen, …). Leer = keine Monatsbasis.</summary>
    public string? StatistikMonat { get; set; }

    /// <summary>Statistik-Topf der Jahreswerte (Überstunden, 13. ML, …). Leer = keine Jahresbasis.</summary>
    public string? StatistikJahr { get; set; }

    /// <summary>Swissdec-Lohnarten, die einmalig (aperiodisch) sind — Seed + Vorschlag in 4b.</summary>
    public static bool SwissdecEinmalig(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        code = code.Trim();
        if (code.StartsWith("12") || code.StartsWith("13") || code.StartsWith("14")) return true;   // Sonderzahlungen, Austritt/Kapital
        if (code.StartsWith("15")) return true;                                                     // Honorare
        if (code.StartsWith("196")) return true;                                                    // Beteiligungsrechte
        if (code.StartsWith("197") || code == "1980") return true;                                  // vom AG übernommene Beiträge, Weiterbildung
        return code is "1067" or "1168" or "3001" or "3034";                                        // Überzeit nach Austritt, Ferienauszahlung Austritt, Geburtszulagen
    }

    public int     SortOrder       { get; set; } = 99;
    public bool    IsActive        { get; set; } = true;
    public DateTime CreatedAt      { get; set; } = DateTime.Now;

    /// <summary>Steht auf einem gespeicherten Lohnzettel. Nicht in der Datenbank.</summary>
    [NotMapped]
    public bool InLohnVerwendet { get; set; }
}

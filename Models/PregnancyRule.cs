using System;

namespace HrSystem.Models;

/// <summary>
/// Globales Regelwerk für Mutterschafts-Fristen (ArG/ArGV 1/OR).
/// Eine Zeile = eine gesetzliche Frist mit konfigurierbarem Offset zur Basis
/// (Errechneter Termin / effektive Geburt / Meldedatum).
/// Pflege via Systemeinstellungen → Mutterschafts-Regeln (admin-only).
/// </summary>
public class PregnancyRule
{
    public int    Id              { get; set; }
    public string Code            { get; set; } = "";    // STEHEN_4H, NACHT_VERBOT, …
    public string Bezeichnung     { get; set; } = "";
    public string? Beschreibung   { get; set; }
    public string? Gesetz         { get; set; }          // z.B. "ArG Art. 35a Abs. 4"

    /// <summary>ET | GEBURT | MELDUNG — worauf bezieht sich der Offset?</summary>
    public string BerechnungBasis { get; set; } = "ET";

    public int    OffsetMonate    { get; set; }
    public int    OffsetWochen    { get; set; }

    /// <summary>VORHER (Frist beginnt davor) | NACHHER.</summary>
    public string Richtung        { get; set; } = "VORHER";

    public bool   IstArbeitsverbot { get; set; }
    public int    SortOrder       { get; set; } = 99;
    public bool   Aktiv           { get; set; } = true;
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;

    // Walter-Vorgabe 10.06.2026 (Variante B): Phasen-Ende, Lohn-/Staffel-
    // Felder. NULL = einzelner Stichtag (alter Modus), gesetzt = Phase mit
    // Ende. Lohn-/Staffel-Felder sind reine Anzeige-Infos.
    public string?  BasisEnde         { get; set; }   // ET | GEBURT | MELDUNG
    public int?     OffsetEndeMonate  { get; set; }
    public int?     OffsetEndeWochen  { get; set; }
    public string?  RichtungEnde      { get; set; }   // VORHER | NACHHER
    public decimal? LohnersatzPct     { get; set; }   // 80.00, 88.00 …
    public decimal? MaxBetragProTag   { get; set; }   // 220.00 (MSE)
    public string?  StaffelText       { get; set; }   // Free-Text Staffel
}

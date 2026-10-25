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

    /// <summary>Zusatzzeile am Firmennamen (Abteilung / Filialbezeichnung) — ELM Name/ComplementaryLine.</summary>
    public string? ZusatzAbteilung { get; set; }
    /// <summary>Zweite Zusatzzeile am Firmennamen (optional).</summary>
    public string? ZusatzAbteilung2 { get; set; }
    /// <summary>Zusatzzeile der Postadresse — ELM Address/ComplementaryLine.</summary>
    public string? AdressZusatz { get; set; }
    public string? Strasse { get; set; }
    /// <summary>Postfach — alternativ zur Strasse (Swissdec 4.2).</summary>
    public string? Postfach { get; set; }
    /// <summary>Lokalität (Region / Provinz).</summary>
    public string? Lokalitaet { get; set; }
    public string? Plz { get; set; }
    public string? Ort { get; set; }
    public string? KantonCode { get; set; }
    /// <summary>Land als ISO-2 (CH); im XML immer englisch GROSS (SWITZERLAND).</summary>
    public string? Land { get; set; } = "CH";
    /// <summary>BFS-Gemeindenummer des Sitzes — für Lohnausweis-Verteilung bei ResidenceCanton EX (nicht im Company-Address-XML).</summary>
    public int? BfsGemeindeNr { get; set; }

    /// <summary>
    /// Ein Buchungskreis für die ganze Rechtseinheit (Swissdec PayrollUnit, Statistik).
    /// Leer = keine Aufteilung. Mehrere Kreise gibt es nicht.
    /// </summary>
    public string? BfsBuchungskreis { get; set; }

    /// <summary>
    /// Lohnvereinbarung der Rechtseinheit (Swissdec PayAgreement).
    /// CLA-Association, CLA-BusinessOrGovernment, collectiveContratOutside-CLA, individualContract.
    /// Leer = Einzelarbeitsvertrag, wie die Muster AG.
    /// </summary>
    public string? Lohnvereinbarung { get; set; }

    public string? Bemerkung { get; set; }

    /// <summary>
    /// Kontaktperson der Rechtseinheit für elektronische Lohnmeldungen
    /// (Walter 27.09.2026, Swissdec <c>ContactPerson</c>). Wer eine Rückfrage zur
    /// Meldung bekommt — bewusst am Hauptsitz und nicht am angemeldeten Benutzer:
    /// die Meldung gehört der Rechtseinheit, nicht dem, der sie erzeugt hat.
    /// </summary>
    public string? KontaktName { get; set; }
    public string? KontaktEmail { get; set; }
    public string? KontaktTelefon { get; set; }
    /// <summary>Mobil — speichern international («+41 79…»), XML national («079…»).</summary>
    public string? KontaktMobil { get; set; }

    /// <summary>UID-BFS unbekannt → XML <c>UID-BFS/Unknown</c> (Ausnahme, Swissdec 4.2).</summary>
    public bool UidUnbekannt { get; set; }

    // Stellvertreter / Treuhand (Swissdec 4.2.4 Delegate) — optional.
    public string? StellvertreterName { get; set; }
    public string? StellvertreterInhaber { get; set; }
    public string? StellvertreterAdressZusatz { get; set; }
    public string? StellvertreterStrasse { get; set; }
    public string? StellvertreterPostfach { get; set; }
    public string? StellvertreterLokalitaet { get; set; }
    public string? StellvertreterPlz { get; set; }
    public string? StellvertreterOrt { get; set; }
    public string? StellvertreterLand { get; set; }
    public string? StellvertreterUid { get; set; }

    public bool IsActive { get; set; } = true;

    // ── Vertragsregeln der Rechtseinheit (Walter-Vorgabe 01.09.2026) ──────
    // Anstellungsbedingungen gehören zum Arbeitgeber, nicht zur Filiale: alle
    // Filialen einer GmbH hängen am selben Wert, und ein zweiter Lizenznehmer
    // mit eigener GmbH darf eigene Grenzen setzen. Der easy@work-Sync prüft
    // Verträge dagegen und importiert Abweichungen NICHT.
    //
    // Alle Felder sind optional — ist eines leer oder hat die Filiale (noch)
    // keinen Hauptsitz, gelten die Standardwerte aus VertragsRegeln.Standard.
    // Damit läuft nie eine Filiale stillschweigend ungeprüft durch.

    /// <summary>Erlaubte FIX/FIX-M-Pensen als Liste, z.B. «50,60,70,80,90,100».</summary>
    public string? FixPensenErlaubt { get; set; }

    /// <summary>FLEX: höchstens so viele Stunden pro Woche.</summary>
    public decimal? FlexStundenMax { get; set; }

    /// <summary>MTP: mindestens so viele Stunden pro Woche.</summary>
    public decimal? MtpStundenMin { get; set; }

    /// <summary>MTP: höchstens so viele Stunden pro Woche.</summary>
    public decimal? MtpStundenMax { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

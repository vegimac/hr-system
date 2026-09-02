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

    public string? Strasse { get; set; }
    public string? Plz { get; set; }
    public string? Ort { get; set; }
    public string? KantonCode { get; set; }

    public string? Bemerkung { get; set; }
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

namespace HrSystem.Models;

/// <summary>
/// Protokoll der Gruppen-E-Mails (Walter-Vorgabe 01.09.2026).
///
/// Bewusst EIN Eintrag pro Versand, nicht pro Empfänger: Die Frage lautet
/// «wann ging was an welche Gruppe raus», nicht «wer hat was bekommen» —
/// letzteres steht ohnehin in <see cref="MailLog"/>.
///
/// Deshalb wird hier die SELEKTION festgehalten (Filiale, Vertragsmodelle,
/// Funktionen, Benutzer ja/nein) und nicht bloss die Empfängerzahl. Aus
/// «6 Mitarbeitende» lässt sich später nicht mehr rekonstruieren, WER
/// gemeint war; aus «058 Oftringen · FIX-M · alle Funktionen» schon.
/// </summary>
public class GruppenMailLog
{
    public int Id { get; set; }

    public DateTime GesendetAm { get; set; } = DateTime.Now;

    /// <summary>Wer den Versand ausgelöst hat.</summary>
    public int? GesendetVonUserId { get; set; }
    public AppUser? GesendetVonUser { get; set; }

    public string Betreff { get; set; } = "";

    // ── Die Selektion, wie sie im Fenster stand ───────────────────────────
    /// <summary>Filiale im Klartext, «Alle Filialen» wenn ohne Einschränkung.</summary>
    public string? Filiale { get; set; }
    /// <summary>Gewählte Vertragsmodelle, z.B. «FIX-M». Leer = alle.</summary>
    public string? Modelle { get; set; }
    /// <summary>Gewählte Funktionen. Leer = alle.</summary>
    public string? Funktionen { get; set; }
    /// <summary>Waren die OneCrew-Benutzer mit dabei?</summary>
    public bool MitBenutzern { get; set; }

    // ── Ergebnis ──────────────────────────────────────────────────────────
    public int AnzahlGesendet { get; set; }
    public int AnzahlFehlgeschlagen { get; set; }
    /// <summary>Doppelte Adressen, die nur einmal angeschrieben wurden.</summary>
    public int AnzahlDoppelt { get; set; }
    public int AnzahlOhneEmail { get; set; }

    public string? AnhangName { get; set; }
    /// <summary>Hatte die Mail einen Nachrichtentext?</summary>
    public bool MitText { get; set; }

    /// <summary>
    /// Ging der Versand SCHARF an die echten Adressen, oder an die
    /// Test-Adresse? Ohne diese Angabe steht später ein Eintrag «an 200
    /// gesendet» im Protokoll, obwohl alles bei Walter gelandet ist.
    /// </summary>
    public bool Scharf { get; set; }
}

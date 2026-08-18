namespace HrSystem.Models;

/// <summary>
/// Konfigurierbare Absenz-Typen.
/// Steuert ob eine Absenz eine automatische Zeitgutschrift auslöst
/// und wie diese berechnet wird (1/5 Arbeitstag oder 1/7 Kalendertag).
/// </summary>
public class AbsenzTyp
{
    public int Id { get; set; }

    /// <summary>Interner Code, z.B. KRANK, FERIEN, NACHT_KOMP</summary>
    public string Code { get; set; } = "";

    /// <summary>Angezeigte Bezeichnung, z.B. "Krankheit"</summary>
    public string Bezeichnung { get; set; } = "";

    /// <summary>
    /// true = FIX/MTP bekommen automatisch Stunden gutgeschrieben.
    /// false = keine automatische Gutschrift (z.B. Feiertag ausbezahlt).
    /// UTP bekommt NIE automatisch Gutschrift (separate Businessregel).
    /// </summary>
    public bool Zeitgutschrift { get; set; } = true;

    /// <summary>
    /// '1/5' = Wochenstunden / 5 pro Arbeitstag (Standard für Krank, Unfall etc.)
    /// '1/7' = Wochenstunden / 7 pro Kalendertag (für Ferien)
    /// null  = keine Zeitgutschrift (Zeitgutschrift = false)
    /// </summary>
    public string? GutschriftModus { get; set; }

    /// <summary>
    /// true = UTP-Mitarbeiter erhalten die Stunden dieser Absenz als Stundenlohn
    /// ausbezahlt (z. B. NACHT_KOMP). Default false (generelle UTP-Regel: keine
    /// Gutschrift).
    /// </summary>
    public bool UtpAuszahlung { get; set; } = false;

    /// <summary>
    /// Welcher Saldo wird durch diese Absenz reduziert?
    ///   NACHT_STUNDEN = Nacht-Saldo (NACHT_KOMP)
    ///   FERIEN_TAGE   = Ferien-Tage-Saldo (FERIEN)
    ///   null          = reduziert keinen Saldo
    /// </summary>
    public string? ReduziertSaldo { get; set; }

    /// <summary>
    /// Basis für die 1/5- oder 1/7-Berechnung der Zeitgutschrift:
    ///   BETRIEB = Normal-Wochenstunden der Filiale (CompanyProfile)
    ///   VERTRAG = Für MTP: GuaranteedHoursPerWeek; für andere Modelle
    ///             Fallback auf Betrieb.
    /// </summary>
    public string BasisStunden { get; set; } = "BETRIEB";

    /// <summary>
    /// Basis bei MTP (Walter-Vorgabe 18.08.2026): der MTP hat quasi seine
    /// eigene «Betriebsarbeitszeit» — das Garantie-Pensum. Pro Absenz-Typ
    /// wählbar, womit die 1/5- bzw. 1/7-Rechnung bei MTP arbeitet:
    ///   GARANTIE (Default) = garantierte Wochenstunden (z.B. Krankheit: 25/5)
    ///   BETRIEB            = Filial-Wochenstunden (z.B. Nacht-Komp: 42/5)
    /// </summary>
    public string BasisStundenMtp { get; set; } = "GARANTIE";

    /// <summary>
    /// Lohnposition-Code für die Auszahlung dieser Absenz (z.B. "70" für
    /// Krankheits-Karenzentschädigung, "2" für Festlohn für bezogene Ferien).
    /// Null = keine Auszahlungs-Lohnposition (z.B. SCHULUNG → nur Saldo).
    /// </summary>
    public string? LohnpositionAuszahlungCode { get; set; }

    /// <summary>
    /// Lohnposition-Code für die Lohnkürzung dieser Absenz (z.B. "75" für
    /// Korrektur Krankheit, "65" für Korrektur Unfall).
    /// Null = keine Kürzung.
    /// </summary>
    public string? LohnpositionKuerzungCode { get; set; }

    /// <summary>
    /// Verbuchungsmuster auf der Lohnabrechnung:
    ///   SPLIT     = nur Auszahlung-Code, Festlohn wird intern um den Betrag
    ///               reduziert und als separate Zeile angezeigt
    ///               (Mirus-Style für Ferien, Feiertag).
    ///   KORREKTUR = beide Codes, Festlohn bleibt voll, Lohnkürzung als
    ///               negative Zeile + Auszahlung als positive Zeile
    ///               (Mirus-Style für Krank, Unfall).
    ///   KEIN      = keine Lohnpositionen, nur Saldo-Wirkung
    ///               (Schulung, Militär ohne Lohn).
    /// </summary>
    public string Pattern { get; set; } = "KEIN";

    /// <summary>
    /// true = eine Absenz dieses Typs, die in die Probezeit fällt, VERLÄNGERT die
    /// Probezeit um die Anzahl Absenztage (Walter 30.06.2026). Pflegbar in der
    /// Absenz-Typen-Konfiguration. Beim Setzen wird eine ABSENZ-Zeile in
    /// employment_probation_log geschrieben und probation_end_date nachgeführt.
    /// </summary>
    public bool VerlaengertProbezeit { get; set; } = false;

    public int SortOrder { get; set; } = 99;
    public bool Aktiv { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Buchstaben-Kürzel im offiziellen ALK-Zwischenverdienst-Formular:
    ///   A = Ferien
    ///   B = Krankheit, Schwangerschaft
    ///   C = Unfall
    ///   D = Mutterschaftsurlaub, Urlaub des andern Elternteils, Betreuungsurlaub
    ///   E = Militär, Zivildienst oder Zivilschutz
    ///   F = Betriebsferien
    ///   G = Unbezahlte Absenzen (Default für nicht spezifizierte)
    /// Null = wird nicht im Tagesraster eingetragen.
    /// </summary>
    public string? ZwischenverdienstKuerzel { get; set; }
}

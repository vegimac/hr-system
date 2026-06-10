namespace HrSystem.Models;

/// <summary>
/// Ergebnis des Akonto-Laufs: eine geschätzte Netto-Vorauszahlung pro
/// Mitarbeiter und Lohnperiode (Kalendermonat).
///
/// Wichtig: Das Akonto ist KEINE echte Lohnabrechnung — es gibt keinen
/// Lohnbeleg und keine SV-/BVG-/QST-Buchung. Es ist eine Zahlung auf
/// Rechnung. Der Definitivlauf am Monatsende ist die einzige echte
/// Lohnabrechnung; er liest <see cref="NettoAkonto"/> über das Snapshot-
/// Feld <c>AkontoBereitsAusbezahlt</c> und zieht es vom berechneten Netto
/// ab → Restzahlung.
///
/// Akonto-Basis je Vertragsmodell (siehe AKONTO-LOHN-PLAN.md, Abschnitt 2):
///   • UTP / MTP : bis zum Akonto-Stichtag gestempelte Stunden + Feriengeld
///                 für bezogene Ferientage → 100 % des geschätzten Netto.
///   • FIX/FIX-M : voraussichtlich ausbezahlter Monatslohn → Filial-Prozent
///                 (CompanyProfile.AkontoProzentFix, Default 80 %).
/// </summary>
public class AkontoZahlung
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public int CompanyProfileId { get; set; }

    /// <summary>Lohnperiode (= Kalendermonat).</summary>
    public int PeriodYear  { get; set; }
    public int PeriodMonth { get; set; }

    /// <summary>Akonto-Auszahlungsdatum (aus <see cref="AkontoTermin"/>).</summary>
    public DateOnly PayoutDate { get; set; }

    // ── Berechnungs-Bestandteile (zur Nachvollziehbarkeit gespeichert) ──

    /// <summary>Geschätzte Brutto-Basis je Vertragsmodell (UTP/MTP =
    /// gestempelte Stunden + Feriengeld, FIX/FIX-M = Monatslohn).</summary>
    public decimal GeschaetzterBrutto { get; set; }

    /// <summary>Anteil Feriengeld für bis zum Stichtag bezogene Ferientage
    /// (bereits in <see cref="GeschaetzterBrutto"/> enthalten — separat
    /// gespeichert für die Transparenz).</summary>
    public decimal FeriengeldAnteil { get; set; }

    /// <summary>Grosszügig geschätzte Abzüge (SV, BVG, QST).</summary>
    public decimal GeschaetzteAbzuege { get; set; }

    /// <summary>Beim MA gekürzter Pfändungs-/Abtretungs-Anteil (Schätzung,
    /// begrenzt durch die Freigrenze des EmployeeLohnAssignment). Die
    /// Zahlung ans Betreibungsamt selbst erfolgt erst im Definitivlauf.</summary>
    public decimal PfaendungAbzug { get; set; }

    /// <summary>Tatsächlich an den MA ausbezahltes Netto-Akonto
    /// (auf CHF 10 abgerundet).</summary>
    public decimal NettoAkonto { get; set; }

    /// <summary>
    /// Status pro MA-Lohnblatt im Akonto-Workflow (Walter-Vorgabe 16.05.2026):
    ///   BERECHNET       — vom System berechnet, vom GF noch nicht gesichtet
    ///   FREIGEGEBEN_GF  — der Geschäftsführer hat das Lohnblatt geprüft und
    ///                     bewusst freigegeben (Stammdaten, Bank, Absenzen,
    ///                     Plausibilität gecheckt)
    ///   AUSBEZAHLT      — HR-Final-Freigabe + DTA gelaufen (immutable)
    ///   STORNIERT       — nach Auszahlung zurückgerollt (mit Audit-Trail)
    /// </summary>
    public string Status { get; set; } = "BERECHNET";

    // ── 4-Augen-Workflow (Walter-Vorgabe 16.05.2026): GF gibt frei, HR kontrolliert ──

    /// <summary>Wann der GF dieses Lohnblatt freigegeben hat (NULL = noch nicht).</summary>
    public DateTime? GfFreigegebenAt { get; set; }

    /// <summary>Welcher GF-User das Lohnblatt freigegeben hat (FK app_user).</summary>
    public int? GfFreigegebenBy { get; set; }

    /// <summary>Notiz vom GF — z.B. wenn er eine ungewöhnliche Konstellation
    /// erklären will, bevor HR das Blatt sieht.</summary>
    public string? KommentarGf { get; set; }

    /// <summary>Notiz von HR — wird gesetzt, wenn HR das Blatt mit Begründung
    /// an den GF zurückschickt (Korrektur-Loop).</summary>
    public string? KommentarHr { get; set; }

    /// <summary>
    /// Walter-Vorgabe 28.05.2026: Ausschluss-Grund wenn der MA am Stichtag
    /// ineligible ist (z.B. „Krank am Stichtag (07.01.–18.03.)", „Kein
    /// gültiger Vertrag in dieser Periode", „Vertrag hängt an Filiale …").
    /// NULL = normale Akonto-Zahlung. Damit erscheint der MA im Lohnlauf
    /// trotzdem (als rote Fehler-Zeile), statt stillschweigend zu verschwinden.
    /// </summary>
    public string? ErrorReason { get; set; }

    /// <summary>
    /// Walter-Vorgabe 28.05.2026: GF-Override — bei Ineligibility (ErrorReason
    /// gesetzt) entscheidet der GF pro MA, ob trotzdem ein Akonto ausgezahlt
    /// werden soll. Default FALSE. Wenn TRUE: das nächste „Neu berechnen"
    /// ignoriert die Eligibility-Sperre und rechnet Brutto/Netto wie üblich.
    /// </summary>
    public bool ForcePayout { get; set; } = false;

    /// <summary>Verweis auf den DTA-/pain.001-Zahllauf, mit dem das Akonto
    /// ausbezahlt wurde. NULL solange noch nicht ausbezahlt. Wird in Etappe 2/3
    /// (HR-Freigabe + DTA) verdrahtet — bewusst noch kein harter FK.</summary>
    public int? DtaRunId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Employee?       Employee        { get; set; }
    public CompanyProfile? Company         { get; set; }
    public AppUser?        GfFreigegebenByUser { get; set; }
}

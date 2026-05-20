namespace HrSystem.Models;

/// <summary>
/// Konkrete Lohnperiode für eine Filiale (z.B. März 2026: 21.02.–20.03.2026).
/// Status-Flow: offen → provisorisch_abgeschlossen → abgeschlossen.
///   • offen                       — Geschäftsführer kontrolliert MA-Lohnzettel.
///   • provisorisch_abgeschlossen  — GF hat alle MAs bestätigt, Lohnzettel
///       eingefroren; HR liest Vorab-PDF, kommuniziert via Posteingang mit GF.
///       Nur HR/Admin können diesen Status zurück auf offen setzen.
///   • abgeschlossen               — HR hat den definitiven Lohnabschluss
///       gemacht: DTA generiert, Lohnbelege bereitgestellt, Periode dicht.
///       Nur Admin kann den Status wieder ändern.
/// </summary>
public class PayrollPeriode
{
    public int Id { get; set; }

    public int CompanyProfileId { get; set; }

    /// <summary>Auszahlungsjahr (Jahr in dem der Lohn ausbezahlt wird).</summary>
    public int Year { get; set; }

    /// <summary>Auszahlungsmonat (1–12).</summary>
    public int Month { get; set; }

    /// <summary>Erster Tag dieser konkreten Periode, z.B. 2026-02-21.</summary>
    public DateOnly PeriodFrom { get; set; }

    /// <summary>Letzter Tag dieser konkreten Periode, z.B. 2026-03-20.</summary>
    public DateOnly PeriodTo { get; set; }

    /// <summary>Anzeige-Label, z.B. "März 2026".</summary>
    public string Label { get; set; } = "";

    /// <summary>offen | provisorisch_abgeschlossen | abgeschlossen</summary>
    public string Status { get; set; } = "offen";

    /// <summary>
    /// Zeitpunkt des definitiven Abschlusses (Status → "abgeschlossen").
    /// Wird auch als „Druckdatum" auf nachträglich regenerierten Lohnbelegen
    /// verwendet — sonst nähme PayrollPdfService DateTime.Now und das Datum
    /// auf dem Beleg würde sich bei jedem späteren Ausdruck verschieben.
    /// </summary>
    public DateTime? AbgeschlossenAm { get; set; }
    public int? AbgeschlossenVon { get; set; }

    /// <summary>
    /// Zeitpunkt des provisorischen Abschlusses durch den GF. Wird beim
    /// Status-Wechsel offen → provisorisch_abgeschlossen gesetzt.
    /// </summary>
    public DateTime? ProvisorischAbgeschlossenAm { get; set; }
    public int? ProvisorischAbgeschlossenVon { get; set; }

    /// <summary>
    /// Auszahlungsdatum der MA-Löhne — vom HR beim definitiven Lohnabschluss
    /// erfasst. Wird ins DTA-XML geschrieben (RequestedExecutionDate). Default:
    /// Tag nach Lohnabschluss, kann aber pro Lohnlauf angepasst werden.
    /// </summary>
    public DateOnly? Auszahlungsdatum { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Bemerkung für diese Lohnperiode (erscheint als Fussnote auf den
    /// Lohnabrechnungen aller MA in dieser Periode). Überschreibt den
    /// Default aus CompanyProfile.PdfFooterText.
    /// </summary>
    public string? PdfFooterText { get; set; }

    // ── Akonto-Workflow (Walter-Vorgabe 16.05.2026) ────────────────────────
    // Eigener Status-Strang parallel zum bestehenden Definitiv-Strang
    // (siehe AKONTO-LOHN-PLAN.md, Abschnitt 6/7). Der GF startet die Akonto-
    // Vorbereitung, bestätigt pro MA-Lohnblatt, schickt an HR. HR kontrolliert,
    // gibt frei und löst DTA aus. Status-Flow:
    //   OFFEN → IN_BEARBEITUNG_GF → BEI_HR → HR_FREIGEGEBEN → AUSBEZAHLT

    /// <summary>OFFEN | IN_BEARBEITUNG_GF | BEI_HR | HR_FREIGEGEBEN | AUSBEZAHLT</summary>
    public string AkontoStatus { get; set; } = "OFFEN";

    /// <summary>Wann der GF die Akonto-Vorbereitung gestartet hat (= „Akonto vorbereiten"-Klick).</summary>
    public DateTime? AkontoGfStartedAt { get; set; }
    public int?      AkontoGfStartedBy { get; set; }

    /// <summary>Wann der GF alle Lohnblätter freigegeben und an HR gesendet hat.</summary>
    public DateTime? AkontoGfSentAt    { get; set; }
    public int?      AkontoGfSentBy    { get; set; }

    /// <summary>Wann HR die Final-Freigabe gegeben hat (vor dem DTA-Klick).</summary>
    public DateTime? AkontoHrFreigegebenAt { get; set; }
    public int?      AkontoHrFreigegebenBy { get; set; }

    /// <summary>Wann das Akonto-DTA gelaufen ist (Status → AUSBEZAHLT, Datensätze eingefroren).</summary>
    public DateTime? AkontoAusbezahltAt { get; set; }
    public int?      AkontoAusbezahltBy { get; set; }

    /// <summary>
    /// Bank-Ausführungsdatum des Akonto-DTA (= RequestedExecutionDate im pain.001).
    /// Wird beim „DTA an Bank gesendet"-Bestätigungsschritt erfasst und im DTA-XML
    /// verwendet. Reset-Lock prüft `heute > AkontoAuszahlungsdatum` → 409.
    /// </summary>
    public DateOnly? AkontoAuszahlungsdatum { get; set; }

    /// <summary>ID des Akonto-DTA-Laufs (pain.001). Wird in Etappe 2/3 verdrahtet.</summary>
    public int? AkontoDtaRunId { get; set; }

    public CompanyProfile? Company { get; set; }
    public ICollection<PayrollSnapshot> Snapshots { get; set; } = new List<PayrollSnapshot>();
}

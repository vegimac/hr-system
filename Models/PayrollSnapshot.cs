namespace HrSystem.Models;

/// <summary>
/// Unveränderlicher Lohnzettel-Snapshot nach Periodenabschluss.
/// Enthält den vollständigen berechneten Lohnzettel als JSON — inkl. aller SV-Sätze,
/// Lohnpositionen, Beträge — damit ein Nachdruck Jahre später exakt dem Originalzettel entspricht.
/// </summary>
public class PayrollSnapshot
{
    public int Id { get; set; }

    public int PayrollPeriodeId { get; set; }
    public int EmployeeId { get; set; }
    public int CompanyProfileId { get; set; }

    /// <summary>
    /// Vollständiger Lohnzettel als JSONB. Gespeichert beim Bestätigen, unveränderlich
    /// nach Periodenabschluss. Enthält alle berechneten Zeilen, Sätze, Beträge, Namen etc.
    /// </summary>
    public string SlipJson { get; set; } = "{}";

    // ── Denormalisiert für Jahresausweis-Abfragen (ohne JSON-Parsing) ──────
    public decimal Brutto                { get; set; }
    public decimal Netto                 { get; set; }
    public decimal SvBasisAhv            { get; set; }  // AHV/ALV-pflichtiger Lohn
    public decimal SvBasisBvg            { get; set; }  // BVG-pflichtiger Lohn (vor Koordinationsabzug)
    public decimal QstBetrag             { get; set; }  // Quellensteuer-Abzug (positiver Wert)
    public decimal ThirteenthAccumulated { get; set; }  // Kumulierter 13. ML per Ende dieser Periode
    public decimal FerienGeldSaldo       { get; set; }  // Feriengeldsaldo per Ende dieser Periode

    /// <summary>
    /// Bereits per Akonto ausbezahlter Betrag für diese Periode (Akonto-Lohn-
    /// Modell). Der Definitivlauf zieht diesen Wert vom berechneten Netto ab
    /// → Restzahlung. 0 = kein Akonto erfasst (z.B. MA in Probezeit / Austritt
    /// geplant, oder Akonto-Lauf für diesen Monat nicht durchgeführt).
    /// Siehe AKONTO-LOHN-PLAN.md.
    /// </summary>
    public decimal AkontoBereitsAusbezahlt { get; set; } = 0;

    /// <summary>
    /// Wird true sobald die Periode abgeschlossen ist. Davor: editierbar (re-confirm möglich).
    /// Nach Abschluss: kein Update mehr erlaubt.
    /// </summary>
    public bool IsFinal { get; set; } = false;

    /// <summary>
    /// Per-MA-Status im 4-Augen-Workflow (Walter-Vorgabe 19.05.2026, analog
    /// AkontoZahlung.Status). Ersetzt das frühere binäre „Snapshot vorhanden
    /// = bestätigt"-Schema und ermöglicht GF → HR Workflow.
    ///
    ///   BERECHNET       — Slip berechnet, GF noch nicht freigegeben
    ///                     (Snapshot existiert noch nicht oder wurde aus
    ///                     FREIGEGEBEN_GF zurückgezogen)
    ///   FREIGEGEBEN_GF  — GF hat „Lohn bestätigen" geklickt
    ///   HR_BESTAETIGT   — HR hat per-MA bestätigt
    ///   ABGESCHLOSSEN   — Periode definitiv abgeschlossen (immutable)
    ///   STORNIERT       — nach Abschluss rückgerollt (mit Audit)
    /// </summary>
    // Walter 19.05.2026: Default geändert von FREIGEGEBEN_GF auf BERECHNET.
    // Der frühere Default sorgte dafür, dass neu erstellte Snapshots fälschlich
    // als „GF-bestätigt" markiert waren — sichtbar als ✓-Häkchen pro MA im
    // Definitiv-Tab nach dem Akonto-Lauf, obwohl GF nichts geklickt hat. Der
    // Confirm-Endpoint hebt den Status erst NACH dem „Lohn bestätigen"-Klick
    // auf FREIGEGEBEN_GF.
    public string Status { get; set; } = "BERECHNET";

    /// <summary>Wann der GF dieses Lohnblatt freigegeben hat (NULL = noch nicht).</summary>
    public DateTime? GfFreigegebenAt { get; set; }
    public int?      GfFreigegebenBy { get; set; }
    /// <summary>Wann HR das Lohnblatt bestätigt hat.</summary>
    public DateTime? HrBestaetigtAt { get; set; }
    public int?      HrBestaetigtBy { get; set; }
    /// <summary>Notiz vom GF (z.B. bei ungewöhnlicher Konstellation).</summary>
    public string?   KommentarGf { get; set; }
    /// <summary>Notiz von HR (z.B. wenn HR mit Begründung zurückschickt).</summary>
    public string?   KommentarHr { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public PayrollPeriode? Periode  { get; set; }
    public Employee?        Employee { get; set; }
}

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
    /// Wird true sobald die Periode abgeschlossen ist. Davor: editierbar (re-confirm möglich).
    /// Nach Abschluss: kein Update mehr erlaubt.
    /// </summary>
    public bool IsFinal { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public PayrollPeriode? Periode  { get; set; }
    public Employee?        Employee { get; set; }
}

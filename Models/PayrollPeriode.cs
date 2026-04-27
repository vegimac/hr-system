namespace HrSystem.Models;

/// <summary>
/// Konkrete Lohnperiode für eine Filiale (z.B. März 2026: 21.02.–20.03.2026).
/// Status: offen → abgeschlossen.
/// Nach Abschluss sind alle Zulagen-Einträge gesperrt und Snapshots unveränderlich.
/// </summary>
public class PayrollPeriode
{
    public int Id { get; set; }

    public int CompanyProfileId { get; set; }

    /// <summary>FK auf die Perioden-Konfiguration (null bei Übergangs-Lohnläufen).</summary>
    public int? ConfigId { get; set; }

    /// <summary>Auszahlungsjahr (Jahr in dem der Lohn ausbezahlt wird).</summary>
    public int Year { get; set; }

    /// <summary>Auszahlungsmonat (1–12).</summary>
    public int Month { get; set; }

    /// <summary>Erster Tag dieser konkreten Periode, z.B. 2026-02-21.</summary>
    public DateOnly PeriodFrom { get; set; }

    /// <summary>Letzter Tag dieser konkreten Periode, z.B. 2026-03-20.</summary>
    public DateOnly PeriodTo { get; set; }

    /// <summary>Anzeige-Label, z.B. "März 2026" oder "Abschluss Dezember 2026".</summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// true = Übergangs-Lohnlauf bei Periodenänderung (z.B. 21.12.–31.12. beim Wechsel auf 1.–31.).
    /// Erlaubt zwei Perioden im gleichen Monat.
    /// </summary>
    public bool IsTransition { get; set; } = false;

    /// <summary>offen | abgeschlossen</summary>
    public string Status { get; set; } = "offen";

    public DateTime? AbgeschlossenAm { get; set; }
    public int? AbgeschlossenVon { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Bemerkung für diese Lohnperiode (erscheint als Fussnote auf den
    /// Lohnabrechnungen aller MA in dieser Periode). Überschreibt den
    /// Default aus CompanyProfile.PdfFooterText.
    /// </summary>
    public string? PdfFooterText { get; set; }

    public CompanyProfile? Company { get; set; }
    public PayrollPeriodeConfig? Config { get; set; }
    public ICollection<PayrollSnapshot> Snapshots { get; set; } = new List<PayrollSnapshot>();
}

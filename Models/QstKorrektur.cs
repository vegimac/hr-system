namespace HrSystem.Models;

/// <summary>
/// K1 QST-Korrektur (Walter 29.08.2026, docs/qst-korrektur-konzept.md):
/// Ein Posten pro MA und ABGESCHLOSSENEM Monat, wenn eine rückwirkende
/// QST-Version die Steuer verändert. Snapshots bleiben eingefroren — der
/// Posten trägt alt/neu/Differenz und wird im Folgemonat-Lohnlauf
/// verrechnet (K2), in der Kantons-Abrechnung ausgewiesen und ab E6 als
/// Swissdec-Korrekturmeldung übermittelt.
/// </summary>
public class QstKorrektur
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public int CompanyProfileId { get; set; }

    /// <summary>Korrigierter Monat (der abgeschlossene Lohnmonat).</summary>
    public int Jahr { get; set; }
    public int Monat { get; set; }

    /// <summary>QST-Version, mit der damals gerechnet wurde (null = keine Erfassung).</summary>
    public int? AlteVersionId { get; set; }
    /// <summary>Die rückwirkend erfasste neue Version.</summary>
    public int NeueVersionId { get; set; }

    public string? AlterCode { get; set; }
    public string? NeuerCode { get; set; }

    /// <summary>QST damals (aus dem eingefrorenen Snapshot, inkl. bereits verrechneter Korrekturen).</summary>
    public decimal AlterBetrag { get; set; }
    /// <summary>QST nach neuer Version (nachgerechnet auf derselben Basis).</summary>
    public decimal NeuerBetrag { get; set; }
    /// <summary>Neu − Alt: positiv = Nachbelastung MA, negativ = Erstattung.</summary>
    public decimal Differenz { get; set; }

    /// <summary>Basis (IST-Brutto) und satzbestimmender Lohn der Nachrechnung.</summary>
    public decimal Basis { get; set; }
    public decimal SatzBasis { get; set; }

    /// <summary>OFFEN → VERRECHNET / IN_DARLEHEN → GEMELDET · VORJAHR = nicht via Lohnlauf.</summary>
    public string Status { get; set; } = "OFFEN";

    /// <summary>Pflicht-Grund der rückwirkenden Erfassung («Heirat verspätet gemeldet …»).</summary>
    public string Grund { get; set; } = "";

    /// <summary>Lohnperiode, in der die Verrechnung erfolgte (K2).</summary>
    public int? VerrechnetPeriodeId { get; set; }
    public DateTime? VerrechnetAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? CreatedBy { get; set; }
}

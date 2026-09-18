namespace HrSystem.Models;

/// <summary>
/// Familienzulagen-Korrektur (Walter 18.09.2026, analog QST K1):
/// Ein Posten pro Kind/Zulage und ABGESCHLOSSENEM Monat, wenn «Gültig ab»
/// vor «Erfahren am» liegt (Nachzahlung) oder die Zulage rückwirkend endet
/// (Rückforderung). Snapshots bleiben eingefroren; Verrechnung im
/// Erfahrungsmonat / nächsten offenen Lohnlauf.
/// Betrag &gt; 0 = zuwenig bezahlt (Nachzahlung), &lt; 0 = zuviel (Rückforderung).
/// </summary>
public class FamzKorrektur
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public int CompanyProfileId { get; set; }
    public int FamilyMemberId { get; set; }
    public int AllowanceId { get; set; }

    public int Jahr { get; set; }
    public int Monat { get; set; }

    /// <summary>Was damals auf dem Beleg stand (meist 0 bei verspäteter Erfassung).</summary>
    public decimal AlterBetrag { get; set; }
    /// <summary>Soll-Monatsbetrag nach Gültigkeit/Tarif.</summary>
    public decimal NeuerBetrag { get; set; }
    /// <summary>Neu − Alt: positiv = Nachzahlung, negativ = Rückforderung.</summary>
    public decimal Betrag { get; set; }

    public string? AllowanceType { get; set; }
    public string? ChildName { get; set; }

    /// <summary>OFFEN → VERRECHNET · VORJAHR = nicht via Lohnlauf.</summary>
    public string Status { get; set; } = "OFFEN";

    public string Grund { get; set; } = "";

    public int? VerrechnetPeriodeId { get; set; }
    public DateTime? VerrechnetAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? CreatedBy { get; set; }
}

namespace HrSystem.Models;

/// <summary>
/// Familienzulagen-Prüffall (Walter 18.09.2026, seit 11.10.2026 nur noch Warnung):
/// Ein Posten pro Kind/Zulage und früherem Monat, wenn «Gültig ab» vor
/// «Erfahren am» liegt (Nachzahlung) oder die Zulage rückwirkend endet
/// (Rückforderung). Nicht automatisch bezahlt — der Lohnlauf zeigt eine
/// Warnung, die quittiert werden muss (als Zulage übernehmen / nicht nötig).
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

    /// <summary>
    /// OFFEN = Warnung im Lohnlauf (sperrt «Lohn bestätigen») → ERLEDIGT (quittiert).
    /// Altbestand: VERRECHNET (bis 11.10.2026 automatisch bezahlt), VORJAHR (seit Stand 59 OFFEN).
    /// </summary>
    public string Status { get; set; } = "OFFEN";

    /// <summary>UEBERNOMMEN (als Zulage im Lohn erfasst) oder NICHT_NOETIG.</summary>
    public string? ErledigtArt { get; set; }
    /// <summary>Die beim Übernehmen angelegte Zulage; wird sie gelöscht, ist die Warnung wieder offen.</summary>
    public int? LohnZulageId { get; set; }
    public string? Bemerkung { get; set; }
    public DateTime? ErledigtAm { get; set; }
    public string? ErledigtVon { get; set; }

    public string Grund { get; set; } = "";

    public int? VerrechnetPeriodeId { get; set; }
    public DateTime? VerrechnetAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? CreatedBy { get; set; }
}

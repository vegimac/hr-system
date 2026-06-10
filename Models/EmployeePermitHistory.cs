namespace HrSystem.Models;

/// <summary>
/// Bewilligungs-Verlauf pro Mitarbeiter. Eintrag pro Verlängerung,
/// Wechsel des Ausweistyps oder Einbürgerung.
///
/// Aktueller Eintrag: <see cref="ValidTo"/> = NULL und
/// <see cref="ValidFrom"/> &lt;= heute.
///
/// Einbürgerung: <see cref="PermitTypeId"/> = NULL +
/// <see cref="Note"/> = "Einbürgerung am ...".
/// </summary>
public class EmployeePermitHistory
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public int? PermitTypeId { get; set; }       // NULL = keine Bewilligung mehr (CH-Bürger / Einbürgerung)
    public DateOnly  ValidFrom { get; set; }
    // Walter-Vorgabe 01.06.2026: ValidTo = behördliches Ablauf-Datum auf dem Ausweis.
    // Bei normalen Bewilligungs-Einträgen IMMER gesetzt. NULL nur zulässig für
    // CH-Bürger-/Einbürgerungs-Einträge (PermitTypeId IS NULL).
    public DateOnly? ValidTo   { get; set; }
    // PermitExpiryDate entfernt 01.06.2026 — war Duplikat von ValidTo.
    public string?   Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int?     CreatedByUserId { get; set; }

    public Employee?   Employee   { get; set; }
    public PermitType? PermitType { get; set; }
}

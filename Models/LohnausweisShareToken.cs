namespace HrSystem.Models;

/// <summary>
/// Öffentlicher Download-Link für den Jahres-Lohnausweis an eine Behörde
/// (Walter 30.07.2026, Lohnabtretung). Klartext-Token nur im Link, in der DB
/// SHA-256-Hash. Landing-Page zuerst — PDF erst per Button (kein Anhang in der Mail).
/// </summary>
public class LohnausweisShareToken
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public int BehoerdeId { get; set; }
    public int EmployeeLohnAssignmentId { get; set; }
    public int? PayrollPeriodeId { get; set; }

    /// <summary>Kalenderjahr des Lohnausweises (Form 11).</summary>
    public int Year { get; set; }

    public string TokenHash { get; set; } = "";

    public DateTime ExpiresAt { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }

    public Employee? Employee { get; set; }
    public Behoerde? Behoerde { get; set; }
    public EmployeeLohnAssignment? Assignment { get; set; }
}

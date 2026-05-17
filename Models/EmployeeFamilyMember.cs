namespace HrSystem.Models;

public class EmployeeFamilyMember
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }

    public string MemberType { get; set; } = "Kind"; // Kind, Ehepartner, Mutter, Vater, Sonstige
    public string? Gender { get; set; }
    public string? FamilyStatus { get; set; }

    public string? LastName { get; set; }
    public string? MaidenName { get; set; }
    public string? FirstName { get; set; }

    public string? SocialSecurityNumber { get; set; }
    public bool LivesInSwitzerland { get; set; } = false;

    public DateTime? DateOfBirth { get; set; }
    public DateTime? DateOfDeath { get; set; }

    // Legacy-Felder. Bleiben in der DB stehen, werden im Frontend aber nicht
    // mehr angezeigt — Zulagen sind nun zeitlich versioniert in family_member_allowance.
    public DateTime? Allowance1Until { get; set; }
    public DateTime? Allowance2Until { get; set; }
    public DateTime? Allowance3Until { get; set; }

    /// <summary>Versionierte Familienzulagen-Einträge (Von/Bis/Monatsbetrag).</summary>
    public List<FamilyMemberAllowance> Allowances { get; set; } = new();

    public int? AlternativeAddressId { get; set; }

    public DateTime? QstDeductibleFrom { get; set; }
    public DateTime? QstDeductibleUntil { get; set; }

    /// <summary>
    /// Aufenthaltsbewilligung des Familienangehörigen (B/C/L/G/F/N) —
    /// referenziert PermitType wie beim MA selbst.
    /// </summary>
    public int? PermitTypeId { get; set; }

    /// <summary>Ablaufdatum der Bewilligung (Gültig bis).</summary>
    public DateTime? PermitExpiryDate { get; set; }

    /// <summary>
    /// ZEMIS-Nummer (Zentrales Migrationsinformationssystem) des Familien-
    /// angehörigen — bleibt während des ganzen Aufenthalts gleich, auch wenn
    /// die Bewilligung wechselt.
    /// </summary>
    public string? ZemisNumber { get; set; }

    /// <summary>Nationalität (FK auf Nationality-Tabelle, wie beim MA).</summary>
    public int? NationalityId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Employee? Employee { get; set; }
    public PermitType? PermitType { get; set; }
    public Nationality? NationalityRef { get; set; }
}

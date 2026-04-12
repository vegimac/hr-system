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

    public DateTime? Allowance1Until { get; set; }
    public DateTime? Allowance2Until { get; set; }
    public DateTime? Allowance3Until { get; set; }

    public int? AlternativeAddressId { get; set; }

    public DateTime? QstDeductibleFrom { get; set; }
    public DateTime? QstDeductibleUntil { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Employee? Employee { get; set; }
}

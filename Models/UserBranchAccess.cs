namespace HrSystem.Models;

public class UserBranchAccess
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int CompanyProfileId { get; set; }

    public AppUser User { get; set; } = null!;
    public CompanyProfile CompanyProfile { get; set; } = null!;
}

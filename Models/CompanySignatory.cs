namespace HrSystem.Models;

public class CompanySignatory
{
    public int Id { get; set; }
    public int CompanyProfileId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? FunctionTitle { get; set; }

    /// <summary>
    /// Rolle der Person: GESCHAEFTSFUEHRER, HR_VERANTWORTLICH, BUCHHALTUNG, SONSTIGES
    /// </summary>
    public string? Role { get; set; }

    public bool IsDefault { get; set; } = false;
    public bool IsActive { get; set; } = true;

    public CompanyProfile? CompanyProfile { get; set; }
}

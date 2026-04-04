namespace HrSystem.Models;

public class MinimumWageRuleNew
{
    public int Id { get; set; }

    public string JobGroupCode { get; set; } = "";
    public string EmploymentModelCode { get; set; } = "";

    public int EducationLevelId { get; set; }

    public string SalaryType { get; set; } = ""; // hourly / monthly

    public decimal Amount { get; set; }

    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    public bool IsActive { get; set; }

    public EducationLevel? EducationLevel { get; set; }
}
namespace HrSystem.Models;

public class EmployeeImportSnapshot
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public string? JobGroupCode { get; set; }
    public string? EmploymentModel { get; set; }
    public string? ContractType { get; set; }

    public decimal? HourlyRate { get; set; }
    public decimal? MonthlySalary { get; set; }
    public decimal? WeeklyHours { get; set; }

    public string? JobTitle { get; set; }

    public string? NationalityCode { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;

    public Employee? Employee { get; set; }
}
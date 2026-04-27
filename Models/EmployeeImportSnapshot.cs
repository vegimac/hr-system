namespace HrSystem.Models;

public class EmployeeImportSnapshot
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public string? JobGroupCode { get; set; }
    public string? EmploymentModel { get; set; }
    public string? ContractType { get; set; }

    public decimal? HourlyRate { get; set; }
    public decimal? MonthlySalaryFte { get; set; }   // 100%-Lohn aus Import
    public decimal? MonthlySalary { get; set; }       // tatsächlicher Lohn (nach Pensum)
    public decimal? WeeklyHours { get; set; }

    /// <summary>Pensum in % (z.B. 80 für 80%) – aus Anzahl-Spalte wenn "80%" steht</summary>
    public decimal? EmploymentPercentage { get; set; }

    /// <summary>Vertragsende – wenn gesetzt = befristet</summary>
    public DateOnly? ContractEndDate { get; set; }

    public string? JobTitle { get; set; }

    public string? NationalityCode { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;

    public Employee? Employee { get; set; }
    public string? Gender { get; set; }
}

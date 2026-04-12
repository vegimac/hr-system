using System.Text.Json.Serialization;

namespace HrSystem.Models;

public class Employment
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public int? CompanyProfileId { get; set; }

    [JsonIgnore]
    public CompanyProfile? CompanyProfile { get; set; }

    public string EmploymentModel { get; set; } = "";
    public string SalaryType { get; set; } = "";

    public DateTime ContractStartDate { get; set; }
    public DateTime? ContractEndDate { get; set; }

    public string? JobTitle { get; set; }
    public string? ContractType { get; set; }

    public decimal? EmploymentPercentage { get; set; }
    public decimal? WeeklyHours { get; set; }
    public decimal? GuaranteedHoursPerWeek { get; set; }

    public decimal? MonthlySalaryFte { get; set; }   // 100%-Lohn (Vollpensum-Referenz)
    public decimal? MonthlySalary { get; set; }       // tatsächlicher Lohn (nach Pensum)
    public decimal? HourlyRate { get; set; }

    public decimal? VacationPercent { get; set; }
    public decimal? HolidayPercent { get; set; }
    public decimal? ThirteenthSalaryPercent { get; set; }

    public string? VacationPaymentMode { get; set; }

    public int? ProbationPeriodMonths { get; set; }
    public DateTime? ProbationEndDate { get; set; }

    public bool IsActive { get; set; } = true;

    [JsonIgnore]
    public Employee? Employee { get; set; }
}
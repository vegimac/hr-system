namespace HrSystem.Models;

public class ComplianceCheckLiveRequest
{
    public string? JobGroupCode { get; set; }
    public string? EducationLevelCode { get; set; }
    public DateTime EffectiveDate { get; set; }
    public string? EmploymentModel { get; set; }
    public decimal? HourlyRate { get; set; }
    public decimal? MonthlySalary { get; set; }
    public decimal? EmploymentPercentage { get; set; } // z.B. 80 für 80%

    /// <summary>
    /// Geburtsdatum des MA (optional). Wird genutzt, um altersabhängige
    /// Mindestlohn-Regeln (z.B. unter 18 Jahre) auszuwählen. Wenn null,
    /// werden nur Regeln ohne age_max-Filter angewendet.
    /// </summary>
    public DateTime? BirthDate { get; set; }
}
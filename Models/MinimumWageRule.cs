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

    /// <summary>
    /// Maximales Alter (inklusiv). NULL = keine Altersgrenze.
    /// Beispiel: AgeMax=17 → Regel gilt bis zum 18. Geburtstag.
    /// L-GAV Anhang II hat Sonderregeln für Jugendliche.
    /// </summary>
    public int? AgeMax { get; set; }

    public EducationLevel? EducationLevel { get; set; }
}
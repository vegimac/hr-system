using System.Text.Json.Serialization;

namespace HrSystem.Models;

public class EmployeeEducationHistory
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public int EducationLevelId { get; set; }

    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    public string? Note { get; set; }

    public bool IsActive { get; set; } = true;

    [JsonIgnore]
    public Employee? Employee { get; set; }

    public EducationLevel? EducationLevel { get; set; }
}
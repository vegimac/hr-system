namespace HrSystem.Models;

public class EmployeeTimeEntry
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }

    public DateOnly EntryDate { get; set; }
    public DateTime TimeIn { get; set; }
    public DateTime? TimeOut { get; set; }

    public string? Comment { get; set; }

    public decimal? DurationHours { get; set; }
    public decimal? NightHours { get; set; }
    public decimal? TotalHours { get; set; }

    /// <summary>"manual" or "import"</summary>
    public string Source { get; set; } = "manual";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Audit-Felder: werden beim ersten Bearbeiten gesetzt
    public DateTime? OriginalTimeIn  { get; set; }
    public DateTime? OriginalTimeOut { get; set; }
    public string?   EditedBy        { get; set; }
    public DateTime? EditedAt        { get; set; }

    public Employee? Employee { get; set; }
}

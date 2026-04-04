namespace HrSystem.Models;

public class JobGroup
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
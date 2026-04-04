namespace HrSystem.Models;

public class PermitType
{
    public int Id { get; set; }

    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string PersonGroup { get; set; } = "";

    public bool IsActive { get; set; } = true;
}
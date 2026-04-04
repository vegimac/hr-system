using System.Text.Json.Serialization;

namespace HrSystem.Models;

public class CompanySignatory
{
    public int Id { get; set; }

    public int CompanyProfileId { get; set; }

    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FunctionTitle { get; set; } = "";

    public bool IsDefault { get; set; } = false;
    public bool IsActive { get; set; } = true;

    [JsonIgnore]
    public CompanyProfile? CompanyProfile { get; set; }
}
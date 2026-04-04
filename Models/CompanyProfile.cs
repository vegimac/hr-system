using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;

namespace HrSystem.Models;

public class CompanyProfile
{
    public int Id { get; set; }

    public string CompanyName { get; set; } = "";

    [Column("branch_name")]
    public string? BranchName { get; set; }

    public string? RestaurantCode { get; set; }

    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }

    public string? Phone { get; set; }
    public string? Email { get; set; }

    public decimal? NormalWeeklyHours { get; set; }
    public int? DefaultVacationWeeks { get; set; }
    public string? WorkLocation { get; set; }

    public int? PayrollPeriodStartDay { get; set; }
    public decimal? MaxPartTimeHoursPerWeek { get; set; }
    public bool AllowFirst3Months8PercentReduction { get; set; } = false;
    public bool HoldBackVacationPayout { get; set; } = true;

    public int? NoticePeriodDuringProbationDays { get; set; }
    public int? NoticePeriodAfterProbationMonths { get; set; }
    public int? NoticePeriodFromTenthYearMonths { get; set; }

    public decimal? MinimumWageUnder18Monthly { get; set; }
    public decimal? MinimumWageUnder18Hourly { get; set; }

    public int? SelectedContractTemplateId { get; set; }

    public bool IsActive { get; set; } = true;

    public List<CompanySignatory> Signatories { get; set; } = new();

    [JsonIgnore]
    [NotMapped]
    public string FullDisplayName =>
        string.IsNullOrWhiteSpace(BranchName)
            ? CompanyName
            : $"{CompanyName} {BranchName}";
}

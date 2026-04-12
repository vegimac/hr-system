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

    // NEU: Lohnzuschläge
    public decimal? DefaultVacationPercent5Weeks { get; set; } = 10.64m;
    public decimal? DefaultVacationPercent6Weeks { get; set; } = 13.04m;
    public decimal? DefaultHolidayPercent { get; set; } = 2.27m;

    // Nachtstunden-Grenzen (Format "HH:mm", z.B. "00:00" und "07:00")
    public string? NightStartTime { get; set; } = "00:00";
    public string? NightEndTime   { get; set; } = "07:00";

    public bool IsActive { get; set; } = true;

    // ── Zwischenverdienst / Behörden ─────────────────────────────────────────
    /// <summary>BUR-Nummer (Betriebseinheitenregister), Format CH-XXX.X.XXX.XXX-X</summary>
    public string? BurNummer { get; set; }

    /// <summary>UID-Nummer (Unternehmens-Identifikationsnummer), Format CHE-XXX.XXX.XXX</summary>
    public string? UidNummer { get; set; }

    /// <summary>NOGA-Branchen-Code (2–5 Stellen)</summary>
    public string? BranchenCode { get; set; }

    /// <summary>Name und Nummer der AHV-Ausgleichskasse</summary>
    public string? AhvKasse { get; set; }

    /// <summary>Name des BVG-Versicherers</summary>
    public string? BvgVersicherer { get; set; }

    /// <summary>Gesamtarbeitsvertrag (GAV) dem der Betrieb unterstellt ist</summary>
    public string? GavName { get; set; }

    /// <summary>true = Betrieb ist einem GAV unterstellt</summary>
    public bool IstGav { get; set; } = false;

    public List<CompanySignatory> Signatories { get; set; } = new();

    [JsonIgnore]
    [NotMapped]
    public string FullDisplayName =>
        string.IsNullOrWhiteSpace(BranchName)
            ? CompanyName
            : $"{CompanyName} {BranchName}";
}
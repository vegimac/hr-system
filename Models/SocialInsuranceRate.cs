namespace HrSystem.Models;

public class SocialInsuranceRate
{
    public int      Id                    { get; set; }
    public string   Code                  { get; set; } = "";   // AHV | ALV | NBUV | BVG
    public string   Name                  { get; set; } = "";   // AHV / IV / EO
    public string?  Description           { get; set; }
    public decimal  Rate                  { get; set; }          // Prozentsatz AN-Anteil
    public string   BasisType             { get; set; } = "gross"; // gross | bvg_basis | coord_deduction
    public string?  EmploymentModelCode   { get; set; }            // NULL = alle | PARTTIME | MTP | FIX | FIX-M
    public int?     MinAge                { get; set; }
    public int?     MaxAge                { get; set; }
    public decimal? FreibetragMonthly     { get; set; }          // AHV 65+
    public decimal? CoordinationDeduction { get; set; }          // BVG Koordinationsabzug/Mt.
    public bool     OnlyQuellensteuer     { get; set; }
    public DateOnly ValidFrom             { get; set; }
    public DateOnly? ValidTo              { get; set; }
    public int      SortOrder             { get; set; } = 99;
    public bool     IsActive              { get; set; } = true;
    public DateTime CreatedAt             { get; set; } = DateTime.UtcNow;
}

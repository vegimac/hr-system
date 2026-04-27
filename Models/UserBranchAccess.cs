namespace HrSystem.Models;

public class UserBranchAccess
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int CompanyProfileId { get; set; }

    /// <summary>
    /// Rolle in dieser Filiale:
    /// GESCHAEFTSFUEHRER, HR_VERANTWORTLICH, REGIONALLEITER, SONSTIGES
    /// </summary>
    public string? Role { get; set; }

    /// <summary>Funktion/Titel in dieser Filiale, z.B. "Geschäftsführerin", "HR-Leiterin"</summary>
    public string? FunctionTitle { get; set; }

    /// <summary>Ist diese Person der Standard-Unterzeichner für diese Filiale?</summary>
    public bool IsDefault { get; set; } = false;

    public AppUser User { get; set; } = null!;
    public CompanyProfile CompanyProfile { get; set; } = null!;
}

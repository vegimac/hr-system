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

    /// <summary>Darf diese Person den Manager-Dienstplan dieser Filiale planen
    /// (Walter 08.08.2026)? Admin darf immer überall.</summary>
    public bool CanDienstplan { get; set; }

    /// <summary>Darf Vertrags-SMS-Links dieser Filiale senden/verwalten (Walter 10.08.2026).</summary>
    public bool CanVertragSms { get; set; }

    /// <summary>Ist diese Person der Standard-Unterzeichner für diese Filiale?</summary>
    public bool IsDefault { get; set; } = false;

    public AppUser User { get; set; } = null!;
    public CompanyProfile CompanyProfile { get; set; } = null!;
}

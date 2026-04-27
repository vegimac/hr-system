namespace HrSystem.Models;

public class AppUser
{
    public int Id { get; set; }

    public string Username { get; set; } = "";

    /// <summary>Vorname</summary>
    public string? FirstName { get; set; }

    /// <summary>Nachname</summary>
    public string? LastName { get; set; }

    public string Email { get; set; } = "";

    /// <summary>Telefon / Mobile</summary>
    public string? Phone { get; set; }

    public string PasswordHash { get; set; } = "";

    /// <summary>admin | superuser | user</summary>
    public string Role { get; set; } = "user";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<UserBranchAccess> BranchAccess { get; set; } = new();

    /// <summary>Anzeigename: Vor- + Nachname, Fallback: Username</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName)
            ? Username
            : $"{FirstName} {LastName}".Trim();
}

namespace HrSystem.Models;

public class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";

    /// <summary>admin | superuser | user</summary>
    public string Role { get; set; } = "user";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<UserBranchAccess> BranchAccess { get; set; } = new();
}

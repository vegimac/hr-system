namespace HrSystem.Models;

/// <summary>
/// Öffentlicher Token-Link zur Bewilligungs-Erinnerung OHNE Login
/// (Walter 19.07.2026). Analog ContractShare / Moments: SMS enthält nur einen
/// kurzen Push + Link; die lange Mitteilung liegt auf der Landing-Page.
/// Klartext-Token nur im Link — in der DB ausschliesslich SHA-256-Hash.
/// </summary>
public class PermitReminderToken
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public int PermitHistoryId { get; set; }

    /// <summary>SHA-256-Hex des Tokens.</summary>
    public string TokenHash { get; set; } = "";

    /// <summary>Aufgelöste Mitteilung (HTML-sicher, bereits Platzhalter ersetzt).</summary>
    public string MessageHtml { get; set; } = "";

    public string? Title { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }
}

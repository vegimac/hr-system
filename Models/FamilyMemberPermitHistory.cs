namespace HrSystem.Models;

/// <summary>
/// Bewilligungs-Verlauf eines Familienangehörigen (Ehepartner) — analog
/// <see cref="EmployeePermitHistory"/>, mit Wirkung (ValidFrom) und Wissen
/// (ErfahrenAm) für rückwirkende QST (Walter 08.10.2026).
/// </summary>
public class FamilyMemberPermitHistory
{
    public int Id { get; set; }
    public int FamilyMemberId { get; set; }

    /// <summary>NULL = keine Bewilligung / CH-Bürger / Einbürgerung.</summary>
    public int? PermitTypeId { get; set; }

    /// <summary>Wirkung: ab wann die Bewilligung gilt.</summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>Wissen: ab wann wir sie kannten. NULL = gleich ValidFrom.</summary>
    public DateOnly? ErfahrenAm { get; set; }

    /// <summary>Behördliches Ablaufdatum (Gültig bis). NULL bei CH-Bürger.</summary>
    public DateOnly? ValidTo { get; set; }

    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Rückverweis nie serialisieren: PUT family/{id} liefert das Mitglied mit
    // geladener Historie zurück → sonst Endlos-Zyklus (HTTP 500).
    [System.Text.Json.Serialization.JsonIgnore]
    public EmployeeFamilyMember? FamilyMember { get; set; }
    public PermitType? PermitType { get; set; }
}

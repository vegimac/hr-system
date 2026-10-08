namespace HrSystem.Models;

/// <summary>
/// Erwerbstätigkeit-/Arbeitgeber-Verlauf eines Familienangehörigen
/// (Ehepartner / Konkubinatspartner) — Wirkung (ValidFrom) + Wissen
/// (ErfahrenAm) für Tarif B↔C und rückwirkende QST (Walter 08.10.2026).
/// </summary>
public class FamilyMemberErwerbHistory
{
    public int Id { get; set; }
    public int FamilyMemberId { get; set; }

    /// <summary>NULL = Frage offen; true/false = erfasst.</summary>
    public bool? Erwerbstaetig { get; set; }

    public string? ArbeitgeberName { get; set; }
    public string? ArbeitgeberStrasse { get; set; }
    public string? ArbeitgeberPlz { get; set; }
    public string? ArbeitgeberOrt { get; set; }
    public string? ArbeitgeberKanton { get; set; }
    public DateOnly? Stellenantritt { get; set; }

    /// <summary>Wirkung: ab wann dieser Erwerbs-Stand gilt.</summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>Wissen: ab wann wir ihn kannten. NULL = gleich ValidFrom.</summary>
    public DateOnly? ErfahrenAm { get; set; }

    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public EmployeeFamilyMember? FamilyMember { get; set; }
}

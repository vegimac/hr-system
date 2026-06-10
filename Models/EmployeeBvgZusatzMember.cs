namespace HrSystem.Models;

/// <summary>
/// Mitgliedschaft eines Mitarbeiters im BVG-Zusatz-Vorsorge-Programm.
/// Walter-Vorgabe 26.05.2026: pro MA versioniert (mehrere Einträge möglich —
/// MA kann rein, raus, später wieder rein). Der Lohnlauf rechnet
/// BVG_ZUSATZ-Beiträge NUR für MA, die am Periodenanfang eine offene
/// Mitgliedschaft haben (egal welches Vertragsmodell — vorher war das
/// hartcodiert auf FIX-M).
/// </summary>
public class EmployeeBvgZusatzMember
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    /// <summary>Mitgliedschaft gilt ab diesem Datum (Pflicht).</summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>Mitgliedschaft gilt bis. NULL = laufend/aktiv.</summary>
    public DateOnly? ValidTo { get; set; }

    /// <summary>Optionale freie Notiz (z.B. „Beförderung Restaurant-Manager 1.1.2026").</summary>
    public string? Bemerkung { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }

    // Navigation
    public Employee? Employee { get; set; }
}

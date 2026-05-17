namespace HrSystem.Models;

/// <summary>
/// Familienzulage pro Familienmitglied, zeitlich versioniert.
///
/// Walter-Anforderung: pro Kind/Familienmitglied können sich die Zulagen über
/// Lebensstufen ändern (Kinderzulage → Ausbildungszulage, oder kantonal
/// unterschiedliche Beträge). Statt fixer Slots „Zulage 1/2/3 bis" gibt's
/// jetzt beliebig viele Einträge mit Von/Bis/Monatsbetrag — bei einer
/// Änderung legt Walter einfach einen neuen Eintrag mit neuem Gültig-ab an.
///
/// Eindeutigkeit ist absichtlich NICHT erzwungen — theoretisch könnten
/// gleichzeitig mehrere Zulagen-Arten parallel laufen (z.B. KZ + zusätzliche
/// kantonale Zulage). Der Lohnberechnungs-Code summiert alle aktiven Einträge.
/// </summary>
public class FamilyMemberAllowance
{
    public int Id { get; set; }

    public int FamilyMemberId { get; set; }
    public EmployeeFamilyMember? FamilyMember { get; set; }

    /// <summary>Gültig ab Datum (inklusive).</summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>Gültig bis Datum (inklusive). NULL = offen / läuft weiter.</summary>
    public DateOnly? ValidTo { get; set; }

    /// <summary>Monatlicher Betrag in CHF (z.B. 215.00 für Kinderzulage Kanton LU).</summary>
    public decimal MonthlyAmount { get; set; }

    /// <summary>
    /// Optional Zulagenart als Freitext-Code, z.B. "KZ" (Kinderzulage),
    /// "AZ" (Ausbildungszulage). Reine Information, beeinflusst aktuell
    /// nichts an der Berechnung.
    /// </summary>
    public string? AllowanceType { get; set; }

    /// <summary>Optionale Bemerkung.</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

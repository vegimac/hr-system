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
    /// Zulagenart als Code: "KZ" (Kinderzulage), "AZ" (Ausbildungszulage),
    /// "GZ" (Geburtszulage), "AdoptZ" (Adoptionszulage).
    /// </summary>
    public string? AllowanceType { get; set; }

    /// <summary>
    /// Walter-Vorgabe 28.05.2026: Konkreter Tarif-Satz innerhalb der Kategorie.
    /// 1 = Satz 1 (jüngere Kinder), 2 = Satz 2 (z.B. ab 12 J.).
    /// NULL bei Pauschal-Zulagen (GZ/AdoptZ — kein Satz, da Pauschalbetrag)
    /// oder bei Alt-Daten vor Umstellung.
    ///
    /// Die Engine schaut PRO LOHNPERIODE: welcher Allowance-Eintrag ist
    /// gültig, welcher Satz ist gewählt — und holt den daraus resultierenden
    /// Wert aus dem aktuell gültigen FAK-Tarif der Filiale (Systemtabelle).
    /// So greifen Tarif-Änderungen (z.B. neue Sätze ab 1.1.2026) automatisch.
    /// </summary>
    public int? TarifSatzNr { get; set; }

    /// <summary>Optionale Bemerkung.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Walter-Vorgabe 19.07.2026: FAK-/Entscheidungsdokument aus dem MA-Dossier
    /// (z.B. Typ «Kinderzulagen»). NULL = kein Beleg verknüpft.
    /// </summary>
    public int? DokumentId { get; set; }
    public EmployeeDokument? Dokument { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

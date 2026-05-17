namespace HrSystem.Models;

/// <summary>
/// Akonto-Auszahlungsdatum pro Filiale, Jahr und Monat.
///
/// Hintergrund: Beim Akonto-Lohn-Modell bleibt die Lohnperiode immer der
/// Kalendermonat (1.–Letzter), aber rund eine Woche vor Monatsende fliesst
/// eine Akonto-Vorauszahlung. Weil ein fixer Tag (z.B. „immer der 23.") an
/// Wochenenden/Feiertagen scheitert, wird das tatsächliche Auszahlungsdatum
/// pro Monat einzeln hinterlegt — pro Filiale, ein Datensatz je Monat.
///
/// Siehe AKONTO-LOHN-PLAN.md, Abschnitt 4.1.
/// </summary>
public class AkontoTermin
{
    public int Id { get; set; }

    public int CompanyProfileId { get; set; }

    /// <summary>Kalenderjahr.</summary>
    public int Year { get; set; }

    /// <summary>Kalendermonat (1–12) — entspricht der Lohnperiode.</summary>
    public int Month { get; set; }

    /// <summary>Tatsächliches Akonto-Auszahlungsdatum (Bankarbeitstag).</summary>
    public DateOnly PayoutDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public CompanyProfile? Company { get; set; }
}

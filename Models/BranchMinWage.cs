namespace HrSystem.Models;

/// <summary>
/// Kommunaler/städtischer Mindestlohn pro Filiale (Walter-Vorgabe 23.05.2026).
/// In manchen Schweizer Städten gilt ein eigener Mindestlohn. Erfasst wird der
/// **Jahreslohn** (brutto); Monats- und Stundenlohn werden daraus mit der
/// hinterlegten Formel berechnet:
///   • Monatslohn (100 %) = Jahreslohn / 13   (13 Löhne)
///   • Stundenlohn        = Jahreslohn / 52 / Wochenstunden der Filiale
/// Dieser Filial-Mindestlohn hebt den L-GAV-Mindestlohn NACH OBEN
/// (effektives Minimum = max(L-GAV, Filial-Floor)) — er senkt nie einen schon
/// höheren L-GAV-Satz. Versioniert über ValidFrom/ValidTo (Generationen).
/// </summary>
public class BranchMinWage
{
    public int Id { get; set; }

    public int CompanyProfileId { get; set; }

    /// <summary>Jahreslohn (brutto, 100 %).</summary>
    public decimal AnnualSalary { get; set; }

    /// <summary>Gilt der Filial-Mindestlohn auch für Jugendliche (&lt; 18)?</summary>
    public bool AppliesToYouth { get; set; }

    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

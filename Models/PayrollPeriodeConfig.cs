namespace HrSystem.Models;

/// <summary>
/// Konfiguration der Lohnperiode pro Filiale (z.B. from_day=21, to_day=20 → 21.–20. des Folgemonats).
/// Einmal gesperrt (is_locked=true), sobald die erste payroll_periode damit erstellt wurde.
/// Periodenänderung erfordert neuen Konfig-Eintrag für ein zukünftiges Jahr.
/// </summary>
public class PayrollPeriodeConfig
{
    public int Id { get; set; }

    public int CompanyProfileId { get; set; }

    /// <summary>Erster Tag der Periode (1–28). Z.B. 21 für 21.–20.</summary>
    public int FromDay { get; set; } = 1;

    /// <summary>Letzter Tag der Periode (1–31). Z.B. 20 für 21.–20. oder 31 für 1.–31.</summary>
    public int ToDay { get; set; } = 31;

    /// <summary>Gültig ab diesem Kalenderjahr.</summary>
    public int ValidFromYear { get; set; }

    /// <summary>Gültig ab diesem Monat (1–12). Standard: 1 = Januar.</summary>
    public int ValidFromMonth { get; set; } = 1;

    /// <summary>
    /// true = gesperrt. Wird automatisch gesetzt sobald die erste payroll_periode mit dieser
    /// Konfiguration erstellt wurde. Danach sind FromDay/ToDay nicht mehr änderbar.
    /// </summary>
    public bool IsLocked { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public CompanyProfile? Company { get; set; }
}

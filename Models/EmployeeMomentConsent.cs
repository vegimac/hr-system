namespace HrSystem.Models;

/// <summary>
/// Freigabe eines Mitarbeitenden für OneCrew Moments (Walter-Vorgabe 30.06.2026).
/// Eine Zeile pro MA (aktueller Stand). Standard ist AUS, solange der MA nicht
/// aktiv zugestimmt hat. Ohne aktive Freigabe (MomentsConsentEnabled=true) darf
/// KEIN Moment-Link erstellt werden.
///
/// Änderungen werden zusätzlich automatisch im zentralen <see cref="AuditLog"/>
/// (AuditSaveChangesInterceptor) mit Alt-/Neu-Werten + Zeitstempel + User
/// protokolliert. Die Felder hier halten den dauerhaften Consent-Stand
/// (Zustimmung/Widerruf mit Zeitstempel, Textversion, Quelle).
/// </summary>
public class EmployeeMomentConsent
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>Haupt-Freigabe. Default false (kein Opt-in bis aktiv zugestimmt).</summary>
    public bool MomentsConsentEnabled { get; set; } = false;

    // ── Unterkategorien (greifen nur wenn MomentsConsentEnabled=true) ──
    public bool AllowBirthdayAndAnniversaryMoments { get; set; } = false;
    public bool AllowAppreciationMoments { get; set; } = false;
    public bool AllowCareMoments { get; set; } = false;

    /// <summary>Version des Zustimmungstextes, dem der MA zugestimmt hat.</summary>
    public string? ConsentTextVersion { get; set; }

    /// <summary>Zeitpunkt der Zustimmung (gesetzt beim Einschalten).</summary>
    public DateTime? GrantedAt { get; set; }

    /// <summary>Zeitpunkt des Widerrufs (gesetzt beim Ausschalten).</summary>
    public DateTime? RevokedAt { get; set; }

    public DateTime LastChangedAt { get; set; } = DateTime.Now;

    /// <summary>Wer hat zuletzt geändert (Anzeigename/Username).</summary>
    public string? LastChangedBy { get; set; }

    /// <summary>Quelle der Änderung, z.B. „EmployeeProfile".</summary>
    public string? Source { get; set; }
}

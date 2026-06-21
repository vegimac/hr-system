namespace HrSystem.Models;

public class EmployeeTimeEntry
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }

    public DateOnly EntryDate { get; set; }
    public DateTime TimeIn { get; set; }
    public DateTime? TimeOut { get; set; }

    public string? Comment { get; set; }

    public decimal? DurationHours { get; set; }
    public decimal? NightHours { get; set; }
    public decimal? TotalHours { get; set; }
    // Source-Spalte entfernt (Walter 17.06.2026): Stempelzeiten kommen ab sofort
    // ausschliesslich aus easy@work via API-Sync. „Quelle" ist konzeptionell
    // konstant „easy@work" und braucht keine Spalte mehr.

    /// <summary>
    /// Eindeutige easy@work-Stempel-ID (Walter 17.06.2026). Saubererer Dedup-
    /// Key als (EmployeeId, TimeIn) und ermöglicht spätere UPDATE-Syncs.
    /// </summary>
    public int? EasyAtWorkTimepunchId { get; set; }

    /// <summary>
    /// Herkunft (Walter-Vorgabe 21.06.2026): in welchem easy@work-Customer
    /// (= Filiale) wurde gestempelt. Wichtig, weil ein MA der in mehreren
    /// Filialen stempelt seine Stempel ALLE auf seinen einen Lohn-MA
    /// (IsPayrollExcluded=false) gespeichert bekommt — die Herkunftsfiliale
    /// bleibt so nachvollziehbar. Lohnberechnung liest weiter nur nach EmployeeId.
    /// </summary>
    public int? EasyAtWorkCustomerId { get; set; }
    /// <summary>Herkunftsfiliale als Cowork-CompanyProfile (optional, sofern auflösbar).</summary>
    public int? SourceCompanyProfileId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Audit-Felder: werden beim ersten Bearbeiten gesetzt
    public DateTime? OriginalTimeIn  { get; set; }
    public DateTime? OriginalTimeOut { get; set; }
    public string?   OriginalComment { get; set; }
    public string?   EditedBy        { get; set; }
    public DateTime? EditedAt        { get; set; }

    public Employee? Employee { get; set; }
}

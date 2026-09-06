namespace HrSystem.Models;

/// <summary>
/// Zeugnis-Entwurf für HR (Walter 06.09.2026): Ein Benutzer ohne Druck-
/// Berechtigung für die gewählte Funktion füllt die Zeugnis-Maske aus und
/// sendet sie als Entwurf an HR. HR öffnet den Entwurf in derselben Maske,
/// passt bei Bedarf an, wählt die Unterschrift und erstellt das PDF.
/// </summary>
public class ArbeitszeugnisEntwurf
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public int? CompanyProfileId { get; set; }
    /// <summary>arbeitszeugnis | zwischen | bestaetigung</summary>
    public string Art { get; set; } = "arbeitszeugnis";
    /// <summary>Alle Maskenwerte als JSON (ZeugnisDto).</summary>
    public string Daten { get; set; } = "{}";
    /// <summary>Bemerkung des Erstellers an HR.</summary>
    public string? Bemerkung { get; set; }
    public int? ErstelltVon { get; set; }
    public AppUser? ErstelltVonUser { get; set; }
    /// <summary>Lokalzeit (timestamp without time zone).</summary>
    public DateTime ErstelltAm { get; set; } = DateTime.Now;
    /// <summary>offen | erledigt | zurueckgewiesen</summary>
    public string Status { get; set; } = "offen";
    public int? ErledigtVon { get; set; }
    public AppUser? ErledigtVonUser { get; set; }
    public DateTime? ErledigtAm { get; set; }
    /// <summary>Antwort/Begründung von HR (bei Zurückweisung).</summary>
    public string? Antwort { get; set; }
    /// <summary>Eintrag im HR-Postfach (wird mit dem Entwurf erledigt/gelöscht).</summary>
    public int? MailboxDocumentId { get; set; }
}

namespace HrSystem.Models;

/// <summary>
/// Fehlerprotokoll des MA-Stammdaten-Syncs (Walter-Vorgabe 01.09.2026).
///
/// Bis dahin gab es KEINES: der Nachtlauf rief den MA-Sync auf, loggte eine
/// Zahl ins Journal und warf die Begründungen weg. Verträge mit
/// Erfassungsfehler verschwanden damit lautlos. Seit die neuen
/// Plausibilitätsregeln solche Verträge aktiv blockieren, ist dieses
/// Protokoll Pflicht — sonst fehlt ein Vertrag, und niemand weiss warum.
///
/// Eine Zeile pro erkanntem Problem und Lauf. Was der Anwender braucht,
/// steht im Klartext in <see cref="Reason"/>: was in easy@work falsch ist
/// und wie es zu korrigieren ist.
/// </summary>
public class EasyAtWorkMaSyncLog
{
    public int Id { get; set; }

    /// <summary>Zeitpunkt des Sync-Laufs (Lokalzeit).</summary>
    public DateTime RunAt { get; set; } = DateTime.Now;

    public int CompanyProfileId { get; set; }

    /// <summary>Personalnummer, soweit bekannt.</summary>
    public string? EmployeeNumber { get; set; }

    /// <summary>OneCrew-MA, sofern schon vorhanden.</summary>
    public int? EmployeeId { get; set; }

    /// <summary>
    /// CONFLICT = MA konnte nicht übernommen werden;
    /// VERTRAG  = einzelner Vertrag/Segment nicht importiert.
    /// </summary>
    public string Kind { get; set; } = "VERTRAG";

    /// <summary>Klartext für den Anwender — was ist falsch, was ist zu tun.</summary>
    public string Reason { get; set; } = "";

    /// <summary>
    /// Erledigt-Haken: der Anwender kann eine Zeile abhaken, wenn er sie in
    /// easy@work korrigiert hat. Taucht das Problem im nächsten Lauf erneut
    /// auf, entsteht eine NEUE Zeile — der Haken vertuscht also nichts.
    /// </summary>
    public bool Erledigt { get; set; }
    public DateTime? ErledigtAm { get; set; }
    public int? ErledigtVonUserId { get; set; }
}

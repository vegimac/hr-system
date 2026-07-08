namespace HrSystem.Models;

/// <summary>
/// Eine Zeile der Verfügbarkeits-Tabelle (nur bei EmployeeAvailability.Type='table').
/// Walter-Vorgabe 07.07.2026.
///
/// Zeit-Interpretation:
///   Von + Bis gesetzt  = Zeitfenster (z.B. 08:00–14:00)
///   nur Von            = „ab" (z.B. ab 18:00)
///   nur Bis            = „bis" (z.B. bis 12:00)
///   beide NULL         = ganztags
///
/// Wochentage: für welche Tage diese Zeile gilt. Ein Wochentag, der in KEINER
/// Slot-Zeile vorkommt, gilt als „nicht verfügbar".
/// </summary>
public class EmployeeAvailabilitySlot
{
    public int Id { get; set; }
    public int AvailabilityId { get; set; }

    public TimeOnly? Von { get; set; }
    public TimeOnly? Bis { get; set; }

    public bool Mon { get; set; }
    public bool Tue { get; set; }
    public bool Wed { get; set; }
    public bool Thu { get; set; }
    public bool Fri { get; set; }
    public bool Sat { get; set; }
    public bool Sun { get; set; }

    public int SortOrder { get; set; }

    public EmployeeAvailability? Availability { get; set; }
}

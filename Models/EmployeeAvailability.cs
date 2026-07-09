namespace HrSystem.Models;

/// <summary>
/// Verfügbare Arbeitszeiten eines Mitarbeiters (L-GAV-Anlage zum Arbeitsvertrag),
/// Walter-Vorgabe 07.07.2026. Eine „Verfügbarkeit" beschreibt, wann ein MA
/// grundsätzlich einsetzbar ist — entweder uneingeschränkt (a) oder als
/// Tages-/Wochentabelle (b). Sie ändert sich über die Zeit OHNE dass sich der
/// Vertrag ändert → deshalb versioniert am MITARBEITER, nicht am Vertrag.
///
/// Type:
///   'unrestricted' = a) uneingeschränkt verfügbar (keine Slots)
///   'table'        = b) Tages-Tabelle (siehe Slots)
///
/// Gültigkeit: ValidFrom Pflicht, ValidTo NULL = unbefristet. Die aktuell
/// gültige Version ist jene mit ValidFrom ≤ heute ≤ ValidTo|null.
/// </summary>
public class EmployeeAvailability
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }

    /// <summary>'unrestricted' | 'table'</summary>
    public string Type { get; set; } = "unrestricted";

    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public string? Bemerkung { get; set; }

    /// <summary>easy@work availability.id, wenn diese Version aus dem
    /// easy@work-Sync stammt (Walter 09.07.2026). NULL = manuell erfasst.
    /// Sync-Upsert-Schlüssel: sync-erzeugte Versionen werden beim nächsten
    /// Abgleich aktualisiert/entfernt, manuelle bleiben unangetastet.</summary>
    public long? EasyAtWorkAvailabilityId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }

    /// <summary>Die Zeilen der Tabelle — nur bei Type='table' relevant.</summary>
    public List<EmployeeAvailabilitySlot> Slots { get; set; } = new();
}

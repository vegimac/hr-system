namespace HrSystem.Models;

/// <summary>
/// History jeder Probezeit-Verschiebung (Walter-Vorgabe 29.06.2026).
/// Eine Zeile pro Ereignis:
///   • ANKER  — beim ersten Stempel: Probezeit auf den tatsächlichen 1. Arbeitstag
///              ausgerichtet (DeltaDays = erste Stempelzeit − Vertragsbeginn).
///   • ABSENZ — spätere Verlängerung wegen Krankheit/Unfall/Absenz in der Probezeit
///              (DeltaDays = +Absenztage).
/// Der aktuelle Probezeit-Endwert steht denormalisiert am Employment
/// (ProbationEndDate) — diese Tabelle liefert das „warum" + die Rekonstruktion.
/// </summary>
public class EmploymentProbationLog
{
    public int Id { get; set; }
    public int EmploymentId { get; set; }
    public Employment? Employment { get; set; }

    /// <summary>Datum, auf das sich das Ereignis bezieht (erste Stempelzeit bzw. Absenz-Tag).</summary>
    public DateOnly EventDate { get; set; }

    /// <summary>"ANKER" | "ABSENZ".</summary>
    public string EventType { get; set; } = "ANKER";

    /// <summary>Verschiebung in Tagen (negativ = vorgezogen, positiv = verlängert).</summary>
    public int DeltaDays { get; set; }

    /// <summary>Klartext-Grund, z.B. „Vertragsbeginn &gt; 1. Arbeitstag (erste Stempelzeit 09.06.2026)".</summary>
    public string? Grund { get; set; }

    /// <summary>Resultierendes Probezeit-Ende NACH diesem Ereignis.</summary>
    public DateOnly? ProbezeitEndeNachher { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;   // Spalte ist timestamp WITHOUT time zone → keine UTC-Kind
}

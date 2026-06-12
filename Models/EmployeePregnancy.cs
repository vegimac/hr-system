using System;

namespace HrSystem.Models;

/// <summary>
/// Schwangerschaft einer Mitarbeiterin. Mehrere Einträge pro MA möglich
/// (Folgeschwangerschaften, Fehlgeburten etc. — Bezugnehmend auf das
/// Audit-Protokoll wird KEIN Eintrag gelöscht, sondern is_active=false gesetzt).
///
/// Fristen werden NICHT denormalisiert gespeichert — sie werden bei jedem
/// GET aus pregnancy_rule berechnet, damit Anpassungen am Regelwerk sofort
/// auf alle laufenden Schwangerschaften wirken.
/// </summary>
public class EmployeePregnancy
{
    public int      Id                    { get; set; }
    public int      EmployeeId            { get; set; }
    public Employee? Employee             { get; set; }

    /// <summary>Datum an dem die MA dem AG die Schwangerschaft gemeldet hat.</summary>
    public DateOnly Meldedatum            { get; set; }

    /// <summary>Vom Arzt errechneter Geburtstermin.</summary>
    public DateOnly ErrechneterTermin     { get; set; }

    /// <summary>Effektives Geburtsdatum — wird nach der Geburt nachgetragen.</summary>
    public DateOnly? Geburtsdatum         { get; set; }

    // ArztzeugnisVorhanden entfernt am 10.06.2026: Krankheits-Absenzen werden
    // im Absenz-Tab als KRANK erfasst (mit Arztzeugnis-Doku am MA).
    public string?  Bemerkung             { get; set; }
    public bool     IsActive              { get; set; } = true;

    public DateTime  CreatedAt            { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt            { get; set; }
}

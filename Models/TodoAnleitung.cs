namespace HrSystem.Models;

/// <summary>
/// «So behebst du es»-Text pro Dashboard-Warnungskategorie
/// (Walter-Vorgabe 30.08.2026).
///
/// Gegenstück zu <see cref="DashboardWarningConfig"/>: dort steht, WANN eine
/// Warnung erscheint, hier steht, was der Geschäftsführer dann tun soll.
/// Kategorien ohne Eintrag fallen im Generator still auf ihr Label zurück.
/// </summary>
public class TodoAnleitung
{
    public int Id { get; set; }

    /// <summary>Gleicher Schlüssel wie dashboard_warning_config.category.</summary>
    public string Category { get; set; } = "";

    /// <summary>Überschrift im Anleitungstext, handlungsorientiert formuliert.</summary>
    public string Titel { get; set; } = "";

    /// <summary>Der Anleitungstext selbst — was ist zu tun, wo, mit welcher Folge.</summary>
    public string Anleitung { get; set; } = "";

    public int SortOrder { get; set; } = 100;
}

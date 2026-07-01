namespace HrSystem.Models;

/// <summary>
/// Eine Text-Vorlage für die Kombination Moment-Typ × Emotionsgrad
/// (Walter-Vorgabe 01.07.2026). Pro Kombination sind mehrere Varianten möglich.
/// Platzhalter {Vorname} / {Absender} werden beim Erstellen ersetzt.
/// </summary>
public class MomentText
{
    public int Id { get; set; }

    public int MomentTypeId { get; set; }
    public MomentType? MomentType { get; set; }

    public int MomentToneId { get; set; }
    public MomentTone? MomentTone { get; set; }

    /// <summary>Optionaler Titel / Betreff.</summary>
    public string? Titel { get; set; }

    /// <summary>SMS-Kurztext (Push).</summary>
    public string? SmsText { get; set; }

    /// <summary>Die eigentliche Mitteilung.</summary>
    public string BodyText { get; set; } = "";

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Sprache der Vorlage (z.B. „de").</summary>
    public string? LanguageCode { get; set; } = "de";

    /// <summary>Versionskennung der Vorlage (z.B. „1.0").</summary>
    public string? Version { get; set; }

    /// <summary>True = muss vor Versand von einer berechtigten Person geprüft werden.</summary>
    public bool RequiresReview { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? CreatedBy { get; set; }
}

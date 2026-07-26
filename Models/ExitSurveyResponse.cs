namespace HrSystem.Models;

/// <summary>
/// Anonyme Antwort auf den Austritts-Fragebogen (Walter 26.07.2026) —
/// ersetzt das frühere Google-Formular. Kein Mitarbeiter-Bezug (Anonymität).
/// </summary>
public class ExitSurveyResponse
{
    public long Id { get; set; }

    /// <summary>ISO-Zeitstempel der Abgabe (lokal).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Gewählte Hauptgründe (Codes), max. 3, als JSON-Array.</summary>
    public string ReasonsJson { get; set; } = "[]";

    /// <summary>Freitext zu «Other» / sonstiger Grund.</summary>
    public string? ReasonOther { get; set; }

    /// <summary>Erläuterung, wenn Atmosphäre/Organisation gewählt wurde.</summary>
    public string? AtmosphereDetail { get; set; }

    /// <summary>Note 1–6 (1 = am schlechtesten, 6 = am besten).</summary>
    public int? Rating { get; set; }

    /// <summary>Weitere Kommentare / Feedback.</summary>
    public string? Comment { get; set; }

    /// <summary>Kurzer SHA-256-Hash der Client-IP (Rate-Limit, kein Klartext).</summary>
    public string? IpHash { get; set; }
}

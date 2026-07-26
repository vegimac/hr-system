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

    /// <summary>
    /// Filiale der Kündigung (Walter 26.07.2026) — anonym, kein MA-Bezug.
    /// Kommt aus dem QR-Parameter der Kündigungsbestätigung (?f=RestaurantCode).
    /// </summary>
    public int? CompanyProfileId { get; set; }

    /// <summary>Gewählte Hauptgründe (Codes), max. 3, als JSON-Array.</summary>
    public string ReasonsJson { get; set; } = "[]";

    /// <summary>Freitext zu «Other» / sonstiger Grund (Legacy).</summary>
    public string? ReasonOther { get; set; }

    /// <summary>Erläuterung Atmosphäre (Legacy).</summary>
    public string? AtmosphereDetail { get; set; }

    /// <summary>Note 1–6 (Legacy).</summary>
    public int? Rating { get; set; }

    /// <summary>Weitere Kommentare / Feedback.</summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Frage 2: «JA» = es gibt etwas, «NEIN» = einfach Zeit für etwas Neues.
    /// </summary>
    public string? ImproveAnswer { get; set; }

    /// <summary>Themen bei ImproveAnswer=JA, als JSON-Array von Codes.</summary>
    public string ImproveThemesJson { get; set; } = "[]";

    /// <summary>Kurzer SHA-256-Hash der Client-IP (Rate-Limit, kein Klartext).</summary>
    public string? IpHash { get; set; }
}

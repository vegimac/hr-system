namespace HrSystem.Models;

/// <summary>
/// SMS-Versand-Protokoll (Walter 07.07.2026, Stufe 1). Zentral geschrieben in
/// <see cref="Services.EcallSmsService"/> — JEDER Versandversuch (Vertrag,
/// Moment, Postfach-Push, Test) landet hier, egal ob erfolgreich.
///
/// Stufe 2 (offen): Zustell-Status «Delivered» via eCall-Statusabfrage oder
/// Notification-Webhook — dafür ist <see cref="MessageId"/> der Schlüssel.
/// </summary>
public class SmsLog
{
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;  // Lokalzeit (timestamp without time zone)

    /// <summary>Zweck: VERTRAG / MOMENT / POSTFACH / TEST / (weitere).</summary>
    public string? Purpose { get; set; }

    /// <summary>Betroffener MA (NULL bei Test-SMS).</summary>
    public int? EmployeeId { get; set; }

    /// <summary>Ursprüngliche Zielnummer (VOR Test-Umleitung).</summary>
    public string? ToPhone { get; set; }

    /// <summary>Test-Umleitungs-Nummer, wenn die SMS umgeleitet wurde.</summary>
    public string? RedirectedTo { get; set; }

    public bool Ok { get; set; }

    /// <summary>eCall-Message-ID (nur bei Erfolg).</summary>
    public string? MessageId { get; set; }

    /// <summary>Fehlertext (nur bei Misserfolg).</summary>
    public string? Error { get; set; }
}

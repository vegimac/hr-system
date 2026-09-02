namespace HrSystem.Models;

/// <summary>
/// Mail-Versand-Protokoll (Walter 01.09.2026) — das Gegenstück zu
/// <see cref="SmsLog"/>. Zentral geschrieben in
/// <see cref="Services.EmailService"/>: JEDER Versandversuch landet hier,
/// egal ob erfolgreich, umgeleitet oder blockiert.
///
/// Damit ist die Haken-Matrix in der Systemsteuerung nicht nur eine
/// Einstellung, der man glauben muss, sondern eine, die man nachprüfen
/// kann: <see cref="RedirectedTo"/> zeigt, ob die Mail umgeleitet wurde.
/// </summary>
public class MailLog
{
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;  // Lokalzeit (timestamp without time zone)

    /// <summary>Verteiler-Kategorie, z.B. «GRUPPEN_MAIL».</summary>
    public string? Kategorie { get; set; }

    /// <summary>Betroffener MA, sofern bekannt.</summary>
    public int? EmployeeId { get; set; }

    /// <summary>Ursprünglicher Empfänger (VOR Test-Umleitung).</summary>
    public string? ToEmail { get; set; }

    /// <summary>Test-Adresse, wenn die Mail umgeleitet wurde; sonst NULL.</summary>
    public string? RedirectedTo { get; set; }

    public string? Subject { get; set; }

    /// <summary>Anzahl Anhänge.</summary>
    public int AttachmentCount { get; set; }

    public bool Ok { get; set; }

    /// <summary>Fehlertext bzw. Blockier-Grund (nur wenn nicht Ok).</summary>
    public string? Error { get; set; }

    /// <summary>
    /// Verweis auf den Gruppen-Versand, zu dem diese Zeile gehört
    /// (Walter-Vorgabe 01.09.2026). Nur bei Kategorie GRUPPEN_MAIL gesetzt.
    ///
    /// Ohne diesen Verweis liesse sich «5 fehlgeschlagen» im Versandlog nur
    /// über Betreff und Zeitfenster auflösen — und zwei Versände mit dem
    /// gleichen Betreff kurz nacheinander (genau das ist am 01.09. passiert:
    /// 21:34 und 21:39) wären nicht auseinanderzuhalten.
    /// </summary>
    public int? GruppenMailLogId { get; set; }

    /// <summary>
    /// true = diese Zeile stammt aus einer WIEDERVORLAGE, also aus einem
    /// späteren Versuch, nachdem der erste an einem vorübergehenden Fehler
    /// gescheitert war (Walter-Vorgabe 01.09.2026).
    ///
    /// Ohne die Unterscheidung stünde derselbe Empfänger im Protokoll
    /// zweimal da — einmal rot, einmal grün — ohne dass erkennbar wäre,
    /// welche Zeile die spätere ist.
    /// </summary>
    public bool Wiedervorlage { get; set; }
}

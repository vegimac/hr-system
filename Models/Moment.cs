namespace HrSystem.Models;

/// <summary>
/// Eine „Moment"-Mitteilung an einen MA (Walter 30.06.2026): kurze SMS mit einem
/// persönlichen Link auf eine Landing-Page, die die vollständige Mitteilung zeigt
/// und je nach Antwortart eine Reaktion erlaubt. Der SMS-Versand kommt später
/// (ASPSMS); vorerst wird der Link manuell verschickt.
/// </summary>
public class Moment
{
    public int Id { get; set; }

    /// <summary>URL-sicherer Zufalls-Token für den Link /moment.html?t=…</summary>
    public string Token { get; set; } = "";

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>Moment-Typ (dokument/rueckfrage/danke/geburtstag/hr/freitext).</summary>
    public string? Typ { get; set; }

    /// <summary>Zustellung: „postfach" (Mitteilung liegt im MA-Postfach, SMS-Link
    /// führt dorthin, MA loggt sich ein) oder „direkt" (Direktlink-Landing ohne
    /// Login, moment.html — für einfache Mitteilungen wie „Happy Birthday").</summary>
    public string Zustellung { get; set; } = "postfach";

    /// <summary>Bei Zustellung „postfach": die erzeugte Postfach-Mitteilung.</summary>
    public int? MailboxDocumentId { get; set; }

    /// <summary>Absender / HR-Name (dynamisches Feld in der Mitteilung).</summary>
    public string? Absender { get; set; }
    /// <summary>Name des angeforderten Dokuments (bei Typ „Dokument anfordern").</summary>
    public string? DokumentName { get; set; }

    public string? SmsText  { get; set; }
    public string? FullText { get; set; }

    /// <summary>Zeitpunkt der Identitätsbestätigung (letzte 4 Ziffern Mobilnr.).</summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>Antwortart: lesen | janein | text | datei.</summary>
    public string Antwortart { get; set; } = "lesen";

    /// <summary>erstellt | geoeffnet | beantwortet.</summary>
    public string Status { get; set; } = "erstellt";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int? CreatedById { get; set; }

    public DateTime? OpenedAt    { get; set; }
    public DateTime? RespondedAt { get; set; }

    /// <summary>Bei „janein": „ja" / „nein".</summary>
    public string? ResponseValue { get; set; }
    /// <summary>Bei „text": die Textantwort.</summary>
    public string? ResponseText  { get; set; }
    /// <summary>Bei „datei": verknüpftes hochgeladenes Dokument.</summary>
    public int? ResponseDokumentId { get; set; }
}

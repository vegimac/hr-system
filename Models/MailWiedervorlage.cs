namespace HrSystem.Models;

/// <summary>
/// Eine Mail, die an einem VORÜBERGEHENDEN Fehler gescheitert ist und
/// später nochmals versucht wird (Walter-Vorgabe 01.09.2026).
///
/// Anlass: Beim Versand an alle MA am 01.09.2026 lehnte Hostfactory fünf
/// Mails mit «5.7.0 The limit on the number of allowed outgoing messages
/// was exceeded» ab — das Stundenlimit von rund 275 Mails war erreicht.
/// Die fünf Empfänger waren damit still verloren: im Protokoll stand
/// «fehlgeschlagen», und danach passierte nichts mehr.
///
/// Die Unterscheidung ist dieselbe wie bei <see cref="MailBounce"/>, nur
/// eine Stufe früher — hier scheitert schon die ÜBERGABE an den eigenen
/// Server, dort meldet der Empfänger-Server später zurück:
///   • VORÜBERGEHEND = alle 4.x sowie «5.7.0 limit exceeded». Morgen (oder
///     in einer Stunde) klappt es wieder → hierher in die Wiedervorlage.
///   • ENDGÜLTIG = die Adresse existiert nicht (5.1.x). Wiederholen bringt
///     nichts; dafür gibt es die Rückläufer-Logik mit der Sperrliste.
///
/// Gespeichert wird die FERTIGE Nachricht (<see cref="Mime"/>), nicht die
/// Bausteine. Damit geht beim späteren Versuch exakt dieselbe Mail raus —
/// mit denselben Anhängen, demselben Betreff und derselben Message-ID.
/// Die Alternative, alles neu zusammenzubauen, hiesse Anhänge separat
/// aufzubewahren und die Umleitungs-Entscheidung ein zweites Mal zu
/// treffen; beides sind Gelegenheiten, eine andere Mail zu verschicken
/// als die, die gescheitert ist.
/// </summary>
public class MailWiedervorlage
{
    public int Id { get; set; }

    /// <summary>Wann der erste Versuch gescheitert ist.</summary>
    public DateTime ErstelltAm { get; set; } = DateTime.Now;

    /// <summary>Verteiler-Kategorie, z.B. «GRUPPEN_MAIL».</summary>
    public string? Kategorie { get; set; }

    /// <summary>Betroffener MA, sofern bekannt.</summary>
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>Gruppen-Versand, zu dem die Mail gehört (sonst NULL).</summary>
    public int? GruppenMailLogId { get; set; }

    /// <summary>Ursprünglicher Empfänger (VOR einer Test-Umleitung).</summary>
    public string? ToEmail { get; set; }

    /// <summary>
    /// Die Adresse, an die wirklich zugestellt werden soll — bei einer
    /// Umleitung also die Test-Adresse. Bewusst festgehalten: die
    /// Freigabe-Matrix kann sich zwischen Fehlschlag und Wiederholung
    /// ändern, und dann soll die zweite Mail trotzdem dorthin gehen,
    /// wohin die erste unterwegs war.
    /// </summary>
    public string EffektiveAdresse { get; set; } = "";

    /// <summary>Test-Adresse, wenn die Mail umgeleitet wurde; sonst NULL.</summary>
    public string? RedirectedTo { get; set; }

    public string? Betreff { get; set; }

    public int AnhangAnzahl { get; set; }

    /// <summary>
    /// Die vollständige Nachricht als MIME. Wird geleert, sobald der Fall
    /// abgeschlossen ist (zugestellt oder abgehakt) — ein Massenversand mit
    /// Anhang läge sonst dauerhaft als Kopie pro Empfänger in der Datenbank.
    /// </summary>
    public byte[] Mime { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Wie viele WIEDERHOLUNGEN bereits gescheitert sind. 0 = erst der
    /// ursprüngliche Versand ist gescheitert, wiederholt wurde noch nicht.
    /// </summary>
    public int Versuche { get; set; }

    /// <summary>Frühester Zeitpunkt des nächsten Versuchs.</summary>
    public DateTime NaechsterVersuch { get; set; } = DateTime.Now;

    public string? LetzterFehler { get; set; }

    /// <summary>Status-Code des letzten Fehlversuchs, z.B. «5.7.0».</summary>
    public string? LetzterCode { get; set; }

    /// <summary>
    /// OFFEN = wartet auf den nächsten Versuch.
    /// GESENDET = später doch zugestellt.
    /// AUFGEGEBEN = alle Versuche verbraucht (oder unterwegs endgültig
    ///              abgelehnt) → Pendenz.
    /// ABGEBROCHEN = jemand hat den Fall in der Systemsteuerung beendet.
    /// </summary>
    public string Status { get; set; } = StatusOffen;

    public const string StatusOffen       = "OFFEN";
    public const string StatusGesendet    = "GESENDET";
    public const string StatusAufgegeben  = "AUFGEGEBEN";
    public const string StatusAbgebrochen = "ABGEBROCHEN";

    /// <summary>Wann der Fall in einen Endzustand gelaufen ist.</summary>
    public DateTime? AbgeschlossenAm { get; set; }

    /// <summary>
    /// Erledigt = jemand hat die Pendenz abgehakt. Nur bei AUFGEGEBEN von
    /// Bedeutung; zugestellte Mails brauchen niemanden mehr.
    /// </summary>
    public bool Erledigt { get; set; }
    public DateTime? ErledigtAm { get; set; }
    public int? ErledigtVonUserId { get; set; }
}

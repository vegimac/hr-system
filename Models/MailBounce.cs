namespace HrSystem.Models;

/// <summary>
/// Ein Rückläufer («Bounce»): eine Mail, die der Empfänger-Server nicht
/// zustellen konnte (Walter-Vorgabe 01.09.2026).
///
/// Hintergrund: Beim ersten Massenversand an alle MA kamen zwei solche
/// Meldungen zurück — einmal «Postfach voll», einmal «Adresse existiert
/// nicht» (ein I wie Ida statt l wie Lima in der Adresse). Beide landeten
/// im HR-Postfach und mussten von Hand gedeutet werden. Bei 300 Empfängern
/// geht das unter, und OneCrew sendet monatelang an tote Adressen.
///
/// Darum holt <c>BounceAbrufService</c> die Rückläufer aus einem eigenen
/// Postfach (bounce@…) und legt sie hier ab — mit Klartext-Grund und, wo
/// möglich, der Zuordnung zum Mitarbeitenden.
///
/// HART vs. WEICH ist die entscheidende Unterscheidung:
///   • HART  = die Adresse gibt es nicht (5.1.x «mailbox not found»).
///             Das wird nie von selbst besser — die Adresse muss korrigiert
///             werden. OneCrew sperrt sie und meldet sie sofort.
///   • WEICH = vorübergehend (Postfach voll 5.2.2, Server-Störung 4.x).
///             Morgen klappt es vielleicht wieder. Erst nach drei
///             Fehlversuchen in Folge kommt eine Pendenz.
/// </summary>
public class MailBounce
{
    public int Id { get; set; }

    /// <summary>Wann OneCrew den Rückläufer aus dem Postfach geholt hat.</summary>
    public DateTime EmpfangenAm { get; set; } = DateTime.Now;

    /// <summary>
    /// Die Adresse, die nicht erreichbar war — IMMER kleingeschrieben
    /// gespeichert. Bei E-Mail ist die Domain garantiert case-insensitiv;
    /// die Kleinschreibung macht den Abgleich mit dem MA-Datensatz und die
    /// Sperrprüfung eindeutig, ohne bei jedem Vergleich umwandeln zu müssen.
    /// </summary>
    public string Adresse { get; set; } = "";

    /// <summary>
    /// Zugeordneter Mitarbeitender, falls die Adresse zu einem passt.
    /// NULL heisst: die Adresse steht (nicht mehr) bei einem MA — z.B. weil
    /// sie inzwischen korrigiert wurde oder ein OneCrew-Benutzer gemeint war.
    /// </summary>
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>true = hart (Adresse existiert nicht), false = weich.</summary>
    public bool Hart { get; set; }

    /// <summary>
    /// Der Status-Code aus der Rückmeldung, z.B. «5.1.1» oder «5.2.2».
    /// Bewusst als Text: die Codes sind dreiteilig und keine Zahl.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Grund in Klartext-Deutsch, wie er auf der Pendenzenliste steht —
    /// z.B. «Adresse existiert nicht» statt «5.1.1».
    /// </summary>
    public string Grund { get; set; } = "";

    /// <summary>
    /// Die Originalmeldung des fremden Servers, ungefiltert. Für den Fall,
    /// dass unsere Übersetzung einen Sonderfall nicht trifft — dann steht
    /// hier, was wirklich zurückkam.
    /// </summary>
    public string? Meldung { get; set; }

    /// <summary>Betreff der ursprünglichen Mail, damit man den Anlass erkennt.</summary>
    public string? OriginalBetreff { get; set; }

    /// <summary>
    /// Message-ID der ursprünglichen Mail. Verhindert, dass derselbe
    /// Rückläufer zweimal erfasst wird, wenn der Abruf doppelt läuft.
    /// </summary>
    public string? OriginalMessageId { get; set; }

    /// <summary>
    /// UID der Mail im Bounce-Postfach — der zweite Riegel gegen Dubletten,
    /// falls die Message-ID fehlt (nicht jeder Server liefert sie mit).
    /// </summary>
    public string? QuellUid { get; set; }

    /// <summary>
    /// Erledigt = jemand hat sich darum gekümmert (Adresse korrigiert oder
    /// bewusst ignoriert). Erledigte Rückläufer sperren nicht mehr und
    /// erscheinen nicht mehr auf der Pendenzenliste.
    /// </summary>
    public bool Erledigt { get; set; } = false;
    public DateTime? ErledigtAm { get; set; }
    public int? ErledigtVonUserId { get; set; }
}

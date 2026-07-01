namespace HrSystem.Models;

/// <summary>
/// „OneCrew Moments" — persönliche, freiwillige Mini-Mitteilung an einen MA
/// (Walter 30.06.2026). STRIKT getrennt vom Postfach: Moments enthalten KEINE
/// administrativen/sensiblen HR-Themen (kein Lohn/Vertrag/Bewilligung/QST/
/// Arztzeugnis), KEINE Dokumente und werden über einen EINMALIGEN Token-Link
/// OHNE Login/Passwort/Datenschutzabfrage geöffnet. Nur für MA mit aktivem
/// Freigabe (siehe EmployeeMomentConsent).
///
/// Der Klartext-Token wird NICHT gespeichert — nur sein SHA-256-Hash
/// (<see cref="TokenHash"/>). Der Link trägt den Klartext; beim Öffnen wird er
/// gehasht und gegen die DB verglichen. Damit ist der Token aus der DB heraus
/// nicht rekonstruierbar.
///
/// Administrative/sensible Mitteilungen laufen weiterhin über das normale
/// Postfach (<see cref="MailboxDocument"/>) — SMS dient dort nur als Push, der
/// Link führt zum Login.
/// </summary>
public class MomentPage
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>HR-User, der den Moment erstellt hat.</summary>
    public int? SenderId { get; set; }

    /// <summary>Persönlicher Moment-Typ: wertschaetzung | geburtstag | jubilaeum | freiwillig.
    /// KEINE administrativen Typen erlaubt.</summary>
    public string MomentType { get; set; } = "";

    /// <summary>Kurzer Titel (z.B. „Alles Gute zum Geburtstag").</summary>
    public string? Title { get; set; }

    /// <summary>Die Mitteilung (bereits aufgelöst: {Vorname}/{Absender} ersetzt).</summary>
    public string? MessageHtml { get; set; }

    /// <summary>SHA-256-Hex des Einmal-Tokens (der Klartext steht nur im Link).</summary>
    public string TokenHash { get; set; } = "";

    /// <summary>Ablaufzeitpunkt des Links. Nach Ablauf 410/„abgelaufen".</summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime? OpenedAt { get; set; }
    public DateTime? RespondedAt { get; set; }

    /// <summary>Bei Antwortart „janein": „ja" / „nein".</summary>
    public string? ResponseValue { get; set; }

    /// <summary>erstellt | geoeffnet | beantwortet | abgelaufen.</summary>
    public string Status { get; set; } = "erstellt";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // ── Pragmatische Zusatzfelder (nicht in Walters Kernliste, aber nötig) ──
    /// <summary>SMS-Push-Text (kurz). Der Link wird angehängt.</summary>
    public string? SmsText { get; set; }

    /// <summary>Antwortart: lesen | janein (Moments erlauben höchstens Ja/Nein).</summary>
    public string Antwortart { get; set; } = "lesen";
}

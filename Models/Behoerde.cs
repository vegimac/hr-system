namespace HrSystem.Models;

/// <summary>
/// Amt als Empfänger von Lohnabtretungen: Betreibungsamt, Sozialamt etc.
/// Einmalig als Stammdatum erfasst, mehrfach über
/// <see cref="EmployeeLohnAssignment"/> referenziert.
/// </summary>
public class Behoerde
{
    public int     Id         { get; set; }
    public string  Name       { get; set; } = "";

    /// <summary>BETREIBUNGSAMT | SOZIALAMT | STEUERAMT | ANDERE</summary>
    public string  Typ        { get; set; } = "BETREIBUNGSAMT";

    /// <summary>
    /// 2-Zeichen-Kantonscode (LU, AG, ZH …). Pflicht für STEUERAMT — über
    /// den Code wird beim QST-Formular automatisch das passende Steueramt
    /// zur Filiale gefunden. Bei Betreibungs-/Sozialamt optional.
    /// </summary>
    public string? KantonCode { get; set; }

    public string? Adresse1   { get; set; }
    public string? Adresse2   { get; set; }
    public string? Adresse3   { get; set; }
    public string? Plz        { get; set; }
    public string? Ort        { get; set; }

    public string? Telefon    { get; set; }

    /// <summary>Handy-/Mobilnummer der Kontaktperson (Walter 30.07.2026).</summary>
    public string? Handy      { get; set; }

    public string? Email      { get; set; }

    /// <summary>Sachbearbeiter/in als persönliche Kontaktperson, z.B. "Jana Hrdinka".</summary>
    public string? Kontaktperson      { get; set; }

    /// <summary>Funktion/Rolle der Kontaktperson, z.B. "Sachbearbeiterin".</summary>
    public string? KontaktpersonRolle { get; set; }

    /// <summary>Telefonische Erreichbarkeit als Freitext, z.B. "Mo–Fr 08:00–11:45".</summary>
    public string? Erreichbarkeit     { get; set; }

    /// <summary>URL zur Behörden-Webseite (für Quicklink im UI).</summary>
    public string? Webseite           { get; set; }

    /// <summary>Normale IBAN (Info).</summary>
    public string? Iban       { get; set; }

    /// <summary>QR-IBAN für QR-Rechnung (falls abweichend von Iban).</summary>
    public string? QrIban     { get; set; }

    /// <summary>
    /// Kontoinhaber für pain.001 Cdtr.Nm — wenn die IBAN auf eine andere
    /// juristische Person lautet als <see cref="Name"/> (z.B. ORS Burgdorf
    /// → Kontoinhaber «ORS Service AG Zürich»). Leer = Name der Behörde.
    /// </summary>
    public string? Kontoinhaber { get; set; }

    public string? Bic        { get; set; }
    public string? BankName   { get; set; }

    public bool    IsActive   { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public ICollection<BehoerdeSachbearbeiter> Sachbearbeiter { get; set; }
        = new List<BehoerdeSachbearbeiter>();
}

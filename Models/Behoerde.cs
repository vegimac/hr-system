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

    /// <summary>BETREIBUNGSAMT | SOZIALAMT | ANDERE</summary>
    public string  Typ        { get; set; } = "BETREIBUNGSAMT";

    public string? Adresse1   { get; set; }
    public string? Adresse2   { get; set; }
    public string? Adresse3   { get; set; }
    public string? Plz        { get; set; }
    public string? Ort        { get; set; }

    public string? Telefon    { get; set; }
    public string? Email      { get; set; }

    /// <summary>Normale IBAN (Info).</summary>
    public string? Iban       { get; set; }

    /// <summary>QR-IBAN für QR-Rechnung (falls abweichend von Iban).</summary>
    public string? QrIban     { get; set; }

    public string? Bic        { get; set; }
    public string? BankName   { get; set; }

    public bool    IsActive   { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

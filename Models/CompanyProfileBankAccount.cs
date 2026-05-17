namespace HrSystem.Models;

/// <summary>
/// Bankverbindung einer Filiale mit Gültigkeitszeitraum (analog zu
/// <see cref="EmployeeBankAccount"/>). Pro Filiale können mehrere Bank-
/// einträge geführt werden — z.B. nach einem Bankenwechsel: alter Eintrag
/// bekommt ein <see cref="ValidTo"/>, ein neuer Eintrag startet ab dem
/// Wechseldatum. Beim Lohnlauf-DTA wird der Eintrag verwendet, der in der
/// Lohnperiode gültig ist und als Hauptbank markiert ist.
/// </summary>
public class CompanyProfileBankAccount
{
    public int       Id               { get; set; }
    public int       CompanyProfileId { get; set; }

    public string    Iban             { get; set; } = "";
    public string?   Bic              { get; set; }
    public string?   BankName         { get; set; }

    /// <summary>
    /// True = Hauptbank für diese Filiale. Pro Filiale sollte zu jedem
    /// Zeitpunkt genau eine Hauptbank gültig sein. Bei mehreren aktiven
    /// Konten wird die mit IsMain=true für DTA-Auftraggeber genommen.
    /// </summary>
    public bool      IsMain           { get; set; } = true;

    public string?   Bemerkung        { get; set; }

    public DateOnly  ValidFrom        { get; set; }
    public DateOnly? ValidTo          { get; set; }

    public DateTime  CreatedAt        { get; set; } = DateTime.UtcNow;
    public DateTime  UpdatedAt        { get; set; } = DateTime.UtcNow;

    public CompanyProfile? CompanyProfile { get; set; }
}

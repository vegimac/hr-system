namespace HrSystem.Models;

/// <summary>
/// Bankverbindung eines Mitarbeiters mit Gültigkeitszeitraum.
/// Bei Bankenwechsel wird der alte Eintrag mit ValidTo abgeschlossen und
/// ein neuer Eintrag ab dem Wechseldatum angelegt.
/// </summary>
public class EmployeeBankAccount
{
    public int       Id               { get; set; }
    public int       EmployeeId       { get; set; }

    public string    Iban             { get; set; } = "";
    public string?   Bic              { get; set; }
    public string?   BankName         { get; set; }

    /// <summary>NULL = MA selbst. Gesetzt bei abweichendem Kontoinhaber.</summary>
    public string?   Kontoinhaber     { get; set; }

    public string?   Zahlungsreferenz { get; set; }
    public string?   Bemerkung        { get; set; }

    /// <summary>
    /// True = Hauptbankverbindung (Default bei Lohn-Auszahlung). Bei mehreren
    /// aktiven Konten eines MA sollte genau eines die Hauptbank sein.
    /// </summary>
    public bool      IsHauptbank      { get; set; } = true;

    /// <summary>
    /// Aufteilungs-Regel für Lohnsplittung auf mehrere Konten:
    ///   VOLL             → gesamter Rest-Nettolohn auf dieses Konto
    ///   FIXBETRAG        → fixer CHF-Betrag aus AufteilungWert
    ///   PROZENT          → X% vom Bruttolohn (AufteilungWert = %)
    ///   NETTO_ABZUEGLICH → Nettolohn minus X CHF (AufteilungWert = CHF)
    /// </summary>
    public string    AufteilungTyp    { get; set; } = "VOLL";

    /// <summary>
    /// Numerischer Wert zur Aufteilung — CHF bei FIXBETRAG/NETTO_ABZUEGLICH,
    /// Prozent bei PROZENT, NULL bei VOLL.
    /// </summary>
    public decimal?  AufteilungWert   { get; set; }

    public DateOnly  ValidFrom        { get; set; }
    public DateOnly? ValidTo          { get; set; }

    public DateTime  CreatedAt        { get; set; } = DateTime.UtcNow;
    public DateTime  UpdatedAt        { get; set; } = DateTime.UtcNow;

    public Employee? Employee         { get; set; }
}

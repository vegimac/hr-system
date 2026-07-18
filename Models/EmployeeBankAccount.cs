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

    /// <summary>
    /// NULL = MA selbst. Gesetzt bei abweichendem Kontoinhaber/Empfänger.
    /// Praxisfall: Revolut/Wise/N26 — Kontoinhaber = "Revolut Bank UAB" mit
    /// LT-Adresse, der MA-Name geht in <see cref="Zahlungsreferenz"/>
    /// (Zahlungsgrund) damit die Schweizer Bank die SEPA-Zahlung akzeptiert.
    /// </summary>
    public string?   Kontoinhaber          { get; set; }

    /// <summary>Adresse Strasse des abweichenden Empfängers (für DTA-Cdtr).</summary>
    public string?   KontoinhaberStrasse   { get; set; }

    /// <summary>PLZ des Empfängers — auch ausländische Formate zulässig (z.B. "08130").</summary>
    public string?   KontoinhaberPlz       { get; set; }

    public string?   KontoinhaberOrt       { get; set; }

    /// <summary>ISO-3166-1 alpha-2 Ländercode (z.B. "LT", "DE", "CH").</summary>
    public string?   KontoinhaberLand      { get; set; }

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

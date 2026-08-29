using System.Text.Json.Serialization;

namespace HrSystem.Models;

/// <summary>
/// K3 (Walter 29.08.2026, docs/qst-korrektur-konzept.md Kap. 4 + Bauplan 2.3):
/// Generisches, ZINSLOSES MA-Darlehen — für QST-Nachbelastungen UND freie
/// Vorschüsse (z.B. «Vorschuss Hochzeit 2'000»). Rückzahlung als
/// automatischer Abzug nach Netto im Definitivlauf (Rate pro Monat, letzte
/// Rate = Rest); bei Austritt wird der Restsaldo mit dem letzten Lohn
/// fällig. Erstattungen sind NIE ein Darlehen.
/// </summary>
public class EmployeeDarlehen
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int CompanyProfileId { get; set; }

    /// <summary>Verwendungszweck, z.B. «Vorschuss Hochzeit» oder «QST-Nachzahlung Jul–Aug 2026».</summary>
    public string Zweck { get; set; } = "";

    /// <summary>Darlehensbetrag (positiv).</summary>
    public decimal Betrag { get; set; }

    /// <summary>Auszahlungs-/Gewährungsdatum. Bei AuszahlungArt=LOHN bestimmt
    /// es die Lohnperiode, in der der Betrag mit dem Lohn ausbezahlt wird.</summary>
    public DateOnly? AuszahlungDatum { get; set; }

    /// <summary>Wie der Betrag zum MA kommt (Walter 29.08.2026):
    /// BAR = bar aus dem Tresor (Vertrag trägt Bar-Quittungszeile) ·
    /// LOHN = mit der Lohnzahlung der Auszahlungs-Periode (Zeile auf dem
    /// Lohnbeleg, erhöht den Auszahlungsbetrag) ·
    /// KEINE = keine Auszahlung an den MA (z.B. QST-Nachbelastung — das Geld
    /// ging an die Behörde).</summary>
    public string AuszahlungArt { get; set; } = "BAR";

    /// <summary>Monatliche Rate; die letzte Rate ist der Restbetrag.</summary>
    public decimal RateBetrag { get; set; }

    /// <summary>Erste Verrechnungs-Periode.</summary>
    public int StartJahr { get; set; }
    public int StartMonat { get; set; }

    /// <summary>OFFEN / GETILGT / STORNIERT.</summary>
    public string Status { get; set; } = "OFFEN";

    public string? Bemerkung { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? CreatedBy { get; set; }

    [JsonIgnore] public Employee? Employee { get; set; }
}

/// <summary>
/// Verrechnete Rate (Historie) — eine Zeile pro Darlehen + Lohnperiode.
/// Entsteht beim GF-Bestätigen (ConfirmPayroll) atomar mit dem Snapshot;
/// DeletePeriode entfernt die Raten der Periode wieder (Saldo lebt auf).
/// </summary>
public class EmployeeDarlehenRate
{
    public int Id { get; set; }
    public int DarlehenId { get; set; }
    public int EmployeeId { get; set; }
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public decimal Betrag { get; set; }
    /// <summary>Restsaldo NACH dieser Rate.</summary>
    public decimal SaldoNachher { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonIgnore] public EmployeeDarlehen? Darlehen { get; set; }
}

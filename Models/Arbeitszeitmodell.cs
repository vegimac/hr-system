namespace HrSystem.Models;

/// <summary>
/// Betriebliches Arbeitszeitmodell einer Rechtseinheit (Walter 27.09.2026).
/// Swissdec meldet sie als <c>CompanyWorkingTime</c>; jede Person verweist über
/// <c>companyWorkingTimeIDRef</c> auf genau eines. Die Muster AG führt vier:
/// 42 h, 40 h, 21 Lektionen sowie 20 h + 10 Lektionen.
///
/// <para><b>Die Lohnrechnung liest dieses Modell NICHT.</b> Sie arbeitet
/// unverändert mit den Wochenstunden der Filiale (<c>normal_weekly_hours</c>,
/// daraus die betriebsüblichen Monatsstunden). Das Modell ist ausschliesslich
/// eine Meldeangabe — sonst würde eine Meldedatei den Lohn verändern.</para>
/// </summary>
public class Arbeitszeitmodell
{
    public int Id { get; set; }

    /// <summary>Rechtseinheit, zu der das Modell gehört.</summary>
    public int HauptsitzId { get; set; }
    public Hauptsitz? Hauptsitz { get; set; }

    /// <summary>
    /// Kennung für die Meldung, z.B. «CompanyWorkingTime1». Leer = wird aus der
    /// Id gebildet. Stabil halten: die Personen verweisen darauf.
    /// </summary>
    public string? Kennung { get; set; }

    /// <summary>Klartext für die Auswahl («Standard», «Lager», «Lehrpersonen»).</summary>
    public string Bezeichnung { get; set; } = "";

    /// <summary>Wochenstunden. NULL/0 zusammen mit den Lektionen = unbrauchbar.</summary>
    public decimal? Wochenstunden { get; set; }

    /// <summary>Wochenlektionen (Lehrpersonen). Eines von beiden muss gesetzt sein.</summary>
    public decimal? Wochenlektionen { get; set; }

    /// <summary>Ferientage pro Jahr, wenn das Modell sie vorgibt. NULL = aus dem Vertrag.</summary>
    public decimal? FerientageProJahr { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Brauchbar für eine Meldung? Sind Stunden UND Lektionen leer oder 0, darf die
    /// Meldung nicht erzeugt werden — Swissdec verlangt einen der beiden Werte.
    /// </summary>
    public bool IstMeldefaehig =>
        (Wochenstunden is > 0m) || (Wochenlektionen is > 0m);

    public string KennungOderId => string.IsNullOrWhiteSpace(Kennung) ? $"CompanyWorkingTime{Id}" : Kennung!.Trim();
}

/// <summary>
/// Zuordnung Mitarbeiter → Arbeitszeitmodell MIT Gültig-ab (Walter 27.09.2026).
/// Bewusst eine eigene Verlaufstabelle und KEIN Feld am Vertrag: das Modell kann
/// ohne Vertragswechsel ändern (Muster AG: TF12 Casanova wechselt per 01.10.2025
/// von Modell 1 auf 2, TF25 Lehmann per 01.04.2025 von 2 auf 1). Ein neues
/// Vertragsstück nur wegen einer Meldeangabe wäre eine Fälschung der Vertragshistorie.
/// </summary>
public class EmployeeArbeitszeitmodell
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public int ArbeitszeitmodellId { get; set; }
    public Arbeitszeitmodell? Arbeitszeitmodell { get; set; }

    /// <summary>Ab diesem Tag gilt das Modell. Das jüngste Datum ≤ Stichtag gewinnt.</summary>
    public DateOnly GueltigAb { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? Bemerkung { get; set; }
}

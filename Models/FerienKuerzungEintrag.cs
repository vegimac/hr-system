namespace HrSystem.Models;

/// <summary>
/// Absenzbedingte Ferienkürzung (Art. 329b OR / L-GAV), bewusst von HR
/// erfasst (Walter-Vorgabe 23.09.2026). Ersetzt das frühere Häkchen im
/// Lohnlauf. Erscheint in der Absenzen-Liste wie eine Absenz, ist aber
/// bewusst KEINE Absence-Zeile: sie ist kein Abwesenheitstag und darf weder
/// Stunden, MTP-Soll, Dienstplan-Import noch Berichte berühren — nur den
/// Ferien-Tagesaldo im Lohnlauf-Monat von <see cref="Datum"/>.
///   • Tage: höchstens «noch möglich» laut Rechnung (Server prüft), Vorschlag
///     = abgerundete ganze Tage, genauer Betrag erlaubt.
///   • Verzicht = true, Tage = 0: «bewusst nicht gekürzt» — stellt die
///     To-do-Warnung ab, bis neue Krankheitstage dazukommen.
/// </summary>
public class FerienKuerzungEintrag
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    /// <summary>Buchungsdatum: bestimmt den Lohnlauf-Monat (muss offen sein).</summary>
    public DateOnly Datum { get; set; }
    /// <summary>Beginn des Dienstjahres, dessen Krankheitstage gekürzt werden
    /// (kann das abgelaufene sein — gebucht wird trotzdem im offenen Monat).</summary>
    public DateOnly DienstjahrVon { get; set; }
    public decimal Tage { get; set; }
    public bool Verzicht { get; set; }
    public string? Bemerkung { get; set; }
    public int? DokumentId { get; set; }
    public string? ErstelltVon { get; set; }
    /// <summary>Lokalzeit (timestamp without time zone).</summary>
    public DateTime ErstelltAm { get; set; } = DateTime.Now;
}

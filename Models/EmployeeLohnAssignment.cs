namespace HrSystem.Models;

/// <summary>
/// Lohnabtretung eines Mitarbeiters an eine Behörde: z. B. Lohnpfändung
/// oder Vorschussabtretung an ein Sozialamt.
///
/// Berechnung pro Lohnlauf:
///   Abzug = max(0, Netto − Freigrenze)
///   Wenn Zielbetrag > 0: Abzug = min(Abzug, Zielbetrag − BereitsAbgezogen)
///
/// Beim Lohn-Bestätigen wird <see cref="BereitsAbgezogen"/> automatisch
/// um den aktuellen Abzug hochgezählt. Sobald BereitsAbgezogen ≥ Zielbetrag,
/// wird in Folgemonaten nichts mehr abgezogen (auch wenn ValidTo noch offen).
/// </summary>
public class EmployeeLohnAssignment
{
    public int      Id               { get; set; }
    public int      EmployeeId       { get; set; }
    public int      BehoerdeId       { get; set; }

    /// <summary>Freitext, z. B. "Lohnpfändung" oder "Vorschuss Sozialamt".</summary>
    public string   Bezeichnung      { get; set; } = "Lohnpfändung";

    /// <summary>
    /// Existenz-Minimum, das dem Mitarbeiter nach Abzügen bleiben muss.
    /// 0 = alles geht an die Behörde.
    /// </summary>
    public decimal  Freigrenze       { get; set; } = 0;

    /// <summary>
    /// Gesamt-Schuld, die abgearbeitet werden muss. 0 = unbegrenzt.
    /// </summary>
    public decimal  Zielbetrag       { get; set; } = 0;

    /// <summary>
    /// Kumulierter bisher abgezogener Betrag. Wird beim Lohn-Bestätigen aktualisiert.
    /// </summary>
    public decimal  BereitsAbgezogen { get; set; } = 0;

    public DateOnly ValidFrom        { get; set; }

    /// <summary>Letzter Tag der Gültigkeit (inklusiv). NULL = offen bis Widerruf.</summary>
    public DateOnly? ValidTo         { get; set; }

    /// <summary>Referenz-/Aktenzeichen für Korrespondenz mit dem Amt (z. B. "Pfändungsgruppe Nr. 22520697").</summary>
    public string?  ReferenzAmt      { get; set; }

    /// <summary>QR-Referenz (27-stellig) oder IBAN-Zahlungsreferenz für die Überweisung.</summary>
    public string?  ZahlungsReferenz { get; set; }

    public string?  Bemerkung        { get; set; }

    public DateTime CreatedAt        { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt        { get; set; } = DateTime.UtcNow;

    public Employee? Employee        { get; set; }
    public Behoerde? Behoerde        { get; set; }
}

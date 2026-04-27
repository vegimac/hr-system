namespace HrSystem.Models;

/// <summary>
/// Historien-Eintrag: tatsächlich abgezogener Betrag pro bestätigtem
/// Lohnlauf (PayrollSnapshot) × Lohnabtretung (EmployeeLohnAssignment).
///
/// Snapshot-Felder (Behörden-Name, IBAN/QR-IBAN, Referenzen, Bezeichnung)
/// werden zum Zeitpunkt des Bestätigens kopiert. Damit bleibt der Eintrag
/// auch dann korrekt dokumentiert, wenn später die Behörde umbenannt wird.
///
/// Verwendung:
///   • Reporting: "was ging in welchem Monat an welches Amt"
///   • DTA/pain.001-Zahlungsexport (aggregiert pro Behörde)
///   • Abacus-FIBU-Export (Buchungen, mit Beleg-Nr.)
///   • Korrekte Rück-Abwicklung bei Re-Confirm
///     (BereitsAbgezogen wird vor Insert reduziert, dann neu hochgezählt)
/// </summary>
public class PayrollLohnAbtretungEntry
{
    public int Id { get; set; }

    // ── Zuordnung ──────────────────────────────────────────────────────
    public int PayrollSnapshotId         { get; set; }
    public int EmployeeLohnAssignmentId  { get; set; }
    public int EmployeeId                { get; set; }
    public int BehoerdeId                { get; set; }

    // ── Periode (denormalisiert) ───────────────────────────────────────
    public int PeriodYear  { get; set; }
    public int PeriodMonth { get; set; }

    // ── Snapshot Abtretungs-Regel ──────────────────────────────────────
    public string? Bezeichnung      { get; set; }
    public string? ReferenzAmt      { get; set; }
    public string? ZahlungsReferenz { get; set; }

    // ── Snapshot Behörde (für Zahlungsauftrag) ─────────────────────────
    public string? BehoerdeName { get; set; }
    public string? Iban         { get; set; }
    public string? QrIban       { get; set; }

    // ── Beträge ────────────────────────────────────────────────────────
    public decimal Betrag                   { get; set; }
    public decimal BereitsAbgezogenVorher   { get; set; }
    public decimal BereitsAbgezogenNachher  { get; set; }

    // ── Export-Markierungen (später befüllt) ───────────────────────────
    public string?   FibuBelegnr        { get; set; }
    public DateTime? FibuExportiertAm   { get; set; }
    public DateTime? DtaExportiertAm    { get; set; }
    public string?   DtaExportRef       { get; set; }

    public string?  Bemerkung { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation (optional) ──────────────────────────────────────────
    public PayrollSnapshot?         Snapshot   { get; set; }
    public EmployeeLohnAssignment?  Assignment { get; set; }
    public Employee?                Employee   { get; set; }
    public Behoerde?                Behoerde   { get; set; }
}

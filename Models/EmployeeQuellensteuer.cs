namespace HrSystem.Models;

/// <summary>
/// Quellensteuer-Angaben pro Mitarbeiter.
/// Gilt jeweils für eine Periode (ValidFrom–ValidTo).
/// Entspricht dem Abacus-Quellensteuer-Formular.
/// </summary>
public class EmployeeQuellensteuer
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    // ── Gültigkeit ──────────────────────────────────────────────────────────
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo  { get; set; }

    // ── Berechnung ──────────────────────────────────────────────────────────
    /// <summary>Kürzel des Steuerkantons, z.B. "LU", "ZH", "BE"</summary>
    public string? Steuerkanton { get; set; }

    /// <summary>Kantonsname, z.B. "Luzern"</summary>
    public string? SteuerkantonName { get; set; }

    /// <summary>Wohngemeinde für QST, z.B. "Oberkirch"</summary>
    public string? QstGemeinde { get; set; }

    /// <summary>BFS-Gemeindenummer, z.B. 1095</summary>
    public int? QstGemeindeBfsNr { get; set; }

    /// <summary>Automatischen Tarifvorschlag verwenden</summary>
    public bool TarifvorschlagQst { get; set; } = true;

    /// <summary>Tarifcode, z.B. "A", "B", "C", "D", "H", "P"</summary>
    public string? TarifCode { get; set; }

    /// <summary>Tarifsbezeichnung, z.B. "Tarif für alleinstehende Personen"</summary>
    public string? TarifBezeichnung { get; set; }

    public int AnzahlKinder { get; set; } = 0;

    public bool Kirchensteuer { get; set; } = false;

    /// <summary>Offizieller QST-Code, z.B. "A0Y", "B1N"</summary>
    public string? QstCode { get; set; }

    public bool SpezielBewilligt { get; set; } = false;

    /// <summary>Steuerkategorie (Dropdown-Wert aus Kanton)</summary>
    public string? Kategorie { get; set; }

    /// <summary>Manuell überschriebener Prozentsatz (leer = automatisch aus Tarif)</summary>
    public decimal? Prozentsatz { get; set; }

    /// <summary>Mindestlohn für Satzbestimmung (Medianlohn), z.B. 4500.00</summary>
    public decimal? MindestlohnSatzbestimmung { get; set; }

    // ── Zusätzliche Angaben zum Partner ─────────────────────────────────────
    /// <summary>Partner-Mitarbeiter-ID (falls Partner im selben Betrieb)</summary>
    public int? PartnerEmployeeId { get; set; }

    public DateOnly? PartnerEinkommenVon { get; set; }
    public DateOnly? PartnerEinkommenBis { get; set; }

    /// <summary>Arbeitsort-Kanton des Partners, z.B. "LU"</summary>
    public string? ArbeitsortKanton { get; set; }

    // ── Zusätzliche Angaben ──────────────────────────────────────────────────
    public bool WeitereBeschaftigungen { get; set; } = false;

    /// <summary>Gesamtpensum bei weiteren Arbeitgebern in %</summary>
    public decimal? GesamtpensumWeitereAg { get; set; }

    // ── Anderer Arbeitgeber des MA (Walter-Vorgabe 25.08.2026): das
    //    kantonale Anmeldeformular verlangt die VOLLE Adresse des weiteren
    //    Arbeitgebers (Name/Strasse/PLZ/Ort/Kanton/Land) — das Einkommen
    //    wird bewusst NICHT mehr erfasst, nur das Stellenpensum. ─────────
    public string? WeitereAgName    { get; set; }
    public string? WeitereAgStrasse { get; set; }
    public string? WeitereAgPlz     { get; set; }
    public string? WeitereAgOrt     { get; set; }
    public string? WeitereAgKanton  { get; set; }
    public string? WeitereAgLand    { get; set; }

    /// <summary>Gesamteinkommen bei weiteren Arbeitgebern pro Monat in CHF</summary>
    public decimal? GesamteinkommenWeitereAg { get; set; }

    /// <summary>Halbfamilie-Status (Dropdown-Wert)</summary>
    public string? Halbfamilie { get; set; }

    /// <summary>Wohnsitz im Ausland (Dropdown, z.B. "Deutschland")</summary>
    public string? WohnsitzAusland { get; set; }

    /// <summary>ISO-Ländercode Wohnsitzstaat, z.B. "DE"</summary>
    public string? Wohnsitzstaat { get; set; }

    /// <summary>Adresse im Ausland (Freitext)</summary>
    public string? AdresseAusland { get; set; }

    // ── Tarif-relevante Stammdaten (für Anmeldung & Tarifbestimmung) ─────────
    /// <summary>Lebt im Konkubinat (ohne Trauschein zusammenlebend).</summary>
    public bool LivesInKonkubinat { get; set; } = false;

    /// <summary>Übt gemeinsam mit Partner die elterliche Sorge aus.</summary>
    public bool HasJointParentalCare { get; set; } = false;

    /// <summary>Zahlt Unterhalt für volljährige Kinder.</summary>
    public bool PaysAlimonyAdultChildren { get; set; } = false;

    /// <summary>Erzielt höheres Bruttoerwerbseinkommen als Partner (Elterntarif H/M).</summary>
    public bool HasHigherIncomeThanPartner { get; set; } = false;

    /// <summary>Grenzgänger/in mit täglicher Rückkehr.</summary>
    public bool IsGrenzgaenger { get; set; } = false;

    /// <summary>Wochenaufenthalter/in mit wöchentlicher Rückkehr.</summary>
    public bool IsWochenaufenthalter { get; set; } = false;

    // ── Beleg (Walter-Vorgabe 21.08.2026) ────────────────────────────────────
    /// <summary>
    /// Tarifbestätigung der Steuerbehörde als verknüpftes Dokument
    /// (FK auf employee_dokument, ON DELETE SET NULL) — gleicher Mechanismus
    /// wie Ehepartner-Ausweis/Bewilligungs-Beleg. Pro QST-Version ein Beleg.
    /// </summary>
    public int? DokumentId { get; set; }

    // ── Audit ────────────────────────────────────────────────────────────────
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

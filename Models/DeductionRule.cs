using System.ComponentModel.DataAnnotations.Schema;

namespace HrSystem.Models;

/// <summary>
/// Abzugsregel pro Filiale (AHV, ALV, KTG, UV, BVG, Quellensteuer etc.)
/// Orientiert sich an der Abacus-Kategoristruktur (500, 510, 550 …)
/// </summary>
public class DeductionRule
{
    public int Id { get; set; }
    public int CompanyProfileId { get; set; }

    /// <summary>Kategorie-Code, z.B. "500", "510", "550", "560"</summary>
    public string CategoryCode { get; set; } = "";

    /// <summary>Kategoriename, z.B. "AHV / IV / EO"</summary>
    public string CategoryName { get; set; } = "";

    /// <summary>Regelname innerhalb Kategorie, z.B. "Festangestellter"</summary>
    public string Name { get; set; } = "";

    /// <summary>"percent" oder "fixed"</summary>
    public string Type { get; set; } = "percent";

    /// <summary>Prozentsatz (z.B. 5.3) oder Fixbetrag in CHF</summary>
    public decimal Rate { get; set; }

    /// <summary>
    /// Arbeitgeber-Satz (%) für die AG-Sozialbeiträge im Fibu-Journal. NULL = kein
    /// AG-Anteil. Transient — kommt aus social_insurance_rate.rate_employer. WICHTIG
    /// bei alters-/modellgestaffelten Sätzen (z.B. BVG): der AG-Betrag wird hier in
    /// der Engine mit demselben Regel-Eintrag wie der AN-Abzug berechnet, damit die
    /// richtige Staffel-Stufe greift (das Journal kann das nicht zurückverfolgen).
    /// </summary>
    [NotMapped]
    public decimal? RateEmployer { get; set; }

    /// <summary>
    /// Berechnungsbasis:
    ///   "gross"  = auf Bruttolohn
    ///   "bvg"    = auf Bruttolohn minus Koordinationsabzug (monatl.)
    /// </summary>
    public string BasisType { get; set; } = "gross";

    /// <summary>Monatlicher Koordinationsabzug (nur für BVG relevant, z.B. 2143.75)</summary>
    public decimal? CoordinationDeduction { get; set; }

    /// <summary>Mindestalter für diese Regel (null = kein Limit), z.B. 18</summary>
    public int? MinAge { get; set; }

    /// <summary>Maximalalter für diese Regel (null = kein Limit), z.B. 64</summary>
    public int? MaxAge { get; set; }

    /// <summary>
    /// Monatlicher Freibetrag, der vor der Beitragsberechnung vom Bruttolohn
    /// abgezogen wird. Relevant für AHV 65+: CHF 1'400/Mt. (Stand 2026).
    /// Basis = max(0, Bruttolohn − FreibetragMonthly)
    /// </summary>
    public decimal? FreibetragMonthly { get; set; }

    /// <summary>
    /// Monatlicher Höchstlohn für die Beitragsbasis (ALV/NBU: CHF 12'350/Mt. =
    /// 148'200/Jahr). Ist gesetzt, wird die Basis auf diesen Wert gedeckelt
    /// (basis = min(basis, MaxBaseMonthly)). NULL = unbegrenzt (AHV/IV/EO).
    /// Transient — kommt aus social_insurance_rate.max_base_monthly.
    /// </summary>
    [NotMapped]
    public decimal? MaxBaseMonthly { get; set; }

    /// <summary>
    /// Flacher Monats-Cap auf die (koordinierte) Basis OHNE Jahresausgleich —
    /// z.B. BVG Max. pflichtiger Betrag 5'355/Mt. Anders als MaxBaseMonthly löst
    /// dieser Cap KEIN Dezember-Aufrollverfahren aus (das gilt nur für ALV/NBU).
    /// basis = min(basis, MaxBaseFlatMonthly). Transient (aus rate).
    /// </summary>
    [NotMapped]
    public decimal? MaxBaseFlatMonthly { get; set; }

    /// <summary>
    /// Min. pflichtige (koordinierte) Basis/Mt. — z.B. BVG 315. Versicherte zahlen
    /// mind. darauf, auch wenn (Brutto − Koordinationsabzug) kleiner ist. Transient.
    /// </summary>
    [NotMapped]
    public decimal? MinBaseMonthly { get; set; }

    /// <summary>
    /// Eintrittsschwelle/Jahr — z.B. BVG 22'680. Liegt der hochgerechnete Jahreslohn
    /// (Monats-Basis × 12) darunter, ist der MA nicht versichert → Basis = 0.
    /// Transient.
    /// </summary>
    [NotMapped]
    public decimal? EntryThresholdYearly { get; set; }

    /// <summary>Gilt nur für Mitarbeiter mit Quellensteuer-Pflicht</summary>
    public bool OnlyQuellensteuer { get; set; } = false;

    /// <summary>
    /// Vertragstyp-Filter: NULL = alle Modelle, 'FIX-M' = nur Management-Kader.
    /// Wird aus social_insurance_rate.employment_model_code übernommen.
    /// </summary>
    public string? EmploymentModelCode { get; set; }

    public DateOnly ValidFrom { get; set; } = new DateOnly(2026, 1, 1);
    public DateOnly? ValidTo { get; set; }

    public int SortOrder { get; set; } = 99;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Transient: effektiver Satz für die Anzeige auf dem Lohnzettel.
    /// Wird nur gesetzt, wenn die echte Rate ein CHF-Betrag ist (Type=fixed),
    /// aber trotzdem ein Prozent-Äquivalent angezeigt werden soll — etwa
    /// bei Quellensteuer: der Tarif liefert einen CHF-Betrag, der Satz
    /// für die Prozent-Spalte kommt aber separat aus dem Tarif.
    /// Nicht in der Datenbank.
    /// </summary>
    [NotMapped]
    public decimal? DisplayRatePercent { get; set; }

    public CompanyProfile? CompanyProfile { get; set; }
}

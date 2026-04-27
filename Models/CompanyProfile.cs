using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;

namespace HrSystem.Models;

public class CompanyProfile
{
    public int Id { get; set; }

    public string CompanyName { get; set; } = "";

    [Column("branch_name")]
    public string? BranchName { get; set; }

    public string? RestaurantCode { get; set; }

    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }

    public string? Phone { get; set; }
    public string? Email { get; set; }

    public decimal? NormalWeeklyHours { get; set; }
    public int? DefaultVacationWeeks { get; set; }
    public string? WorkLocation { get; set; }

    public int? PayrollPeriodStartDay { get; set; }
    public decimal? MaxPartTimeHoursPerWeek { get; set; }
    public bool AllowFirst3Months8PercentReduction { get; set; } = false;
    public bool HoldBackVacationPayout { get; set; } = true;

    /// <summary>
    /// Bemerkungstext am Ende der Lohnabrechnung (Fussnote).
    /// Bearbeitbar pro Filiale. Default = leerer Text.
    /// </summary>
    [Column("pdf_footer_text")]
    public string? PdfFooterText { get; set; }

    public int? NoticePeriodDuringProbationDays { get; set; }
    public int? NoticePeriodAfterProbationMonths { get; set; }
    public int? NoticePeriodFromTenthYearMonths { get; set; }

    public decimal? MinimumWageUnder18Monthly { get; set; }
    public decimal? MinimumWageUnder18Hourly { get; set; }

    public int? SelectedContractTemplateId { get; set; }

    // NEU: Lohnzuschläge
    public decimal? DefaultVacationPercent5Weeks { get; set; } = 10.64m;
    public decimal? DefaultVacationPercent6Weeks { get; set; } = 13.04m;
    public decimal? DefaultHolidayPercent { get; set; } = 2.27m;

    // Nachtstunden-Grenzen (Format "HH:mm", z.B. "00:00" und "07:00")
    public string? NightStartTime { get; set; } = "00:00";
    public string? NightEndTime   { get; set; } = "07:00";

    /// <summary>
    /// Anzahl 13.-ML-Auszahlungen pro Jahr.
    ///   12 = monatlich (Default)
    ///    4 = quartalsweise (Auszahlung in den Monaten 3, 6, 9, 12)
    ///    2 = halbjährlich  (Auszahlung in den Monaten 6, 12)
    ///    1 = jährlich      (Auszahlung nur im Dezember)
    /// Wirkt für FIX/FIX-M/MTP. UTP wird immer monatlich ausbezahlt.
    /// </summary>
    public int ThirteenthMonthPayoutsPerYear { get; set; } = 12;

    /// <summary>
    /// Wenn true: bei UTP- und MTP-Mitarbeitenden wird im Dezember-Lohnlauf
    /// das gesamte aktuelle Ferien-Geld-Saldo automatisch ausbezahlt
    /// (Lohnposition 195.3 "Ferien-Geld-Auszahlung"). Saldo geht auf 0.
    /// Bei Austritt mid-year weiterhin manuelle Buchung über 195.3-Zulage.
    /// FIX/FIX-M haben kein Ferien-Geld-Saldo — Flag wirkt dort nicht.
    /// </summary>
    public bool AutoFerienGeldAuszahlungDezember { get; set; } = true;

    public bool IsActive { get; set; } = true;

    // ── Zwischenverdienst / Behörden ─────────────────────────────────────────
    /// <summary>BUR-Nummer (Betriebseinheitenregister), Format CH-XXX.X.XXX.XXX-X</summary>
    public string? BurNummer { get; set; }

    /// <summary>UID-Nummer (Unternehmens-Identifikationsnummer), Format CHE-XXX.XXX.XXX</summary>
    public string? UidNummer { get; set; }

    /// <summary>NOGA-Branchen-Code (2–5 Stellen)</summary>
    public string? BranchenCode { get; set; }

    /// <summary>Name und Nummer der AHV-Ausgleichskasse</summary>
    public string? AhvKasse { get; set; }

    /// <summary>Name des BVG-Versicherers</summary>
    public string? BvgVersicherer { get; set; }

    /// <summary>Gesamtarbeitsvertrag (GAV) dem der Betrieb unterstellt ist</summary>
    public string? GavName { get; set; }

    /// <summary>true = Betrieb ist einem GAV unterstellt</summary>
    public bool IstGav { get; set; } = false;

    // ── Krankheits-Karenz ──────────────────────────────────────────────────
    /// <summary>
    /// Basis für das Karenzjahr:
    ///   ARBEITSJAHR  = ab MA-Eintrittsdatum (Default)
    ///   KALENDERJAHR = 01.01. – 31.12.
    /// </summary>
    public string KarenzjahrBasis { get; set; } = "ARBEITSJAHR";

    /// <summary>
    /// Maximale Karenztage Krankheit pro Jahr mit erhöhter Lohnfortzahlung (z.B. 14).
    /// Danach reduziert (z.B. auf 80%).
    /// </summary>
    public decimal KarenzTageMax { get; set; } = 14m;

    /// <summary>
    /// Maximale Karenztage Unfall pro Jahr mit erhöhter Lohnfortzahlung (Default 2).
    /// Berechnung identisch zu Krankheit — nur die Tage-Grenze ist typ. kleiner.
    /// </summary>
    public decimal KarenzTageMaxUnfall { get; set; } = 2m;

    /// <summary>
    /// Dauer der BVG-Wartefrist in KALENDERMONATEN (Default 3). Während
    /// dieser Zeit bleibt die BVG-Basis auf 100%-Lohn, auch wenn der MA
    /// nur 88%/80% erhält. Danach greift die Beitragsbefreiung (je nach
    /// AU-Grad). Krankheit und Unfall werden separat gezählt, da sie
    /// durch unterschiedliche Versicherungen abgedeckt sind.
    /// Quelle: GastroSocial-Merkblatt zur Arbeitsunfähigkeit (2025).
    /// </summary>
    public int BvgWartefristMonate { get; set; } = 3;

    // ── L-GAV-Vollzugsbeitrag (Jahresabrechnung) ──────────────────────────
    /// <summary>
    /// Wenn true: der L-GAV-Beitrag wird im Trigger-Monat oder im ersten
    /// Lohn nach Eintritt automatisch als Abzug (Lohnposition 600.24)
    /// eingefügt. Default true.
    /// </summary>
    public bool LgavAktiv { get; set; } = true;

    /// <summary>
    /// Monat (1-12) in dem der jährliche L-GAV-Abzug erfolgt. Default 1
    /// (Januar). Neue MA bekommen den Beitrag in ihrer ersten Lohnperiode,
    /// falls ihr Eintritt nach diesem Monat liegt.
    /// </summary>
    public int LgavTriggerMonat { get; set; } = 1;

    /// <summary>
    /// Voller Beitrag für FIX, FIX-M, und MTP mit > 50% Pensum
    /// UND > 6 Monaten Anstellung. Default 99.00 CHF.
    /// </summary>
    public decimal LgavBeitragVoll { get; set; } = 99m;

    /// <summary>
    /// Reduzierter Beitrag für MTP ≤ 50% Pensum, MTP mit Anstellung ≤ 6 Mt.,
    /// und alle UTP. Default 49.50 CHF.
    /// </summary>
    public decimal LgavBeitragReduziert { get; set; } = 49.5m;

    [JsonIgnore]
    [NotMapped]
    public string FullDisplayName =>
        string.IsNullOrWhiteSpace(BranchName)
            ? CompanyName
            : $"{CompanyName} {BranchName}";
}
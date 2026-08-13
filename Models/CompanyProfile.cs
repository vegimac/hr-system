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

    /// <summary>
    /// Standort-Kanton der Filiale (2-Zeichen-Code: LU, AG, BE, …).
    /// Massgeblich für die Familienzulagen-Berechnung (FAK richtet sich
    /// nach Betriebsstandort, NICHT nach Wohnort des MA wie die QST).
    /// Optional — wird im Filial-Edit-Modal gepflegt und kann via
    /// PLZ-Lookup vorgeschlagen werden.
    /// </summary>
    [Column("kanton_code")]
    public string? KantonCode { get; set; }

    /// <summary>
    /// Präfix für das Initial-Passwort der Mitarbeiter-Postfach-Accounts
    /// dieser Filiale. Typisch 2 Zeichen (z.B. "Su" für Sursee).
    /// Initial-Passwort = LoginPasswordPrefix + EmployeeNumber.
    /// </summary>
    [Column("login_password_prefix")]
    public string? LoginPasswordPrefix { get; set; }

    public string? Phone { get; set; }

    /// <summary>BUR-Nummer der örtlichen Einheit (BFS-Betriebsregister,
    /// 8 Zeichen) — für die LSE-Spalte AR «burNr» (Walter 13.08.2026).</summary>
    public string? BurNr { get; set; }
    /// <summary>UID fürs BFS (LSE-Spalte R «uidBFS», z.B. CHE-123.456.789).</summary>
    public string? UidBfs { get; set; }
    public string? Email { get; set; }

    public decimal? NormalWeeklyHours { get; set; }
    /// <summary>
    /// Maximale gestempelte Stunden pro Woche (Mo–So). Dient nur der
    /// Anzeige-/Warnfunktion im Stempelzeiten-Tab (rote Warnung wenn das
    /// Wochentotal der gestempelten Zeiten diesen Wert übersteigt). NULL =
    /// keine Grenze / keine Warnung. (Walter-Vorgabe 24.05.2026)
    /// </summary>
    public decimal? MaxWeeklyHours { get; set; }
    public int? DefaultVacationWeeks { get; set; }
    public string? WorkLocation { get; set; }

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
    public decimal? DefaultVacationPercent5Weeks { get; set; } = 10.65m;
    public decimal? DefaultVacationPercent6Weeks { get; set; } = 13.04m;
    public decimal? DefaultHolidayPercent { get; set; } = 2.27m;

    /// <summary>
    /// Walter-Vorgabe 06.06.2026: Alter, ab dem die 6-Wochen-Ferien-Regel
    /// greift. L-GAV-Standard = 50. Pro Filiale konfigurierbar, falls eine
    /// Filiale grosszügiger sein will oder der L-GAV ändert. Engine prüft
    /// in jeder Lohnperiode `dob.AddYears(VacationSixWeeksFromAge) <= periodTo`
    /// und upgradet vacationPct auf DefaultVacationPercent6Weeks.
    /// </summary>
    public int VacationSixWeeksFromAge { get; set; } = 50;

    /// <summary>
    /// Walter-Vorgabe 06.06.2026: 13.-Monatslohn-% pro Filiale (L-GAV-Standard
    /// = 8.33 %, monatlich akkumuliert). Engine + Arbeitsvertrags-PDF + Importer
    /// fallen darauf zurück, wenn der Vertrag selbst keinen Wert hat. Vertrags-
    /// Override greift weiterhin, falls explizit gesetzt (Sonderverträge).
    /// </summary>
    public decimal? DefaultThirteenthSalaryPercent { get; set; } = 8.33m;

    /// <summary>
    /// Probezeit-Vorgabe pro Filiale (Walter-Vorgabe 29.06.2026): gespeichert als
    /// 14 = 14 Tage, 1/2/3 = Monate. NULL = keine Vorgabe. Die Probezeit darf NICHT
    /// manuell verlängert werden; sie verlängert sich später automatisch bei
    /// Krankheit/Unfall/Absenzen (eigener Schritt). Grundlage für Schritt 2
    /// (Vertrag: „keine Probezeit" + Grund).
    /// </summary>
    public int? ProbationMonths { get; set; }

    // Nachtstunden-Grenzen (Format "HH:mm", z.B. "00:00" und "07:00")
    public string? NightStartTime { get; set; } = "00:00";
    public string? NightEndTime   { get; set; } = "07:00";

    /// <summary>
    /// Legacy: Anzahl 13.-ML-Auszahlungen pro Jahr. Bleibt für Rückwärts-
    /// Kompatibilität liegen; primär gilt jetzt ThirteenthMonthPayoutMonths.
    /// </summary>
    public int ThirteenthMonthPayoutsPerYear { get; set; } = 12;

    /// <summary>
    /// Auszahlungsmonate des 13. Monatslohns als CSV-String, z.B. "6,12"
    /// oder "1,2,3,4,5,6,7,8,9,10,11,12" für monatlich. Definiert in
    /// welchen Monaten der akkumulierte 13.-ML-Saldo ausbezahlt wird.
    /// Wirkt für FIX/FIX-M/MTP. UTP wird immer monatlich abgerechnet.
    /// Auszahlungs-Logik unterscheidet sich zwischen den Modellen:
    ///   • MTP    → Auszahlung = nur prevThirteenth (Saldo bis Vormonat),
    ///              aktueller Monat geht in nächste Periode
    ///   • FIX/FIX-M → Auszahlung = prevThirteenth + currentAccrual
    ///                 (inkl. aktueller Monat)
    /// </summary>
    public string? ThirteenthMonthPayoutMonths { get; set; }

    /// <summary>
    /// Wenn true: bei UTP- und MTP-Mitarbeitenden wird im Dezember-Lohnlauf
    /// das gesamte aktuelle Ferien-Geld-Saldo automatisch ausbezahlt
    /// (Lohnposition 195.3 "Ferien-Geld-Auszahlung"). Saldo geht auf 0.
    /// Bei Austritt mid-year weiterhin manuelle Buchung über 195.3-Zulage.
    /// FIX/FIX-M haben kein Ferien-Geld-Saldo — Flag wirkt dort nicht.
    /// </summary>
    public bool AutoFerienGeldAuszahlungDezember { get; set; } = true;

    /// <summary>
    /// Lohnausweis Box F (Form 11 dfe): "Unentgeltliche Beförderung
    /// zwischen Wohn- und Arbeitsort". Bei McDonald's typischerweise false
    /// (kein Werks-Bus).
    /// </summary>
    public bool LohnausweisBoxFFreierTransport { get; set; } = false;

    /// <summary>
    /// Lohnausweis Box G: "Kantinenverpflegung / Lunch-Checks". TRUE wenn
    /// MA unentgeltlich Verpflegung erhalten. Bei Schaub Restaurants
    /// false, weil die Crew 50% des Crew-Meal-Preises bezahlt — keine
    /// unentgeltliche Leistung im Sinn des Lohnausweises.
    /// </summary>
    public bool LohnausweisBoxGKantineGratis { get; set; } = false;

    /// <summary>
    /// Lohnausweis Position 2.1 Verpflegung/Unterkunft (Geldwert pro
    /// Monat in CHF). Bei korrekter 50%-Beteiligung der MA = 0; falls
    /// eine Filiale Standard-Restbetrag deklariert (über ESTV-Pauschale
    /// von CHF 645/Monat hinaus), hier eintragen.
    /// </summary>
    public decimal? LohnausweisPos21VerpflegungMonat { get; set; }

    public bool IsActive { get; set; } = true;

    // ── Bankverbindung der Filiale (Auftraggeber-Konto für DTA / Lohnlauf) ──
    /// <summary>
    /// IBAN des Filial-Lohnkontos. Wird beim DTA (pain.001) als Auftraggeber-
    /// Konto verwendet — von hier geht der Sammelauftrag an die Hausbank,
    /// von dort werden alle MA-Löhne ausbezahlt.
    /// </summary>
    public string? Iban { get; set; }

    /// <summary>BIC der Filial-Hausbank (z.B. POFICHBEXXX für PostFinance, RAIFCH22XXX für Raiffeisen).</summary>
    public string? Bic { get; set; }

    /// <summary>Name der Hausbank (z.B. "PostFinance AG", "Raiffeisenbank Sursee"). Optional, automatisch via IID-Lookup gefüllt.</summary>
    public string? BankName { get; set; }

    // ── Zwischenverdienst / Behörden ─────────────────────────────────────────
    /// <summary>BUR-Nummer (Betriebseinheitenregister), Format CH-XXX.X.XXX.XXX-X</summary>
    public string? BurNummer { get; set; }

    /// <summary>UID-Nummer (Unternehmens-Identifikationsnummer), Format CHE-XXX.XXX.XXX</summary>
    public string? UidNummer { get; set; }

    /// <summary>NOGA-Branchen-Code (2–5 Stellen)</summary>
    public string? BranchenCode { get; set; }

    /// <summary>
    /// SSL-Nummern der Filiale. Eine SSL-Nummer pro Kanton, in dem die
    /// Filiale quellensteuerpflichtige Mitarbeitende beschäftigt — die
    /// Nummer wird jeweils vom kantonalen Steueramt vergeben und ist
    /// kanton- UND filialspezifisch (siehe <see cref="CompanyProfileSsl"/>).
    /// </summary>
    public List<CompanyProfileSsl> SslNummern { get; set; } = new();

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

    // ── Akonto-Lohn ────────────────────────────────────────────────────────
    /// <summary>
    /// Akonto-Prozentsatz für FIX (Akonto-Lohn-Modell). Das Akonto für FIX
    /// = AkontoProzentFix % des voraussichtlich ausbezahlten Monatslohns.
    /// Default 80 %, pro Filiale im Einstellungen-Tab änderbar. Siehe
    /// AKONTO-LOHN-PLAN.md, Abschnitt 2.2 / 4.4.
    /// </summary>
    public decimal AkontoProzentFix { get; set; } = 80m;

    /// <summary>
    /// Akonto-Prozentsatz für FIX-M (Management-Festlohn) — separat von FIX,
    /// da Manager planbare hohe Festlöhne haben und ein höheres Akonto
    /// vertragen (Walter-Vorgabe 18.05.2026). Default 90 %.
    /// </summary>
    public decimal AkontoProzentFixM { get; set; } = 90m;

    /// <summary>
    /// Akonto-Prozentsatz für UTP/MTP (Walter-Vorgabe 16.05.2026, Regel 5/6).
    /// Wird angewendet auf (gestempelte Stunden × Rate + Ferien-Pott − SV-Abzüge).
    /// Default 100 % = voller Anspruch wird ausbezahlt. Konservativer Wert (z.B.
    /// 95 %) baut einen Sicherheitspuffer falls Stempelzeiten noch korrigiert werden.
    /// </summary>
    public decimal AkontoProzentHourly { get; set; } = 100m;

    [JsonIgnore]
    [NotMapped]
    public string FullDisplayName =>
        string.IsNullOrWhiteSpace(BranchName)
            ? CompanyName
            : $"{CompanyName} {BranchName}";
}
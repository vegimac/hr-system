using System.ComponentModel.DataAnnotations.Schema;
namespace HrSystem.Models;

public class Employee
{
    public int Id { get; set; }

    public string EmployeeNumber { get; set; } = "";

    /// <summary>
    /// Alte/zweite Personalnummern (Walter-Vorgabe 21.06.2026): eigene Tabelle
    /// <see cref="EmployeeNumberAlias"/> (ersetzt die früheren Felder alt1/alt2 —
    /// ein MA kann beliebig viele alte Nummern haben).
    /// </summary>
    public List<EmployeeNumberAlias> NumberAliases { get; set; } = new();

    public string? Salutation { get; set; }

    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? MaidenName { get; set; }
    public string? ShortName { get; set; }

    public string? Street { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }

    /// <summary>Wohnkanton als 2-Zeichen-Code (ZH, BE, AG, ...). NULL = nicht gepflegt.</summary>
    public string? CantonCode { get; set; }

    public DateTime? DateOfBirth { get; set; }

    // alter Textwert vorläufig behalten
    public string? Nationality { get; set; }

    // neue saubere Referenz
    public int? NationalityId { get; set; }

    public string? LanguageCode { get; set; }

    public string? PhoneMobile { get; set; }
    public string? Phone2 { get; set; }
    public string? Email { get; set; }

    public DateTime? EntryDate { get; set; }
    public DateTime? ExitDate { get; set; }

    /// <summary>Kündigung ausgesprochen am (Walter 16.07.2026) — wird beim
    /// Erstellen des Kündigungsschreibens gesetzt, beim Kündigungsrückzug
    /// gelöscht. NICHT das Austrittsdatum (das kann früher liegen).</summary>
    public DateTime? KuendigungAusgesprochenAm { get; set; }

    /// <summary>Kündigung per (letzter Arbeitstag gemäss Kündigungsschreiben).
    /// 2 Wochen vor Ablauf erscheint eine ToDo «Vertragsende wegen Kündigung».</summary>
    public DateTime? KuendigungPer { get; set; }

    /// <summary>Kündigung durch: «AG» = durch uns (Arbeitgeber), «AN» = durch
    /// Mitarbeiter (Arbeitnehmer). Null = nicht gesetzt. Walter 26.07.2026.</summary>
    public string? KuendigungDurch { get; set; }

    /// <summary>Austrittsgrund (Code, siehe <see cref="AustrittsgrundCodes"/>).
    /// Für Statistik — kurz gehalten. Walter 26.07.2026.</summary>
    public string? Austrittsgrund { get; set; }

    public int? PermitTypeId { get; set; }
    // PermitExpiryDate (denormalisierte Kopie) entfernt 01.06.2026 — Dashboard-Warnung
    // läuft jetzt über EmployeePermitHistory.ValidTo des jüngsten Eintrags.

    /// <summary>
    /// ZEMIS-Nummer (Zentrales Migrationsinformationssystem), Format 12345678.9.
    /// Bleibt während des ganzen Aufenthalts in der Schweiz gleich, auch wenn
    /// die Bewilligung wechselt (B → C → CH). Daher als personenbezogene
    /// Stammdaten und nicht bei der Bewilligung gepflegt.
    /// EINZIGES ZEMIS-Feld (Walter 12.07.2026): die Ausweis-OCR schreibt
    /// ebenfalls hierhin — das kurzlebige Duplikat zemis_nr wurde entfernt.
    /// </summary>
    public string? ZemisNumber { get; set; }

    /// <summary>
    /// Datum ab dem der Mitarbeiter von der Quellensteuer befreit ist.
    /// Null = QST-pflichtig (solange Nationalität ≠ CH).
    /// Wird gesetzt, sobald der MA einen C-Ausweis oder CH-Bürgerrecht erhält.
    /// Legacy-Feld — die neue Pflicht-Prüfung läuft über `QstPflichtCheckService`,
    /// der CH-Bürgerschaft / C-Ausweis / Behörden-Befreiung / Spouse-Status
    /// zur Laufzeit kombiniert.
    /// </summary>
    public DateOnly? QuellensteuerBefreitAb { get; set; }

    // ── QST-Befreiung durch die Steuerbehörde (Walter-Vorgabe 26.05.2026) ──
    /// <summary>True = der MA hat ein Bestätigungsschreiben der Steuerbehörde,
    /// das ihn von der QST befreit (z.B. wegen Doppelbesteuerungsabkommen,
    /// Diplomatenstatus, etc.). Das Schreiben muss als Dokument im MA-Doku-
    /// Tab hochgeladen UND via `QstBefreiungDokumentId` verlinkt sein.</summary>
    public bool QstBefreitDurchBehoerde { get; set; } = false;

    /// <summary>FK auf das Bestätigungsschreiben in `employee_dokument`.
    /// Pflicht wenn `QstBefreitDurchBehoerde = true`. ON DELETE SET NULL.</summary>
    public int? QstBefreiungDokumentId { get; set; }

    /// <summary>Befreiung gilt ab diesem Datum (Pflicht wenn befreit).</summary>
    public DateOnly? QstBefreiungGueltigAb { get; set; }

    /// <summary>Befreiung gilt bis diesem Datum. NULL = unbefristet.</summary>
    public DateOnly? QstBefreiungGueltigBis { get; set; }

    /// <summary>
    /// Walter-Vorgabe 13.06.2026: Beleg für QST-Befreiung als CH-Bürger.
    /// FK auf das hochgeladene Dokument (Pass ODER Identitätskarte). NULL =
    /// kein Beleg verknüpft → roter Warnbanner im QST-Tab + Dashboard-Card.
    /// </summary>
    public int? IdPassDokumentId { get; set; }

    /// <summary>
    /// Walter-Vorgabe 13.06.2026: Beleg für QST-Befreiung als C-Ausweis-
    /// Inhaber. FK auf das hochgeladene Bewilligungs-Dokument. NULL = kein
    /// Beleg verknüpft → roter Warnbanner im QST-Tab + Dashboard-Card.
    /// </summary>
    public int? CAusweisDokumentId { get; set; }

    /// <summary>
    /// Nachtarbeit-Untersuchung (Walter-Vorgabe 20.06.2026 / 26.07.2026): Gültig-bis
    /// IMMER selbst gerechnet aus <see cref="NightWorkExamIssued"/> (Beginn + 2 Jahre
    /// − 1 Tag, ab Alter 45: + 1 Jahr − 1 Tag). easy@work-«to» ist UTC-inkonsistent
    /// und wird NICHT als Quelle übernommen — nur zur Kontrolle
    /// (<see cref="NightWorkExamEasyMismatch"/>).
    /// </summary>
    public DateTime? NightWorkExamValidUntil { get; set; }

    /// <summary>
    /// Ausstellungs-/Beginndatum — beim Sync 1:1 aus easy@work «from» (UTC→Zürich).
    /// </summary>
    public DateTime? NightWorkExamIssued { get; set; }

    /// <summary>
    /// true = easy@work-«to» fehlt oder entspricht keiner UTC-Lesart dem Soll-Ende
    /// (Walter 26.07.2026). OneCrew speichert trotzdem das korrekte gerechnete Ende;
    /// Chip/ToDo fordern Korrektur in easy@work.
    /// </summary>
    public bool NightWorkExamEasyMismatch { get; set; }

    /// <summary>
    /// Zentrale Nachtarbeit-Regel (ArG): gültig bis = Beginn + N Jahre − 1 Tag,
    /// N = 1 ab Alter 45 (am Ausstellungstag), sonst 2. EINE Quelle für Sync,
    /// manuellen Endpoint und Dashboard-Kontrolle.
    /// </summary>
    public static DateOnly NightWorkValidUntil(DateOnly issued, DateOnly? dob)
    {
        int years = 2;
        if (dob.HasValue)
        {
            int age = issued.Year - dob.Value.Year;
            if (issued < dob.Value.AddYears(age)) age--;
            if (age >= 45) years = 1;
        }
        return issued.AddYears(years).AddDays(-1);
    }

    /// <summary>FK auf das hinterlegte Dokument (Arztbericht/Eignungszeugnis ODER Verzichtserklärung).</summary>
    public int? NightWorkExamDokumentId { get; set; }

    /// <summary>FK auf die hinterlegte unterschriebene „Ausnahmeregelung Tag-/Nachtarbeit"
    /// (Walter 22.06.2026, ArG) — zweiter Beleg neben Arztbericht/Verzicht für die Kontrolle.</summary>
    public int? NightWorkAusnahmeDokumentId { get; set; }

    /// <summary>
    /// Probezeitgespräch 1/2 (Walter 20.07.2026, Restaurant Admin): Datum der
    /// Durchführung + verknüpftes ausgefülltes Protokoll (Dokumenttyp
    /// «Probezeitgespräch» unter Mitarbeiterentwicklung). Formular-Blanko:
    /// Assets/Forms/Probezeitgespraech_1_und_2.xlsx.
    /// </summary>
    public DateTime? ProbezeitGespraech1Am { get; set; }
    public int? ProbezeitGespraech1DokumentId { get; set; }
    public DateTime? ProbezeitGespraech2Am { get; set; }
    public int? ProbezeitGespraech2DokumentId { get; set; }

    /// <summary>
    /// Interne easy@work-Employee-ID (Walter 17.06.2026). Wird beim MA-Sync
    /// gesetzt und erlaubt das Auflösen von edited_by_id-Verweisen aus den
    /// Stempelzeit-Audits zum Manager-Namen.
    /// </summary>
    public int? EasyAtWorkEmployeeId { get; set; }

    /// <summary>
    /// Verschollen-Wächter (Walter 05.08.2026): gesetzt, wenn der Nacht-Sync
    /// diesen AKTIVEN, easy@work-verknüpften MA in KEINER Aktiv-Liste der
    /// gemappten Filialen mehr findet (typisch: Wechsel zu einem fremden
    /// McDonald's-Franchise / vergessener Austritt). Dashboard zeigt dann in
    /// der Filiale eine kritische Warnung «Austritt prüfen». Wird vom Sync
    /// automatisch wieder gelöscht, sobald der MA wieder auftaucht.
    /// </summary>
    public DateOnly? EasyMissingSince { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Walter-Vorgabe 12.06.2026: MA wurde vom Admin gelöscht, hat aber noch
    /// Lohn-Daten (PayrollSnapshot/PayrollSaldo/AkontoZahlung) — er bleibt in
    /// der DB für Audit + Jahresauswertungen, wird aber in ALLEN MA-Listen,
    /// Pickern und im Lohnlauf ausgeblendet. NUR der hart-gelöschte Pfad
    /// entfernt die Zeile + alle Abhängigkeiten. Filterregel überall:
    /// `WHERE NOT IsHidden`. Default false; nur über DELETE-Endpoint gesetzt.
    /// </summary>
    public bool IsHidden { get; set; } = false;

    /// <summary>
    /// Walter-Vorgabe 07.06.2026: ist der MA dem L-GAV unterstellt? Wenn ja,
    /// rechnet der Lohnlauf den jährlichen L-GAV-Beitrag ab (volle/halbe Höhe
    /// gemäss Wochenstunden bzw. Betriebszugehörigkeit — folgt in Stufe 2).
    /// Default = true bei NEUanlage (Schaub Restaurants ist L-GAV-Branche).
    /// </summary>
    public bool LgavPflichtig { get; set; } = true;

    /// <summary>
    /// Walter-Vorgabe 07.06.2026: arbeitet der MA weniger als 8 Stunden pro
    /// Woche? Dann zahlt er KEINE NBU (Nicht-Berufs-Unfall-Versicherung).
    /// Default = false (Standard ist NBU-pflichtig).
    /// </summary>
    public bool TeilzeitUnter8hWoche { get; set; }

    /// <summary>
    /// True = MA wird im HR-System geführt (z.B. weil er als Vorgesetzter im
    /// Stempelsystem oder einem anderen Drittsystem benötigt wird), aber NICHT
    /// im Lohn-Tab gerechnet. Beispiel: Restaurant-Manager der über McDonald's-
    /// Zentrale bezahlt wird, aber Stempelzeiten der Crew freigeben muss.
    /// Auswirkungen: Lohn-Tab listet ihn nicht auf, kein Lohnzettel, keine
    /// QST-Anmeldung, kein 13. ML — kein Payroll-Touchpoint. Beim CSV-Re-
    /// Import wird die Flag NICHT überschrieben.
    /// Setzbar nur durch admin / superuser.
    /// </summary>
    public bool IsPayrollExcluded { get; set; } = false;

    /// <summary>
    /// Manueller KTG/UVG-Tagessatz (100 %) für Legacy-MA aus dem alten
    /// Lohnsystem. Wenn gesetzt: übersteuert die Auto-Berechnung des
    /// KtgTagessatzService. Die 88-/80-%-Stufen werden weiterhin daraus
    /// abgeleitet (× 0.88 bzw. × 0.80). NULL = Auto-Berechnung.
    /// </summary>
    public decimal? KtgTagessatzManuell { get; set; }

    /// <summary>
    /// Walter-Migration: true wenn die Karenzfrist (88 %) beim Wechsel
    /// vom alten Lohnsystem bereits abgelaufen ist. Bei Setzen wird im
    /// KTG/UVG-Tab kein 88-%-Schritt mehr angezeigt — die Versicherung
    /// startet direkt mit 80 %.
    /// </summary>
    public bool KtgKarenzAbgeschlossen { get; set; } = false;

    // Hinweis: Die Moments-Freigabe liegt seit 30.06.2026 in der eigenen Tabelle
    // employee_moment_consent (Model EmployeeMomentConsent), NICHT mehr als Bool-
    // Spalten am Employee. Die früheren moments_allow*-Spalten werden nicht mehr
    // gemappt (kein Lese-Zwang mehr auf eventuell fehlende Spalten).

    public PermitType? PermitType { get; set; }
    public Nationality? NationalityRef { get; set; }

    public List<Employment> Employments { get; set; } = new();
    public string? Gender { get; set; }

    /// <summary>AHV-Versichertennummer, Format 756.XXXX.XXXX.XX</summary>
    public string? SocialSecurityNumber { get; set; }

    /// <summary>
    /// Zivilstand. Mögliche Werte:
    ///   ledig | verheiratet | getrennt | geschieden | verwitwet
    ///   | eingetragene_partnerschaft | aufgeloeste_partnerschaft
    ///
    /// Hinweis "getrennt": rechtlich ist man bis zur Scheidung weiterhin
    /// verheiratet. Wir führen "getrennt" als Convenience-Wert für die UI,
    /// damit Walter es schnell auswählen kann; die QST-Anmeldung mappt
    /// es intern als "verheiratet + Trennung Ja" (siehe QstAnmeldungController).
    /// </summary>
    [Column("marital_status")]
    public string? MaritalStatus { get; set; }

    /// <summary>Datum, ab dem der aktuelle Zivilstand gilt (Heirat, Scheidung, Verwitwung).</summary>
    [Column("marital_status_since")]
    public DateOnly? MaritalStatusSince { get; set; }

    /// <summary>Getrennt lebend seit … (NULL = nicht getrennt). Persönliche Information,
    /// hat aber Auswirkung auf den QST-Tarif (verheiratet+getrennt = anderer Tarif).</summary>
    [Column("separated_since")]
    public DateOnly? SeparatedSince { get; set; }

    /// <summary>Konfession: evangelisch_reformiert | roemisch_katholisch | christ_katholisch | andere | keine.
    /// Allgemeines persönliches Datum (auch für Statistik / Kirchensteuer).</summary>
    [Column("religion")]
    public string? Religion { get; set; }

    /// <summary>Briefanrede für Korrespondenz-Vorlagen (z.B. "Sehr geehrte Frau Muster").
    /// Wenn leer, wird zur Laufzeit aus Anrede + Nachname gebildet.</summary>
    [Column("letter_salutation")]
    public string? LetterSalutation { get; set; }

    /// <summary>Heimatort (für Schweizer Bürger). Auf Lohnausweis bei
    /// Schweizer-Nationalität anstelle Wohnort möglich.</summary>
    [Column("place_of_origin")]
    public string? PlaceOfOrigin { get; set; }

    /// <summary>
    /// Laufende Schwangerschaft / Mutterschutz (Walter 20.07.2026) — nur für
    /// List-Anzeige; wird in GetAll aus employee_pregnancy gesetzt (bis 16
    /// Wochen nach Geburt/ET). Keine DB-Spalte.
    /// </summary>
    [NotMapped]
    public bool IsPregnant { get; set; }

    // Hinweis: LivesInKonkubinat, HasJointParentalCare, PaysAlimonyAdultChildren,
    // HasHigherIncomeThanPartner, IsGrenzgaenger, IsWochenaufenthalter sind in
    // EmployeeQuellensteuer (zeitlich versionierter QST-Eintrag) gewandert,
    // weil sie ausschliesslich für die QST-Tarifbestimmung relevant sind und sich
    // mit Lebenslagen ändern können (Heirat, Trennung, Geburt eines Kindes …).
}
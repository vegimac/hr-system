namespace HrSystem.Services.Elm;

/// <summary>
/// Zentrale Übersetzung der BFS-Codes in die Swissdec-Werte der Lohnstatistik
/// (Walter 27.09.2026). Ausbildung und berufliche Stellung werden in OneCrew
/// bereits für die LSE erfasst (<c>employee_lse.education</c> 1–8,
/// <c>employee_lse.position_override</c> bzw. <c>lse_code_mapping</c> 1–5) —
/// sie werden hier NICHT nochmals erfasst, sondern nur übersetzt. Eine zweite
/// Erfassung derselben Sache wäre eine Fehlerquelle.
///
/// <para>Quelle: Swissdec, Richtlinien für Lohndatenverarbeitung ELM 6.0,
/// Kap. 12 (Lohnstrukturerhebung); die Wertelisten stehen im Schema
/// (<c>EducationType</c>, <c>c:PositionType</c>).</para>
/// </summary>
public static class ElmStatistikCodes
{
    /// <summary>
    /// BFS-Ausbildung 1–8 → Swissdec. Die BFS-Skala der LSE:
    /// 1 Universität/ETH · 2 Fachhochschule/PH · 3 höhere Berufsbildung ·
    /// 4 Lehrerpatent · 5 Matura · 6 abgeschlossene Berufsausbildung ·
    /// 7 unternehmensinterne Ausbildung · 8 ohne abgeschlossene Berufsausbildung.
    /// Bei Uni und FH bestimmt der LSE-Hochschultitel (1 Doktorat, 2 Master, 3 Bachelor)
    /// die feinere Swissdec-Stufe (RefXML Jan 2025: TF09 Estermann doctorate, TF23 Koller
    /// universityBachelor); ohne Titel gilt Uni = Master, FH = Bachelor.
    /// </summary>
    public static string Ausbildung(int? bfsCode, int? hochschultitel = null) => (bfsCode, hochschultitel) switch
    {
        (1 or 2, 1) => "doctorate",
        (1, 3) => "universityBachelor",
        (2, 2) => "higherEducationMaster",
        (3, 2) => "higherVocEducationMaster",
        (3, 3) => "higherVocEducationBachelor",
        _ => AusbildungOhneTitel(bfsCode),
    };

    /// <summary>
    /// Wert für das XML. Eine Katalogzeile sticht die alte Zahlen-Übersetzung,
    /// damit eine angepasste Tabelle ohne Code-Änderung in die Meldung kommt.
    /// </summary>
    public static string AusbildungFuerMeldung(string? katalogCode, int? bfsCode, int? hochschultitel)
    {
        if (!string.IsNullOrWhiteSpace(katalogCode)) return katalogCode.Trim();
        return Ausbildung(bfsCode, hochschultitel);
    }

    /// <summary>Wie <see cref="AusbildungFuerMeldung"/> für die berufliche Stellung.</summary>
    public static string StellungFuerMeldung(string? katalogCode, int? bfsCode)
    {
        if (!string.IsNullOrWhiteSpace(katalogCode)) return katalogCode.Trim();
        return Stellung(bfsCode);
    }

    private static string AusbildungOhneTitel(int? bfsCode) => bfsCode switch
    {
        1 => "universityMaster",
        2 => "higherEducationBachelor",
        3 => "higherVocEducation",
        4 => "teacherCertificate",
        5 => "universityEntranceCertificate",
        6 => "vocEducationCompl",
        7 => "enterpriseEducation",
        8 => "mandatorySchoolOnly",
        // Ohne Angabe die vorsichtigste Annahme: keine abgeschlossene Ausbildung
        // behauptet nichts, was nicht belegt ist.
        _ => "mandatorySchoolOnly",
    };

    /// <summary>Ist die Ausbildung erfasst, oder ist der Wert nur der Rückfall?</summary>
    public static bool AusbildungErfasst(int? bfsCode) => bfsCode is >= 1 and <= 8;

    /// <summary>
    /// BFS-Stellung 1–5 → Swissdec. Beide Skalen laufen parallel:
    /// 1 oberstes Kader · 2 mittleres Kader · 3 unteres Kader ·
    /// 4 unterstes Kader · 5 ohne Kaderfunktion.
    /// </summary>
    public static string Stellung(int? bfsCode) => bfsCode switch
    {
        1 => "highestCadre",
        2 => "middleCadre",
        3 => "lowerCadre",
        4 => "lowestCadre",
        _ => "noCadre",
    };

    public static bool StellungErfasst(int? bfsCode) => bfsCode is >= 1 and <= 5;

    /// <summary>Rückrichtung für den Testmandanten: Swissdec-Wert → BFS-Ausbildungscode.</summary>
    public static int? BfsAusbildung(string? swissdec) => (swissdec ?? "").Trim() switch
    {
        "universityMaster" or "doctorate" or "universityBachelor" => 1,
        "higherEducationBachelor" or "higherEducationMaster" => 2,
        "higherVocEducation" or "higherVocEducationMaster" or "higherVocEducationBachelor" => 3,
        "teacherCertificate" => 4,
        "universityEntranceCertificate" => 5,
        "vocEducationCompl" => 6,
        "enterpriseEducation" => 7,
        "mandatorySchoolOnly" => 8,
        _ => null,
    };

    /// <summary>Rückrichtung für den Testmandanten: Swissdec-Wert → LSE-Hochschultitel (1 Doktorat, 2 Master, 3 Bachelor).</summary>
    public static int? BfsHochschultitel(string? swissdec) => (swissdec ?? "").Trim() switch
    {
        "doctorate" => 1,
        "universityMaster" or "higherEducationMaster" or "higherVocEducationMaster" => 2,
        "universityBachelor" or "higherEducationBachelor" or "higherVocEducationBachelor" => 3,
        _ => null,
    };

    /// <summary>Rückrichtung für den Testmandanten: Swissdec-Wert → BFS-Stellungscode.</summary>
    public static int? BfsStellung(string? swissdec) => (swissdec ?? "").Trim() switch
    {
        "highestCadre" => 1,
        "middleCadre" => 2,
        "lowerCadre" => 3,
        "lowestCadre" => 4,
        "noCadre" => 5,
        _ => null,
    };

    /// <summary>Topf der Lohnstatistik, in den eine Lohnart fällt.</summary>
    public enum Topf
    {
        /// <summary>Nicht in der Statistik (Spesen, Abzüge, Ausgleichsbuchungen).</summary>
        Keiner,
        Bruttolohn,          // MonthlyValues/GrossBaseSalaryAndRegularAllowance
        Zulagen,             // MonthlyValues/Allowances
        Familienzulagen,     // MonthlyValues/FamilyIncomeSupplement
        Drittleistungen,     // MonthlyValues/PaymentsByThird
        Kurzarbeit,          // MonthlyValues/ShortTimeWorkCompensation
        Ueberstunden,        // AnnualValues/Overtime
        Dreizehnter,         // AnnualValues/Earnings13th
        Unregelmaessig,      // AnnualValues/SporadicBenefits
        Naturalleistungen,   // AnnualValues/FringeBenefits
        Kapitalleistung,     // AnnualValues/CapitalPayment
        Uebrige,             // AnnualValues/OtherBenefits
    }

    /// <summary>
    /// Zuordnung einer Swissdec-Lohnart zum Statistik-Topf (Richtlinien ELM 6.0,
    /// Kap. 12 «Lohnstrukturerhebung»). Bewusst über die LOHNART, nicht über den
    /// Namen — Namen ändern, Nummern nicht.
    ///
    /// <para>An der Referenz 2024-11 nachgerechnet und damit belegt:
    /// 1005 + 1160 + 1161 = Bruttolohn 2'106.20 (TF14) · 1201 → 13. ML 175.45 ·
    /// 6000 Reisespesen gar nicht · 1010 Honorare = Bruttolohn 9'458.35 (TF16) ·
    /// 1212 → unregelmässig 3'000 · 1977 + 1978 → übrige 500 ·
    /// 1000 + 1001 = Bruttolohn 5'000 (TF37) · 3000 → Familienzulagen 250.</para>
    ///
    /// <para>Die übrigen Zuordnungen folgen den Nummernbereichen des
    /// Musterlohnartenstamms. Eine Lohnart, die hier NICHT vorkommt, wird gemeldet
    /// statt still einsortiert — <see cref="TopfFuer"/> gibt dann null zurück.</para>
    /// </summary>
    public static Topf? TopfFuer(int lohnart) => lohnart switch
    {
        // Grundlohn und regelmässige Zulagen
        1000 or 1001 or 1005 or 1006 or 1010 or 1033 => Topf.Bruttolohn,
        // Schicht-, Pikett-, Nacht- und Sonntagszulagen (RefXML Jan 2025: TF02 Schichtzulage 90 → Allowances)
        1070 or 1071 or 1072 or 1073 => Topf.Zulagen,
        1160 or 1161 or 1162 or 1163 or 1168 => Topf.Bruttolohn,
        // Verwaltungsratshonorar: LSE SporadicBenefits (RefXML Feb TF40 Farine:
        // Brutto 4'000, Sporadic 20'300 = 20'000 Sonderzulage + 300 VR).
        1500 => Topf.Unregelmaessig,
        // Überstunden und Überzeit
        1061 or 1065 or 1067 => Topf.Ueberstunden,
        // 13. und 14. Monatslohn
        1200 or 1201 or 1202 or 1205 => Topf.Dreizehnter,
        // Unregelmässige Zahlungen
        1203 or 1209 or 1210 or 1212 or 1216 or 1218 or 1230 or 1401 or 1420 => Topf.Unregelmaessig,
        // Kapitalleistungen mit Vorsorgecharakter
        1410 => Topf.Kapitalleistung,
        // Naturalleistungen und geldwerte Vorteile
        >= 1900 and <= 1969 => Topf.Naturalleistungen,
        // Vom Arbeitgeber übernommene Beiträge
        >= 1970 and <= 1989 => Topf.Uebrige,
        // Kurzarbeit / Schlechtwetter (2070 = ALV-Entschädigung KA — TF02 Paganini Feb:
        // 3'000+2'200+200 = 5'400 Kurzarbeit, nicht Drittleistungen)
        2060 or 2065 or 2070 or 2075 => Topf.Kurzarbeit,
        // Korrektur Taggelder: storniert Brutto/SV, gehört nicht in PaymentsByThird
        // (RefXML Feb TF10 Ganz: EO 1'000 + MDK 800 = 1'800 Drittleistungen, 2050 −1'800 nicht abgezogen)
        2050 => Topf.Keiner,
        // Ersatzeinkünfte über den Arbeitgeber
        >= 2000 and <= 2099 => Topf.Drittleistungen,
        // Familienzulagen
        >= 3000 and <= 3999 => Topf.Familienzulagen,
        // Abzüge und Ausgleichsbuchungen gehören nicht in die Lohntöpfe
        >= 5000 and <= 5999 => Topf.Keiner,
        // Spesen sind kein Lohn
        >= 6000 and <= 6999 => Topf.Keiner,
        _ => null,
    };

    public static bool IstMonat(Topf t)
        => t is Topf.Bruttolohn or Topf.Zulagen or Topf.Familienzulagen or Topf.Drittleistungen or Topf.Kurzarbeit;

    public static bool IstJahr(Topf t)
        => t is Topf.Ueberstunden or Topf.Dreizehnter or Topf.Unregelmaessig
            or Topf.Naturalleistungen or Topf.Kapitalleistung or Topf.Uebrige;

    public static Topf? AusText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return Enum.TryParse<Topf>(text.Trim(), ignoreCase: true, out var t) ? t : null;
    }

    /// <summary>
    /// An der Lohnart gespeicherter Topf. null = die Nummer entscheidet
    /// (<see cref="TopfFuer"/>). «Keiner» heisst ausdrücklich nicht melden.
    /// </summary>
    public static Topf? Gespeichert(string? monat, string? jahr)
    {
        var m = AusText(monat);
        var j = AusText(jahr);
        if (m == Topf.Keiner || j == Topf.Keiner) return Topf.Keiner;
        if (m is Topf monatlich && IstMonat(monatlich)) return monatlich;
        if (j is Topf jaehrlich && IstJahr(jaehrlich)) return jaehrlich;
        return m ?? j;
    }

    /// <summary>
    /// Ferienanspruch in Tagen pro Jahr für die Statistik.
    /// <para>
    /// Wer im Stunden- oder Lektionenlohn arbeitet und die Ferien als Prozentzuschlag
    /// bekommt, hat keinen Anspruch in Tagen — dort meldet die Referenz 0 (Muster AG
    /// Egli und Lusser). Sonst Ferienwochen × 5 Arbeitstage; die Wochen sind
    /// altersabhängig 5 oder 6 (L-GAV).
    /// </para>
    /// <para>Eine Handeingabe am MA sticht diese Regel.</para>
    /// </summary>
    public static decimal Ferientage(bool stundenlohnMitFerienProzent, int ferienwochen, decimal? handeingabe)
    {
        // Richtlinien ELM 6.0, S. 366: Ferienprozent und Ferientage werden NIE
        // zusammen gemeldet. Beim Stunden-/Lektionenlohn steht der Prozentsatz im
        // Block ContractualHourlyWage — dann sind die Tage zwingend 0, auch wenn
        // jemand von Hand etwas erfasst hat (Walter 27.09.2026, TF14 Egli).
        if (stundenlohnMitFerienProzent) return 0m;
        if (handeingabe is >= 0m) return handeingabe.Value;
        return ferienwochen * 5m;
    }

    /// <summary>
    /// Ferienwochen für einen vollen Kalenderjahr bei 100 %. Die Filiale gibt
    /// die Basis (5 oder schon 6). Liegt der Geburtstag der Schwelle am Stichtag
    /// oder davor, sind es sechs Wochen — dieselbe Schwelle wie der Ferienprozentsatz
    /// im Lohn (<c>VacationSixWeeksFromAge</c>).
    /// </summary>
    public static int Ferienwochen(DateTime? geburt, DateOnly stichtag, int schwelleAlter, int? filialWochen)
    {
        var basis = filialWochen is >= 6 ? 6 : 5;
        if (basis >= 6 || geburt is null) return basis;
        var jahre = schwelleAlter > 0 ? schwelleAlter : 50;
        var dob = DateOnly.FromDateTime(geburt.Value);
        return dob.AddYears(jahre) <= stichtag ? 6 : basis;
    }

    /// <summary>
    /// Was die Maske als Regel zeigt. Stundenlohn mit Ferienprozent: 0 Tage,
    /// eine Eingabe darüber wird abgewiesen. Monatslohn: Wochen × 5.
    /// Verwaltungsrat: 0, eine eigene Zahl bleibt erlaubt.
    /// </summary>
    public static FerienRegel FerienRegelFuer(
        string? modell, string? vertragsart, DateTime? geburt, DateOnly stichtag,
        int schwelleAlter, int? filialWochen, decimal ferienProzent)
    {
        var art = (vertragsart ?? "").Trim();
        if (art == "administrativeBoard")
            return new FerienRegel(0m,
                "Verwaltungsrat: kein Ferienanspruch. Ohne eigene Zahl wird 0 gemeldet.",
                false);

        var m = (modell ?? "").ToUpperInvariant();
        var monatslohn = m is "FIX" or "FIX-M" or "MTP";
        if (!monatslohn && ferienProzent > 0m)
        {
            var prozent = ferienProzent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                .Replace('.', ',');
            return new FerienRegel(0m,
                $"Stundenlohn mit Ferienentschädigung {prozent} %. Der Anspruch in Tagen ist 0. "
                + "Eine Zahl grösser als 0 widerspricht der Meldung an das BFS.",
                true);
        }

        var wochen = Ferienwochen(geburt, stichtag, schwelleAlter, filialWochen);
        var tage = wochen * 5m;
        var herkunft = wochen == 6 && filialWochen is not >= 6
            ? $"{wochen} Wochen ab Alter {schwelleAlter}"
            : $"{wochen} Wochen";
        return new FerienRegel(tage,
            $"Vertraglicher Anspruch bei 100 % und vollem Kalenderjahr: {herkunft} = {tage:0} Tage. "
            + "Bezogene Tage, Pensum, unbezahlter Urlaub und Feiertage zählen nicht.",
            false);
    }

    public readonly record struct FerienRegel(decimal Tage, string Text, bool HandAbweisen);
}

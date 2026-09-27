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
    /// Die drei Swissdec-Werte ohne BFS-Entsprechung (doctorate,
    /// higherEducationMaster, universityBachelor) kommen hier nicht vor.
    /// </summary>
    public static string Ausbildung(int? bfsCode) => bfsCode switch
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
        1070 or 1071 or 1072 or 1073 => Topf.Bruttolohn,
        1160 or 1161 or 1162 or 1163 or 1168 => Topf.Bruttolohn,
        1500 => Topf.Bruttolohn,                     // Verwaltungsratshonorar
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
        // Kurzarbeit / Schlechtwetter
        2060 or 2065 or 2075 => Topf.Kurzarbeit,
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
        if (handeingabe is >= 0m) return handeingabe.Value;
        if (stundenlohnMitFerienProzent) return 0m;
        return ferienwochen * 5m;
    }
}

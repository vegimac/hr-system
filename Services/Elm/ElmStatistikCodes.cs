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

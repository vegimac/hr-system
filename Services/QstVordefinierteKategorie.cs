using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Swissdec vordefinierte QST-Kategorien (ELM CategoryPredefined).
/// NICHT mit ESTV-Tarif M (Kapitalleistung 4.5 %) oder Grenzgänger-DE M mischen.
///
/// Satz: ESTV Satzart 11 in qst_sonderkategorie_satz (gültig von/bis).
/// Tarif M in Satzart 06 ist Kapitalleistung 4.5 % — nicht MEY. Unbekannter
/// Kanton/Jahr → null, nichts erfinden.
///
/// Zuordnung am MA: employee_quellensteuer.qst_code (versioniert).
/// Dieser Katalog ist das Wissen für HR, Warnungen und Seeds.
/// Walter 11.09.2026, TF30 Müller Jan: BE MEY 5'500 × 29.5 % = 1'622.50.
/// </summary>
public static class QstVordefinierteKategorie
{
    public enum Art { Men, Mey, Hen, Hey, Non, Noy, Sfn }

    public readonly record struct Eintrag(Art Art, string Code);

    public const string GruppeBeteiligung = "BETEILIGUNG";
    public const string GruppeVr = "VR";
    public const string GruppeKorrektur = "KORREKTUR";
    public const string GruppeFr = "FR_SONDER";

    public const string HinweisWohnsitzAusland =
        "Wohnsitz Ausland: bei exportierten Mitarbeiterbeteiligungen (Lohnart 1960) gilt MEN/MEY, "
        + "bei VR-Honorar (Lohnart 1500) HEN/HEY — nicht A/B/C/H. Normaler Lohn und diese Leistungen "
        + "getrennt behandeln. Unklar → mit dem QST-Amt abklären.";

    public static Eintrag? Parse(string? qstCode)
    {
        if (string.IsNullOrWhiteSpace(qstCode)) return null;
        var c = qstCode.Trim().ToUpperInvariant();
        return c switch
        {
            "MEN" => new Eintrag(Art.Men, c),
            "MEY" => new Eintrag(Art.Mey, c),
            "HEN" => new Eintrag(Art.Hen, c),
            "HEY" => new Eintrag(Art.Hey, c),
            "NON" => new Eintrag(Art.Non, c),
            "NOY" => new Eintrag(Art.Noy, c),
            "SFN" => new Eintrag(Art.Sfn, c),
            _ => null,
        };
    }

    public static bool IstNullAbzug(Art art)
        => art is Art.Non or Art.Noy or Art.Sfn;

    public static bool IstMitarbeiterbeteiligung(Art art)
        => art is Art.Men or Art.Mey;

    public static bool IstVerwaltungsrat(Art art)
        => art is Art.Hen or Art.Hey;

    public static string GruppeVon(Art art) => art switch
    {
        Art.Men or Art.Mey => GruppeBeteiligung,
        Art.Hen or Art.Hey => GruppeVr,
        Art.Non or Art.Noy => GruppeKorrektur,
        Art.Sfn => GruppeFr,
        _ => "",
    };

    /// <summary>
    /// Pauschalsatz in Prozent für MEN/MEY. Fallback wenn die Satz-Tabelle
    /// leer ist (Tests). Quellen: TaxInfo BE 29.5 %; ZH/LU-Merkblatt 31.5 %.
    /// </summary>
    public static decimal? MitarbeiterbeteiligungSatz(string? kanton)
        => (kanton ?? "").Trim().ToUpperInvariant() switch
        {
            "BE" => 29.5m,
            "ZH" => 31.5m,
            "LU" => 31.5m,
            _ => null,
        };

    /// <summary>VR-Honorar-Pauschale — Satz erst mit Beleg/Amtsangabe.</summary>
    public static decimal? VerwaltungsratSatz(string? kanton) => null;

    public static decimal? SatzFuer(string? code, string? kanton, DateOnly stichtag,
        IReadOnlyList<QstSonderkategorieSatz>? saetze)
    {
        var e = Parse(code);
        if (e == null) return null;
        if (IstNullAbzug(e.Value.Art)) return 0m;
        var kt = (kanton ?? "").Trim().ToUpperInvariant();
        if (saetze == null || saetze.Count == 0)
            return IstMitarbeiterbeteiligung(e.Value.Art) ? MitarbeiterbeteiligungSatz(kanton) : VerwaltungsratSatz(kanton);

        var hit = saetze
            .Where(s =>
                (string.Equals(s.Code, e.Value.Code, StringComparison.OrdinalIgnoreCase)
                    || (string.IsNullOrWhiteSpace(s.Code) && s.Gruppe == GruppeVon(e.Value.Art)))
                && string.Equals(s.Kanton, kt, StringComparison.OrdinalIgnoreCase)
                && s.ValidFrom <= stichtag
                && (s.ValidTo == null || s.ValidTo >= stichtag))
            .OrderByDescending(s => s.ValidFrom)
            .FirstOrDefault();
        return hit?.SatzPct;
    }

    public static IReadOnlyList<QstSonderkategorie> Katalog() =>
    [
        new()
        {
            Code = "HEN", Gruppe = GruppeVr, Kirchensteuer = false, AbzugArt = "LINEAR", SortOrder = 10,
            Bezeichnung = "VR-Honorar Ausland, ohne Kirchensteuer",
            Erklaerung = "Verwaltungsratshonorar an eine quellensteuerpflichtige Person mit Wohnsitz im Ausland, ohne Kirchensteuer. "
                + "Kein Tarif A/B/C/H. Swissdec: linearer Satz gemäss hinterlegter kantonaler Pauschale — nicht ESTV-Tarif H. "
                + "Hat dieselbe Person zusätzlich normalen Lohn, müssen die Leistungen getrennt laufen.",
            Automatik = "Wohnsitz Ausland und Lohnart 1500 (VR-Honorar), Konfession ohne Kirchensteuer → HEN. "
                + "Zusammen mit normalem Lohn: nicht automatisch, mit QST-Amt abklären.",
            Warnung = "Satz nur wenn für den Arbeitskanton hinterlegt. Sonst Beleg mit 0 und Rückfrage beim Amt.",
        },
        new()
        {
            Code = "HEY", Gruppe = GruppeVr, Kirchensteuer = true, AbzugArt = "LINEAR", SortOrder = 11,
            Bezeichnung = "VR-Honorar Ausland, mit Kirchensteuer",
            Erklaerung = "Wie HEN, aber mit Kirchensteuer (Y). Typisch: deutscher VR einer Schweizer AG, Landeskirche.",
            Automatik = "Wohnsitz Ausland und Lohnart 1500, Konfession kirchensteuerpflichtig → HEY.",
            Warnung = "Satz nur mit hinterlegter kantonaler Pauschale. Fehlt sie → mit QST-Amt klären.",
        },
        new()
        {
            Code = "MEN", Gruppe = GruppeBeteiligung, Kirchensteuer = false, AbzugArt = "LINEAR", SortOrder = 20,
            Bezeichnung = "Mitarbeiterbeteiligungen nach Wegzug, ohne Kirchensteuer",
            Erklaerung = "Leistungen aus exportierten Mitarbeiterbeteiligungen (Aktien, Optionen) an eine qsP mit Wohnsitz im Ausland, ohne Kirchensteuer. "
                + "Typisch: früher in der Schweiz erworben, nach dem Wegzug realisiert. Kein Tarif A/B/C/H und nicht ESTV-Tarif M (Kapitalleistung 4.5 %).",
            Automatik = "Wohnsitz Ausland und Lohnart 1960 (steuerbare Beteiligungsrechte), ohne Kirchensteuer → MEN. "
                + "Zusammen mit normalem Monatslohn: getrennt behandeln, mit Amt abklären.",
            Warnung = "Im McDonald's-Alltag praktisch nie. Fehlt der Kantonssatz → nicht erfinden, Amt fragen.",
        },
        new()
        {
            Code = "MEY", Gruppe = GruppeBeteiligung, Kirchensteuer = true, AbzugArt = "LINEAR", SortOrder = 21,
            Bezeichnung = "Mitarbeiterbeteiligungen nach Wegzug, mit Kirchensteuer",
            Erklaerung = "Wie MEN, aber mit Kirchensteuer. Referenz Swissdec TF30 Müller Januar 2025: Lohnart 1960, Kanton BE, 5'500 × 29.5 % = 1'622.50 (TaxInfo BE).",
            Automatik = "Wohnsitz Ausland und Lohnart 1960, Konfession kirchensteuerpflichtig → MEY.",
            Warnung = "Nicht mit ESTV-Tarif M (4.5 %) verrechnen.",
        },
        new()
        {
            Code = "NON", Gruppe = GruppeKorrektur, Kirchensteuer = false, AbzugArt = "NULL", SortOrder = 30,
            Bezeichnung = "Nicht quellensteuerpflichtig (Korrektur), ohne Kirchensteuer",
            Erklaerung = "Swissdec-Code für Korrekturen: ein Zeitraum war fälschlich mit A/B/C/H abgerechnet, war aber gar nicht QST-pflichtig. "
                + "Kein Abzug. In OneCrew kein normaler Status «MA ist nicht pflichtig» — dafür gelten CH/C/Ehepartner-Befreiung und das Schliessen der QST-Version.",
            Automatik = "Nur im Korrektur-Pfad (z. B. Testmandant PersonTASCode NON schliesst die offene Version). Nie automatisch statt einer Befreiung setzen.",
            Warnung = "Nicht als «kein QST-Eintrag nötig» verwenden.",
        },
        new()
        {
            Code = "NOY", Gruppe = GruppeKorrektur, Kirchensteuer = true, AbzugArt = "NULL", SortOrder = 31,
            Bezeichnung = "Nicht quellensteuerpflichtig (Korrektur), mit Kirchensteuer",
            Erklaerung = "Wie NON, Y = mit Kirchensteuer im ursprünglichen (falschen) Zeitraum. Abzug 0. Nur Korrektur.",
            Automatik = "Nur Korrektur-Pfad, nie als laufender Tarif.",
            Warnung = "Wie NON: nicht als Stammdaten-Status.",
        },
        new()
        {
            Code = "SFN", Gruppe = GruppeFr, Kirchensteuer = false, AbzugArt = "NULL", SortOrder = 40,
            Bezeichnung = "Sondervereinbarung Frankreich",
            Erklaerung = "Grenzgänger Frankreich in BE, BS, BL, JU, NE, SO, VD, VS: keine CH-Quellensteuer, wenn die jährliche Ansässigkeitsbescheinigung vorliegt und die Grenzgängerregel erfüllt ist (höchstens 45 Nichtrückkehrtage, höchstens 40 % Telearbeit). Sonst und in allen anderen Kantonen: ordentliche Tarife A/B/C/H.",
            Automatik = "Nicht ohne Bescheinigung setzen. Nachweis fehlt → ordentliche Tarife vorläufig, Status ROT. Bewiesen nicht erfüllt → ordentlich GRÜN.",
            Warnung = "K4.7: ohne Nachweis kein SFN.",
        },
    ];
}

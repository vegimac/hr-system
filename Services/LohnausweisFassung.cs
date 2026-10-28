using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Lohnausweis-Angaben gelten ab einem Jahr, bis bleibt offen.
/// Eine Änderung in einem späteren Jahr legt eine neue Fassung an
/// und schliesst die bisherige per Ende des Vorjahres.
/// </summary>
public static class LohnausweisFassung
{
    /// <summary>Übernommene Angaben ohne bekanntes Anfangsjahr gelten ab hier.</summary>
    public const int Beginn = 2000;

    public static T? FuerJahr<T>(IEnumerable<T> alle, int jahr, Func<T, int> ab, Func<T, int?> bis)
    {
        return alle
            .Where(x => ab(x) <= jahr && (bis(x) is not int ende || ende >= jahr))
            .OrderByDescending(ab)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gleiches Jahr: die offene Fassung fortschreiben (<paramref name="bisherBis"/> bleibt leer).
    /// Späteres Jahr: neue Fassung, die alte gilt bis zum Jahr davor.
    /// </summary>
    public static string? Entscheiden(int offenesAb, int jahr, out int? bisherBis)
    {
        bisherBis = null;
        if (jahr < 1990 || jahr > 2100)
            return "Gültig ab muss ein Jahr zwischen 1990 und 2100 sein.";
        if (jahr < offenesAb)
            return "Gültig ab liegt vor der offenen Fassung. Abgeschlossene Jahre bleiben unverändert.";
        if (jahr > offenesAb)
            bisherBis = jahr - 1;
        return null;
    }

    public static void AufProfil(CompanyProfile ziel, CompanyLohnausweis quelle)
    {
        ziel.LohnausweisBoxFFreierTransport = quelle.BoxF;
        ziel.LohnausweisBoxGKantineGratis = quelle.BoxG;
        ziel.LohnausweisPos21VerpflegungMonat = quelle.Pos21;
        ziel.LohnausweisZiffer14 = quelle.Ziffer14;
        ziel.LohnausweisExpatDatum = quelle.ExpatDatum;
        ziel.LohnausweisExpatKanton = quelle.ExpatKanton;
        ziel.LohnausweisWagenAbklaeren = quelle.WagenAbklaeren;
        ziel.LohnausweisWagenMinimal = quelle.WagenMinimal;
        ziel.LohnausweisSpesenDatum = quelle.SpesenDatum;
        ziel.LohnausweisSpesenKanton = quelle.SpesenKanton;
        ziel.LohnausweisRz52 = quelle.Rz52;
        ziel.LohnausweisWertDatum = quelle.WertDatum;
        ziel.LohnausweisWertKanton = quelle.WertKanton;
        ziel.LohnausweisBetGesperrt = quelle.BetGesperrt;
        ziel.LohnausweisBetNichtKotiert = quelle.BetNichtKotiert;
        ziel.LohnausweisBetAnwartschaft = quelle.BetAnwartschaft;
        ziel.LohnausweisBetUnecht = quelle.BetUnecht;
        ziel.LohnausweisKontaktOrg = quelle.KontaktOrg;
        ziel.LohnausweisKontaktStrasse = quelle.KontaktStrasse;
        ziel.LohnausweisKontaktPlz = quelle.KontaktPlz;
        ziel.LohnausweisKontaktOrt = quelle.KontaktOrt;
        ziel.LohnausweisKontaktPerson = quelle.KontaktPerson;
        ziel.LohnausweisKontaktTelefon = quelle.KontaktTelefon;
    }

    public static CompanyLohnausweis AusProfil(CompanyProfile firma, int ab) => new()
    {
        CompanyProfileId = firma.Id,
        GueltigAb = ab,
        BoxF = firma.LohnausweisBoxFFreierTransport,
        BoxG = firma.LohnausweisBoxGKantineGratis,
        Pos21 = firma.LohnausweisPos21VerpflegungMonat,
        Ziffer14 = firma.LohnausweisZiffer14,
        ExpatDatum = firma.LohnausweisExpatDatum,
        ExpatKanton = firma.LohnausweisExpatKanton,
        WagenAbklaeren = firma.LohnausweisWagenAbklaeren,
        WagenMinimal = firma.LohnausweisWagenMinimal,
        SpesenDatum = firma.LohnausweisSpesenDatum,
        SpesenKanton = firma.LohnausweisSpesenKanton,
        Rz52 = firma.LohnausweisRz52,
        WertDatum = firma.LohnausweisWertDatum,
        WertKanton = firma.LohnausweisWertKanton,
        BetGesperrt = firma.LohnausweisBetGesperrt,
        BetNichtKotiert = firma.LohnausweisBetNichtKotiert,
        BetAnwartschaft = firma.LohnausweisBetAnwartschaft,
        BetUnecht = firma.LohnausweisBetUnecht,
        KontaktOrg = firma.LohnausweisKontaktOrg,
        KontaktStrasse = firma.LohnausweisKontaktStrasse,
        KontaktPlz = firma.LohnausweisKontaktPlz,
        KontaktOrt = firma.LohnausweisKontaktOrt,
        KontaktPerson = firma.LohnausweisKontaktPerson,
        KontaktTelefon = firma.LohnausweisKontaktTelefon,
        GeaendertAm = DateTime.Now,
    };
}

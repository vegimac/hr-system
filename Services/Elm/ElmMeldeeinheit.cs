using HrSystem.Models;

namespace HrSystem.Services.Elm;

/// <summary>
/// Wer tritt in einer Lohnmeldung als Absender auf (Walter 05.11.2026)? Der Hauptsitz entscheidet:
/// «Je Filiale» = Name, UID und Nummern der Filiale, jede Filiale sendet für sich.
/// «Über den Hauptsitz» = Name und UID des Hauptsitzes, die Filialen sind Arbeitsorte.
/// Die Rechnung ist in beiden Fällen dieselbe.
/// </summary>
public static class ElmMeldeeinheit
{
    /// <param name="Filialen">Arbeitsorte der Einheit, ohne Filialwahl alle Filialen des Hauptsitzes.</param>
    /// <param name="Gewaehlt">Filialen, deren Lohn und Nummern in die Meldung gehören.</param>
    public sealed record Einheit(Hauptsitz Hauptsitz, List<CompanyProfile> Filialen, List<CompanyProfile> Gewaehlt, bool JeFiliale);

    /// <summary>Null = so darf keine Meldung entstehen; der Grund steht in <paramref name="warn"/>.</summary>
    public static Einheit? Bestimme(IReadOnlyList<CompanyProfile> alle, IReadOnlyList<Hauptsitz> hauptsitze, int? nurFiliale, List<string> warn)
    {
        if (nurFiliale is > 0)
        {
            var filiale = alle.FirstOrDefault(b => b.Id == nurFiliale);
            if (filiale == null)
            {
                warn.Add("Diese Filiale ist in der Meldung nicht enthalten.");
                return null;
            }
            var eigener = hauptsitze.FirstOrDefault(h => h.Id == filiale.HauptsitzId);
            if (eigener == null)
            {
                warn.Add($"Filiale «{filiale.FullDisplayName}» ist keinem Hauptsitz zugeordnet. Bitte in den Stammdaten der Filiale zuordnen.");
                return null;
            }
            var nur = new List<CompanyProfile> { filiale };
            return eigener.LohnmeldungJeFiliale
                ? new Einheit(eigener, nur, nur, true)
                : new Einheit(eigener, alle.Where(b => b.HauptsitzId == eigener.Id).ToList(), nur, false);
        }

        var ohne = alle.Where(b => hauptsitze.All(h => h.Id != b.HauptsitzId)).ToList();
        if (ohne.Count > 0)
        {
            warn.Add("Ohne Hauptsitz: " + string.Join(", ", ohne.Select(b => b.FullDisplayName)) + ". Bitte in den Stammdaten der Filiale zuordnen.");
            return null;
        }
        var ids = alle.Select(b => b.HauptsitzId).Distinct().ToList();
        if (ids.Count != 1)
        {
            warn.Add("Die Filialen gehören zu mehreren Hauptsitzen. Bitte oben links eine Filiale wählen.");
            return null;
        }
        var hs = hauptsitze.First(h => h.Id == ids[0]);
        if (hs.LohnmeldungJeFiliale)
        {
            warn.Add($"«{hs.Name}» meldet je Filiale. Bitte oben links eine Filiale wählen.");
            return null;
        }
        var filialen = alle.ToList();
        return new Einheit(hs, filialen, filialen, false);
    }

    /// <summary>
    /// Offene Lohnläufe, die eine echte Meldung sperren (Walter 05.11.2026): Monatsmeldung den Monat,
    /// Jahres- und Grenzgängermeldung das Jahr bis zum Meldemonat. EMA und voraussichtliche Jahresmeldung hängen nicht am Lohnlauf.
    /// </summary>
    public static List<string> OffeneLohnlaeufe(IEnumerable<PayrollPeriode> perioden, IReadOnlyList<CompanyProfile> filialen,
        string art, int jahr, int? monat)
    {
        if (art is "ema" or "prospective") return new();
        var von = art == "monthly" ? monat ?? 1 : 1;
        var bis = art is "monthly" or "annual" && monat is >= 1 and <= 12 ? monat.Value : 12;
        return perioden
            .Where(p => p.Year == jahr && p.Month >= von && p.Month <= bis && p.Status != "abgeschlossen"
                        && filialen.Any(f => f.Id == p.CompanyProfileId))
            .OrderBy(p => p.Month).ThenBy(p => p.CompanyProfileId)
            .Select(p => $"{p.Month:00}.{p.Year} {filialen.First(f => f.Id == p.CompanyProfileId).FullDisplayName}")
            .ToList();
    }

    /// <summary>Nach der ersten echten Meldung im Lohnjahr bleibt die Einstellung bis zum nächsten Jahr fix. Testmeldungen zählen nicht.</summary>
    public static bool EinstellungGesperrt(IEnumerable<ElmVorgang> vorgaenge, IReadOnlySet<int> filialIds, int jahr)
        => vorgaenge.Any(v => v.Art != "subscribe" && !v.TestCase && !ElmMeldungAnzeige.Zertifizierungstest(v) && v.Jahr == jahr
                              && (v.CompanyProfileId == null || filialIds.Contains(v.CompanyProfileId.Value)));
}

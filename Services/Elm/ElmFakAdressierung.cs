using System.Text.RegularExpressions;
using System.Xml.Linq;
using HrSystem.Models;
using static HrSystem.Services.Elm.ElmGemeinsam;

namespace HrSystem.Services.Elm;

/// <summary>
/// ELM 6.0 Nullmeldung FAK (Swissdec S_ELM_V6_01_01_05 / Direktive 8.2.4, Walter 17.10.2026):
/// Im Job stehen nur FAKs, die einem Arbeitsort der Rechtseinheit am Stichtag
/// zugeordnet sind — auch wenn dort niemand Lohn hat. Alte/abgelaufene und
/// nicht zugewiesene Kassen bleiben draussen. ProcessByDistributor=true für
/// diese Empfänger (Übermittlung ohne Personen nur FAK, Logik aber generisch).
/// </summary>
public static class ElmFakAdressierung
{
    public record Eintrag(
        string AddresseeId,
        string Kassennummer,
        string CustomerNumber,
        string Bezeichnung,
        IReadOnlyList<string> Kantone);

    /// <summary>
    /// FAKs aus dem Katalog, die an mind. einem Workplace (Filiale) der
    /// Rechtseinheit am Stichtag gültig zugeordnet sind.
    /// </summary>
    public static List<Eintrag> AusZuordnungen(
        IEnumerable<LohndatenEmpfaenger> katalog,
        IReadOnlySet<int> workplaceIds,
        DateOnly stichtag,
        Func<int, string?> kantonDerFiliale)
    {
        var map = new Dictionary<string, Eintrag>(StringComparer.OrdinalIgnoreCase);
        var byId = katalog.Where(x => x.Id != 0)
            .GroupBy(x => x.Id)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var e in katalog.Where(x => x.IsActive
                 && string.Equals(x.Art, "FAK", StringComparison.OrdinalIgnoreCase)))
        {
            var nr = NormKasse(e.Kassennummer);
            if (nr.Length == 0) continue;

            foreach (var z in e.Zuordnungen.Where(z => z.GiltAm(stichtag) && workplaceIds.Contains(z.CompanyProfileId)))
            {
                // Vereinbarung: diese Filiale geht an die Kasse des Hauptsitzes.
                // Der Kanton in der Meldung bleibt der Arbeitskanton.
                var ziel = Zielkasse(z, byId);
                var adressat = ziel ?? e;
                var adressNr = NormKasse(adressat.Kassennummer);
                if (adressNr.Length == 0) continue;

                var kunde = ziel == null
                    ? (z.Mitgliednummer ?? "").Trim()
                    : MitgliedDerKasse(adressat, stichtag, workplaceIds);
                if (kunde.Length == 0) kunde = adressNr;

                var kanton = (kantonDerFiliale(z.CompanyProfileId) ?? "").Trim().ToUpperInvariant();
                if (kanton.Length != 2 && ziel == null)
                    kanton = (e.KantonCode ?? "").Trim().ToUpperInvariant();

                Mergen(map, adressNr, kunde, adressat.Bezeichnung ?? adressNr, kanton);
            }
        }

        return map.Values
            .OrderBy(x => x.Kassennummer, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Kantonale Zielkasse der Vereinbarung, sonst keine Umleitung.</summary>
    static LohndatenEmpfaenger? Zielkasse(
        CompanyProfileEmpfaenger z, IReadOnlyDictionary<int, LohndatenEmpfaenger> byId)
    {
        if (!z.IkvAktiv) return null;
        LohndatenEmpfaenger? ziel = z.IkvEmpfaenger;
        if (ziel == null && z.IkvEmpfaengerId is int id && byId.TryGetValue(id, out var gefunden))
            ziel = gefunden;
        if (!FakIkv.IstKantonalFak(ziel)) return null;
        return NormKasse(ziel!.Kassennummer).Length == 0 ? null : ziel;
    }

    /// <summary>Mitgliednummer der Zielkasse an einer Filiale derselben Meldung.</summary>
    static string MitgliedDerKasse(LohndatenEmpfaenger kasse, DateOnly stichtag, IReadOnlySet<int> workplaceIds)
    {
        return kasse.Zuordnungen
            .Where(z => z.GiltAm(stichtag) && workplaceIds.Contains(z.CompanyProfileId))
            .Select(z => (z.Mitgliednummer ?? "").Trim())
            .FirstOrDefault(v => v.Length > 0) ?? "";
    }

    static void Mergen(Dictionary<string, Eintrag> map, string nr, string kunde, string bezeichnung, string kanton)
    {
        var kt = kanton.Length == 2 ? new List<string> { kanton } : new List<string>();
        if (!map.TryGetValue(nr, out var alt))
        {
            map[nr] = new Eintrag(AddresseeIdFuer(nr), nr, kunde, bezeichnung, kt);
            return;
        }
        var kantone = alt.Kantone.Concat(kt).Distinct().OrderBy(k => k).ToList();
        var kundeNeu = alt.CustomerNumber.Length > 0 ? alt.CustomerNumber : kunde;
        map[nr] = alt with { Kantone = kantone, CustomerNumber = kundeNeu };
    }

    public static string AddresseeIdFuer(string kassennummer)
    {
        var n = NormKasse(kassennummer);
        // XML-ID: Buchstaben/Ziffern/Punkt/Unterstrich
        var safe = Regex.Replace(n, @"[^A-Za-z0-9._-]", "_");
        return "#FAK-" + (safe.Length > 0 ? safe : "X");
    }

    public static string NormKasse(string? nr)
    {
        if (string.IsNullOrWhiteSpace(nr)) return "";
        return Regex.Replace(nr.Trim().Replace('\u00a0', ' '), @"\s+", "").ToUpperInvariant();
    }

    public static XElement JobAddressee(Eintrag e) =>
        new(Sdc + "Addressee",
            new XAttribute("addresseeID", e.AddresseeId),
            new XElement(Ep + "AddresseeIdentification", e.Kassennummer),
            new XElement(Ep + "ProcessByDistributor", "true"));

    public static XElement Institution(Eintrag e) =>
        new(Sd + "FAK-CAF",
            new XAttribute("addresseeIDRef", e.AddresseeId),
            new XElement(Sd + "FAK-CAF-CustomerNumber", e.CustomerNumber));

    /// <summary>Nullmeldung: Totals 0.00 je Kanton der zugewiesenen Arbeitsorte.</summary>
    public static XElement TotalsNull(Eintrag e)
    {
        var kantone = e.Kantone.Count > 0 ? e.Kantone : new[] { "EX" };
        return new XElement(Sd + "FAK-CAF-Totals",
            new XAttribute("addresseeIDRef", e.AddresseeId),
            kantone.Select(kt => new XElement(Sd + "Total-FAK-CAF-PerCanton",
                new XElement(Sd + "Total-FAK-CAF-ContributorySalary", "0.00"),
                new XElement(Sd + "Total-FAK-CAF-FamilyIncomeSupplementRepetitive", "0.00"),
                new XElement(Sd + "Total-FAK-CAF-FamilyIncomeSupplementSingular", "0.00"),
                new XElement(Sd + "Canton", kt))));
    }
}

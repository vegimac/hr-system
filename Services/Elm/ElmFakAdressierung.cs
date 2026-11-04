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
                var k = Kasse(e, z, byId, stichtag, workplaceIds, kantonDerFiliale);
                if (k != null) Mergen(map, k.Value.Nr, k.Value.Kunde, k.Value.Bezeichnung, k.Value.Kanton);
            }
        }

        return map.Values
            .OrderBy(x => x.Kassennummer, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Kasse einer Zuordnung: Vereinbarung (IKV) ⇒ die Kasse des Hauptsitzes, der Kanton in der
    /// Meldung bleibt der Arbeitskanton. Null = keine Kassennummer.
    /// </summary>
    static (string Nr, string Kunde, string Bezeichnung, string Kanton)? Kasse(
        LohndatenEmpfaenger e, CompanyProfileEmpfaenger z, IReadOnlyDictionary<int, LohndatenEmpfaenger> byId,
        DateOnly stichtag, IReadOnlySet<int> workplaceIds, Func<int, string?> kantonDerFiliale)
    {
        var ziel = Zielkasse(z, byId);
        var adressat = ziel ?? e;
        var adressNr = NormKasse(adressat.Kassennummer);
        if (adressNr.Length == 0) return null;

        var kunde = ziel == null
            ? (z.Mitgliednummer ?? "").Trim()
            : MitgliedDerKasse(adressat, stichtag, workplaceIds);
        if (kunde.Length == 0) kunde = adressNr;

        var kanton = (kantonDerFiliale(z.CompanyProfileId) ?? "").Trim().ToUpperInvariant();
        if (kanton.Length != 2 && ziel == null)
            kanton = (e.KantonCode ?? "").Trim().ToUpperInvariant();
        return (adressNr, kunde, adressat.Bezeichnung ?? adressNr, kanton);
    }

    /// <summary>
    /// FAK einer Filiale an einem Tag (FAK-Jahresmeldung pro Person, Walter 01.11.2026):
    /// dieselbe Regel wie die Adressierung, nur für eine Filiale. Null = keine gültige Zuordnung.
    /// </summary>
    public static Eintrag? FuerFiliale(
        IEnumerable<LohndatenEmpfaenger> katalog,
        int filialeId,
        DateOnly stichtag,
        IReadOnlySet<int> workplaceIds,
        Func<int, string?> kantonDerFiliale)
    {
        var liste = katalog.ToList();
        var byId = liste.Where(x => x.Id != 0).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var e in liste.Where(x => x.IsActive && string.Equals(x.Art, "FAK", StringComparison.OrdinalIgnoreCase)))
        {
            if (NormKasse(e.Kassennummer).Length == 0) continue;
            foreach (var z in e.Zuordnungen.Where(z => z.CompanyProfileId == filialeId && z.GiltAm(stichtag)))
            {
                var k = Kasse(e, z, byId, stichtag, workplaceIds, kantonDerFiliale);
                if (k == null) continue;
                var kt = k.Value.Kanton.Length == 2 ? new List<string> { k.Value.Kanton } : new List<string>();
                return new Eintrag(AddresseeIdFuer(k.Value.Nr), k.Value.Nr, k.Value.Kunde, k.Value.Bezeichnung, kt);
            }
        }
        return null;
    }

    /// <summary>Kassen der Meldung um die ergänzen, die nur bei Personen vorkommen (Zuordnung im Jahr beendet).</summary>
    public static List<Eintrag> Ergaenzt(IEnumerable<Eintrag> adressiert, IEnumerable<Eintrag> beiPersonen)
    {
        var map = adressiert.ToDictionary(x => x.Kassennummer, StringComparer.OrdinalIgnoreCase);
        foreach (var p in beiPersonen)
        {
            if (map.TryGetValue(p.Kassennummer, out var alt))
                map[p.Kassennummer] = alt with { Kantone = alt.Kantone.Concat(p.Kantone).Distinct().OrderBy(k => k).ToList() };
            else
                map[p.Kassennummer] = p;
        }
        return map.Values.OrderBy(x => x.Kassennummer, StringComparer.OrdinalIgnoreCase).ToList();
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

    /// <summary>
    /// Kassennummer, wie sie an den Distributor geht: immer NNN.NNN (Referenz: 003.000).
    /// Der Katalog darf «003» führen. Anderes (L1200 beim BVG) bleibt, wie es ist. Ein erster Block 000 ist
    /// ungültig und wird nicht schön gemacht: Der Katalog lehnt ihn ab (AusgleichskasseNummer).
    /// </summary>
    public static string KasseFuerMeldung(string? nr)
    {
        var n = (nr ?? "").Trim();
        return Regex.IsMatch(n, @"^\d{3}$") && n != "000" ? n + ".000" : n;
    }

    public static XElement JobAddressee(Eintrag e) =>
        new(Sdc + "Addressee",
            new XAttribute("addresseeID", e.AddresseeId),
            new XElement(Ep + "AddresseeIdentification", KasseFuerMeldung(e.Kassennummer)),
            new XElement(Ep + "ProcessByDistributor", "true"));

    public static XElement Institution(Eintrag e) =>
        new(Sd + "FAK-CAF",
            new XAttribute("addresseeIDRef", e.AddresseeId),
            new XElement(Sd + "FAK-CAF-CustomerNumber", e.CustomerNumber));

    /// <summary>Nullmeldung: Totals 0.00 je Kanton der zugewiesenen Arbeitsorte.</summary>
    public static XElement TotalsNull(Eintrag e) => Totals(e, new Dictionary<string, Summe>());

    public readonly record struct Summe(decimal Lohn, decimal Wiederkehrend, decimal Einmalig);

    /// <summary>
    /// Totale je Kanton: Summe der gemeldeten FAK-Zeilen dieser Kasse. Kantone der zugewiesenen
    /// Arbeitsorte ohne Personen stehen mit 0.00 (Nullmeldung, S_ELM_V6_01_01_05).
    /// </summary>
    public static XElement Totals(Eintrag e, IReadOnlyDictionary<string, Summe> jeKanton)
    {
        // Kasse mit Personen: nur deren Kantone (Quality Tool 2024-11). Nullmeldung: Kantone der Zuordnung.
        var kantone = (jeKanton.Count > 0 ? jeKanton.Keys : e.Kantone.AsEnumerable()).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        if (kantone.Count == 0) kantone.Add("EX");
        return new XElement(Sd + "FAK-CAF-Totals",
            new XAttribute("addresseeIDRef", e.AddresseeId),
            kantone.Select(kt =>
            {
                var s = jeKanton.TryGetValue(kt, out var v) ? v : default;
                return new XElement(Sd + "Total-FAK-CAF-PerCanton",
                    new XElement(Sd + "Total-FAK-CAF-ContributorySalary", Amt(s.Lohn)),
                    new XElement(Sd + "Total-FAK-CAF-FamilyIncomeSupplementRepetitive", Amt(s.Wiederkehrend)),
                    new XElement(Sd + "Total-FAK-CAF-FamilyIncomeSupplementSingular", Amt(s.Einmalig)),
                    new XElement(Sd + "Canton", kt));
            }));
    }
}

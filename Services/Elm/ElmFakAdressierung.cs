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

        foreach (var e in katalog.Where(x => x.IsActive
                 && string.Equals(x.Art, "FAK", StringComparison.OrdinalIgnoreCase)))
        {
            var nr = NormKasse(e.Kassennummer);
            if (nr.Length == 0) continue;

            var gilt = e.Zuordnungen
                .Where(z => z.GiltAm(stichtag) && workplaceIds.Contains(z.CompanyProfileId))
                .ToList();
            if (gilt.Count == 0) continue;

            var kunde = gilt.Select(z => (z.Mitgliednummer ?? "").Trim())
                .FirstOrDefault(v => v.Length > 0) ?? "";
            if (kunde.Length == 0) kunde = nr;

            var kantone = gilt
                .Select(z => (kantonDerFiliale(z.CompanyProfileId) ?? e.KantonCode ?? "").Trim().ToUpperInvariant())
                .Where(k => k.Length == 2)
                .Distinct()
                .OrderBy(k => k)
                .ToList();
            if (kantone.Count == 0 && !string.IsNullOrWhiteSpace(e.KantonCode))
                kantone.Add(e.KantonCode.Trim().ToUpperInvariant());

            if (!map.TryGetValue(nr, out var alt))
            {
                map[nr] = new Eintrag(AddresseeIdFuer(nr), nr, kunde, e.Bezeichnung ?? nr, kantone);
            }
            else
            {
                // Gleiche Kassen-Nr. (z.B. Spida an mehreren Filialen): Kantone mergen
                var kt = alt.Kantone.Concat(kantone).Distinct().OrderBy(k => k).ToList();
                var kundeNeu = alt.CustomerNumber.Length > 0 ? alt.CustomerNumber : kunde;
                map[nr] = alt with { Kantone = kt, CustomerNumber = kundeNeu };
            }
        }

        return map.Values
            .OrderBy(x => x.Kassennummer, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

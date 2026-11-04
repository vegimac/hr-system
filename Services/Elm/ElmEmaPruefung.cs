using System.Xml.Linq;

namespace HrSystem.Services.Elm;

/// <summary>
/// Prüft, was der Feldvergleich nicht sieht: Attribute. Unsere Kennungen heissen anders als in der Referenz
/// (#AHV statt #AHV_KK, #w12 statt #work_TF12_0), darum zählt, worauf sie zeigen:
/// addresseeIDRef auf die Kassennummer im Job, workIDRef auf das Eintrittsdatum der Work, dazu oldCode.
/// </summary>
public static class ElmEmaPruefung
{
    public static List<string> Verweise(string istXml, string sollXml)
    {
        var ist = Aufgeloest(XDocument.Parse(istXml).Root!);
        var soll = Aufgeloest(XDocument.Parse(sollXml).Root!);
        var res = new List<string>();
        foreach (var nr in ist.Keys.Union(soll.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            var a = ist.GetValueOrDefault(nr) ?? new();
            var b = soll.GetValueOrDefault(nr) ?? new();
            for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
            {
                var x = i < a.Count ? a[i] : "fehlt";
                var y = i < b.Count ? b[i] : "fehlt";
                if (x != y) res.Add($"Nr. {nr}: {x} statt {y}");
            }
        }
        return res;
    }

    private static Dictionary<string, List<string>> Aufgeloest(XElement root)
    {
        var job = root.Descendants().Where(e => e.Name.LocalName == "Addressee" && e.Attribute("addresseeID") != null)
            .GroupBy(e => e.Attribute("addresseeID")!.Value)
            .ToDictionary(g => g.Key, g => g.First().Elements().FirstOrDefault(k => k.Name.LocalName == "AddresseeIdentification")?.Value.Trim() ?? "");
        var res = new Dictionary<string, List<string>>();
        foreach (var p in root.Descendants().Where(e => e.Name.LocalName == "Person"))
        {
            var nr = p.Descendants().FirstOrDefault(e => e.Name.LocalName == "EmployeeNumber")?.Value.Trim() ?? "?";
            var works = p.Elements().Where(e => e.Name.LocalName == "Work" && e.Attribute("workID") != null)
                .ToDictionary(e => e.Attribute("workID")!.Value, e => e.Elements().FirstOrDefault(k => k.Name.LocalName == "EntryDate")?.Value.Trim() ?? "");
            var zeilen = new List<string>();
            foreach (var n in p.Descendants().Where(e => e.Attribute("addresseeIDRef") != null))
            {
                var kasse = job.GetValueOrDefault(n.Attribute("addresseeIDRef")!.Value) ?? "kein Adressat";
                var w = n.Attribute("workIDRef")?.Value;
                var eintritt = w == null ? "" : " Work " + (works.GetValueOrDefault(w) ?? "unbekannt");
                var alt = n.Elements().FirstOrDefault(k => k.Attribute("oldCode") != null)?.Attribute("oldCode")!.Value;
                zeilen.Add($"{n.Name.LocalName} an {kasse}{eintritt}{(alt == null ? "" : " oldCode " + alt)}");
            }
            res[nr] = zeilen;
        }
        return res;
    }
}

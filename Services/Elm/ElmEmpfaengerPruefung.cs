using System.Xml.Linq;
using HrSystem.Models;

namespace HrSystem.Services.Elm;

/// <summary>
/// Letzte Prüfung vor dem Senden (Walter 05.11.2026): Jeder Empfänger in der Meldung muss aus den
/// Lohndaten-Empfängern stammen und bei den gewählten Filialen im Meldezeitraum zugeordnet sein,
/// jede Nummer muss dort hinterlegt und eindeutig sein. Sonst Warnung und kein Senden.
/// Lohnausweis und Statistik haben keine Nummer der Filiale und bleiben aussen vor.
/// </summary>
public static class ElmEmpfaengerPruefung
{
    private static readonly Dictionary<string, string[]> Arten = new()
    {
        ["AHV-AVS"] = new[] { "AUSGLEICHSKASSE" },
        ["FAK-CAF"] = new[] { "FAK" },
        ["UVG-LAA"] = new[] { "UVG" },
        ["UVGZ-LAAC"] = new[] { "UVGZ", "UVG" },
        ["KTG-AMC"] = new[] { "KTG" },
        ["BVG-LPP"] = new[] { "BVG" },
    };

    /// <summary>UVG und UVGZ können denselben Versicherer mit eigenen Nummern haben; UVGZ ist auch ein UVG-Eintrag mit «Zusatz».</summary>
    private static bool Passt(LohndatenEmpfaenger x, string domaene, string[] arten)
    {
        var zusatz = (x.Bezeichnung + " " + x.Zusatz).Contains("zusatz", StringComparison.OrdinalIgnoreCase);
        return domaene switch
        {
            "UVGZ-LAAC" => x.Art == "UVGZ" || (x.Art == "UVG" && zusatz),
            "UVG-LAA" => x.Art == "UVG" && !zusatz,
            _ => arten.Contains(x.Art),
        };
    }

    private static bool Gilt(CompanyProfileEmpfaenger z, DateOnly von, DateOnly bis)
        => z.IsActive && (z.GueltigAb == null || z.GueltigAb <= bis) && (z.GueltigBis == null || z.GueltigBis >= von);

    /// <summary>
    /// SSL-Nummer eines Kantons. Je Filiale: nur die Nummer bei der Filiale im Zeitraum.
    /// Über den Hauptsitz: die zentrale Nummer des Empfängers, sonst die Nummer der Zuordnung.
    /// </summary>
    public static string SslNummer(IEnumerable<LohndatenEmpfaenger> kassen, string kanton, IReadOnlySet<int> filialIds,
        DateOnly von, DateOnly bis, bool jeFiliale)
    {
        var imKanton = kassen.Where(x => x.IsActive && string.Equals((x.KantonCode ?? "").Trim(), kanton, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!jeFiliale)
        {
            var zentral = imKanton.Select(x => (x.Kassennummer ?? "").Trim()).Where(n => n.Length > 0).Distinct().ToList();
            var nummer = zentral.FirstOrDefault(n => n.Any(char.IsDigit)) ?? zentral.FirstOrDefault();
            if (nummer != null) return nummer;
        }
        var zugeordnet = imKanton.SelectMany(x => x.Zuordnungen)
            .Where(z => filialIds.Contains(z.CompanyProfileId) && Gilt(z, von, bis))
            .Select(z => (z.Mitgliednummer ?? "").Trim()).Where(n => n.Length > 0).Distinct().ToList();
        return zugeordnet.Count == 1 ? zugeordnet[0] : "";
    }

    /// <summary>Anmeldung nur für einen Empfänger, der bei diesen Filialen mit Kunden- und Vertragsnummer hinterlegt ist.</summary>
    public static string? PruefeAnmeldung(IEnumerable<LohndatenEmpfaenger> katalog, string? domain, string? kennung,
        string? kunde, string? vertrag, IReadOnlySet<int> filialIds, DateOnly heute)
    {
        var d = (domain ?? "").Trim();
        if (!Arten.TryGetValue(d, out var arten)) return "Für diesen Standard gibt es keine Anmeldung.";
        var norm = ElmFakAdressierung.NormKasse(ElmFakAdressierung.KasseFuerMeldung(kennung));
        if (norm.Length == 0) return "Die Kennung des Empfängers fehlt.";
        var zuordnungen = katalog.Where(x => x.IsActive && Passt(x, d, arten)
                && ElmFakAdressierung.NormKasse(ElmFakAdressierung.KasseFuerMeldung(x.Kassennummer)) == norm)
            .SelectMany(x => x.Zuordnungen).Where(z => filialIds.Contains(z.CompanyProfileId) && Gilt(z, heute, heute)).ToList();
        if (zuordnungen.Count == 0) return $"Empfänger {kennung} ist dieser Filiale nicht zugeordnet.";
        var k = (kunde ?? "").Trim();
        var v = (vertrag ?? "").Trim();
        return zuordnungen.Any(z => (z.Mitgliednummer ?? "").Trim() == k && k.Length > 0 && (z.Subnummer ?? "").Trim() == v && v.Length > 0)
            ? null : $"Kunden- und Vertragsnummer von Empfänger {kennung} sind bei dieser Filiale nicht so hinterlegt.";
    }

    public static List<string> Pruefe(XElement meldung, IReadOnlyList<LohndatenEmpfaenger> katalog, IReadOnlySet<int> filialIds,
        DateOnly von, DateOnly bis, bool jeFiliale)
    {
        var fehler = new List<string>();
        var institutionen = meldung.Descendants().Where(e => e.Name.LocalName == "Institutions")
            .SelectMany(i => i.Elements()).ToList();
        var adressaten = meldung.Descendants().Where(e => e.Name.LocalName == "Addressee").ToList();
        if (adressaten.Count == 0) fehler.Add("Die Meldung hat keinen Empfänger.");

        foreach (var a in adressaten)
        {
            var id = (string?)a.Attribute("addresseeID") ?? "";
            var kennung = a.Elements().FirstOrDefault(e => e.Name.LocalName == "AddresseeIdentification")?.Value.Trim() ?? "";
            var inst = institutionen.FirstOrDefault(i => (string?)i.Attribute("addresseeIDRef") == id);
            var domaene = inst?.Name.LocalName;
            if (domaene is "Tax" or "Statistic" || kennung == "Statistic" || id.StartsWith("#TAX-", StringComparison.Ordinal)) continue;

            if (domaene is "TaxAtSource" or "TaxCrossborder")
            {
                var kanton = id.StartsWith("#QST-", StringComparison.Ordinal) ? id[5..] : kennung;
                var erwartet = SslNummer(katalog.Where(x => x.Art == "QST"), kanton, filialIds, von, bis, jeFiliale);
                if (erwartet.Length == 0)
                    fehler.Add($"Quellensteuer {kanton}: keine eindeutige SSL-Nummer bei den Lohndaten-Empfängern dieser Filiale.");
                else if (Feld(inst!, "CustomerIdentity") != erwartet)
                    fehler.Add($"Quellensteuer {kanton}: Die Nummer in der Meldung ist nicht die hinterlegte.");
                continue;
            }

            if (domaene == "AHV-AVS")
                foreach (var (element, art, pflicht) in new[] { ("UVG-LAA-Insurance", "UVG", true), ("BVG-LPP-Insurance", "BVG", false) })
                {
                    var block = inst!.Elements().FirstOrDefault(e => e.Name.LocalName == element);
                    if (block == null || block.Elements().All(e => e.Name.LocalName != "NoneWithReason")) continue;
                    var zugeordnet = katalog.Any(x => x.IsActive && x.Art == art
                        && x.Zuordnungen.Any(z => filialIds.Contains(z.CompanyProfileId) && Gilt(z, von, bis)));
                    if (zugeordnet)
                        fehler.Add($"{art}: Beim Versicherer fehlen in der Meldung Name, UID oder «versichert seit».");
                    else if (pflicht)
                        fehler.Add($"{art}: Keine Versicherung bei dieser Filiale zugeordnet.");
                }

            if (domaene == null || !Arten.TryGetValue(domaene, out var arten))
            {
                fehler.Add($"Empfänger «{(kennung.Length > 0 ? kennung : id)}»: Bei den Lohndaten-Empfängern nicht bestimmbar.");
                continue;
            }
            var name = domaene.Split('-')[0];
            if (kennung.Length == 0)
            {
                fehler.Add($"{name}: Kein Lohndaten-Empfänger zugeordnet. Bitte bei der Filiale einrichten.");
                continue;
            }
            var norm = ElmFakAdressierung.NormKasse(ElmFakAdressierung.KasseFuerMeldung(kennung));
            var empfaenger = katalog.Where(x => x.IsActive && Passt(x, domaene, arten)
                    && ElmFakAdressierung.NormKasse(ElmFakAdressierung.KasseFuerMeldung(x.Kassennummer)) == norm).ToList();
            if (empfaenger.Count == 0)
            {
                fehler.Add($"{name}: Empfänger {kennung} steht nicht bei den Lohndaten-Empfängern.");
                continue;
            }
            var zuordnungen = empfaenger.SelectMany(x => x.Zuordnungen)
                .Where(z => filialIds.Contains(z.CompanyProfileId) && Gilt(z, von, bis)).ToList();
            if (zuordnungen.Count == 0)
            {
                fehler.Add($"{name}: Empfänger {kennung} ist dieser Filiale im Meldezeitraum nicht zugeordnet.");
                continue;
            }

            var kunde = domaene switch
            {
                "AHV-AVS" => Feld(inst!, "AK-CC-CustomerNumber"),
                "FAK-CAF" => Feld(inst!, "FAK-CAF-CustomerNumber"),
                _ => Feld(inst!, "CustomerIdentity"),
            };
            var mitglieder = zuordnungen.Select(z => (z.Mitgliednummer ?? "").Trim()).Where(n => n.Length > 0).ToHashSet();
            if (!mitglieder.Contains(kunde))
                fehler.Add($"{name}: Keine hinterlegte Mitglied- oder Kundennummer für Empfänger {kennung}.");
            else if (mitglieder.Count > 1 && filialIds.Count > 1)
                fehler.Add($"{name}: Mehrere Nummern bei Empfänger {kennung}. In einer gemeinsamen Meldung gilt je Empfänger nur eine.");

            if (domaene is not ("AHV-AVS" or "FAK-CAF"))
            {
                var vertrag = Feld(inst!, "ContractIdentity");
                if (!zuordnungen.Any(z => (z.Subnummer ?? "").Trim() == vertrag && vertrag.Length > 0))
                    fehler.Add($"{name}: Keine hinterlegte Vertragsnummer für Empfänger {kennung}.");
            }
        }
        return fehler.Distinct().ToList();
    }

    private static string Feld(XElement inst, string name)
        => inst.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? "";
}

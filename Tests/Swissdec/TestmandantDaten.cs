using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HrSystem.Tests.Swissdec;

/// <summary>
/// Liest die Swissdec-Testdaten der Muster AG aus dem Repo — ohne Datenbank,
/// ohne Testinstanz (Walter 26.09.2026 «kannst du das nicht selber nachrechnen»).
///   • SWISSCEC/Testmandant/company_export.csv      → Sätze, Lohnbänder, Höchstlöhne
///   • SWISSCEC/Testmandant/testcases_export.csv    → Personenstammdaten (JSON-Spalte)
///   • SWISSCEC/Testmandant/testcase_differences_export.csv → Mutationen je Monat
///   • SWISSCEC/Testmandant/wagetypes_export.csv    → Lohnarten je Testfall und Monat
///   • Assets/Swissdec/SwissdecLohnarten.json       → SV-Pflichten je Lohnart
///   • SWISSCEC/RefXML/RefXML_*_{RETROSPECTIVE,MONTHLY}.xml → Swissdec-Soll
/// Alles rein lesend.
/// </summary>
public static class TestmandantDaten
{
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SWISSCEC", "Testmandant", "company_export.csv")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("SWISSCEC/Testmandant/company_export.csv nicht gefunden.");
        }
    }

    private static string Pfad(params string[] teile) => Path.Combine(new[] { RepoRoot }.Concat(teile).ToArray());

    private static decimal? Dez(string? s)
        => decimal.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    // ── CSV-Zeilen mit Anführungszeichen und Zeilenumbrüchen in Feldern ──────
    public static List<string[]> LiesCsv(string pfad)
    {
        var zeilen = new List<string[]>();
        var feld = new System.Text.StringBuilder();
        var aktuell = new List<string>();
        bool inQuote = false;
        foreach (char c in File.ReadAllText(pfad))
        {
            if (inQuote)
            {
                if (c == '"') inQuote = false;
                else feld.Append(c);
            }
            else if (c == '"') inQuote = true;
            else if (c == ',') { aktuell.Add(feld.ToString()); feld.Clear(); }
            else if (c == '\n')
            {
                aktuell.Add(feld.ToString().TrimEnd('\r')); feld.Clear();
                zeilen.Add(aktuell.ToArray()); aktuell = new List<string>();
            }
            else feld.Append(c);
        }
        if (feld.Length > 0 || aktuell.Count > 0)
        {
            aktuell.Add(feld.ToString().TrimEnd('\r'));
            zeilen.Add(aktuell.ToArray());
        }
        return zeilen;
    }

    // ── Firmenstammdaten (Sätze/Bänder) ─────────────────────────────────────
    /// <summary>Lösung einer Versicherung: Lohnband und Sätze Mann/Frau.</summary>
    public readonly record struct Loesung(string Code, decimal VonJahr, decimal BisJahr, decimal SatzMann, decimal SatzFrau)
    {
        public decimal VonMonat => VonJahr / 12m;
        public decimal BisMonat => BisJahr / 12m;
        public decimal Satz(string? geschlecht)
            => (geschlecht ?? "").StartsWith("F", StringComparison.OrdinalIgnoreCase) ? SatzFrau : SatzMann;
    }

    public sealed class Firmensaetze
    {
        public decimal AhvSatz { get; init; }
        public decimal AlvSatz { get; init; }
        public decimal AlvzSatz { get; init; }
        public decimal AlvLimit { get; init; }
        public decimal AlvzLimit { get; init; }
        public decimal AhvFreibetragJahr { get; init; }
        public decimal UvgLimit { get; init; }
        /// <summary>NBU-Satz je Betriebsteil (A, B, P …).</summary>
        public Dictionary<string, decimal> NbuSatz { get; init; } = new();
        public Dictionary<string, Loesung> Uvgz { get; init; } = new();
        public Dictionary<string, Loesung> Ktg { get; init; } = new();
    }

    public static Firmensaetze LadeFirmensaetze(int jahr)
    {
        var zeilen = LiesCsv(Pfad("SWISSCEC", "Testmandant", "company_export.csv"));
        var kopf = zeilen[0];
        int spalte = Array.FindIndex(kopf, h => h.Trim() == jahr.ToString());
        if (spalte < 0) throw new InvalidOperationException($"Jahr {jahr} fehlt in company_export.csv.");

        var wert = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var z in zeilen.Skip(1))
            if (z.Length > spalte && !string.IsNullOrWhiteSpace(z[0]))
                wert[z[0].Trim()] = z[spalte].Trim();

        decimal W(string k) => Dez(wert.TryGetValue(k, out var v) ? v : null) ?? 0m;

        var nbu = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in wert.Keys.Where(k => k.StartsWith("CompanyUVGLAANBUVAANPRate", StringComparison.OrdinalIgnoreCase)))
        {
            var teil = k["CompanyUVGLAANBUVAANPRate".Length..];
            if (Dez(wert[k]) is { } satz && satz > 0) nbu[teil] = satz * 100m;   // Datei führt Faktoren
        }

        Dictionary<string, Loesung> Loesungen(string praefix)
        {
            var d = new Dictionary<string, Loesung>(StringComparer.OrdinalIgnoreCase);
            foreach (var code in new[] { "10", "11", "12" })
            {
                if (!wert.ContainsKey($"{praefix}Code{code}")) continue;
                d[code] = new Loesung(
                    code,
                    Dez(wert.GetValueOrDefault($"{praefix}Limit{code}From")) ?? 0m,
                    Dez(wert.GetValueOrDefault($"{praefix}Limit{code}Until")) ?? 0m,
                    (Dez(wert.GetValueOrDefault($"{praefix}RateMale{code}")) ?? 0m) * 100m,
                    (Dez(wert.GetValueOrDefault($"{praefix}RateFemale{code}")) ?? 0m) * 100m);
            }
            return d;
        }

        return new Firmensaetze
        {
            AhvSatz           = W("CompanyAHVAVSEmployeeContributions") * 100m,
            AlvSatz           = W("CompanyALVACEmployeeContributions") * 100m,
            AlvzSatz          = W("CompanyALVZACSEmployeeContributions") * 100m,
            AlvLimit          = W("CompanyALVACLimit"),
            AlvzLimit         = W("CompanyALVZACSLimit"),
            AhvFreibetragJahr = W("CompanyAHVAVSExemptionLimit"),
            UvgLimit          = W("CompanyUVGLAALimit"),
            NbuSatz           = nbu,
            Uvgz              = Loesungen("CompanyUVGZLAAC"),
            Ktg               = Loesungen("CompanyKTGAMC"),
        };
    }

    // ── Personen + Mutationen ───────────────────────────────────────────────
    /// <summary>Personenstand zu einem Monat (Stammdaten + alle Mutationen bis dahin).</summary>
    public sealed class Person
    {
        public string Tf { get; init; } = "";
        public string Name { get; init; } = "";
        public Dictionary<string, string> Werte { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? W(string k) => Werte.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
        public decimal? D(string k) => Dez(W(k));
        public DateOnly? Datum(string k) => DatumVon(W(k));
    }

    public static DateOnly? DatumVon(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Regex.Match(s, @"^(\d{2})\.(\d{2})\.(\d{4})");
        if (m.Success) return new DateOnly(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value));
        m = Regex.Match(s, @"^(\d{4})-(\d{2})-(\d{2})");
        if (m.Success) return new DateOnly(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        return null;
    }

    /// <summary>Stammdaten je Testfall (Stand Eintritt), JSON-Spalte ist doppelt quotiert.</summary>
    public static Dictionary<string, Person> LadePersonen()
    {
        var txt = File.ReadAllText(Pfad("SWISSCEC", "Testmandant", "testcases_export.csv"));
        var res = new Dictionary<string, Person>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in txt.Split("\nTF").Skip(1))
        {
            var b = "TF" + block;
            var tf = b[..4];
            var p = new Person { Tf = tf, Name = b.Split(',')[0][5..].Trim() };
            foreach (Match m in Regex.Matches(b, @"""?""(Person\w+)""?"":""?""([^""]*)""?"""))
                p.Werte[m.Groups[1].Value] = m.Groups[2].Value;
            res[tf] = p;
        }
        return res;
    }

    /// <summary>Mutationen: tf → Monat («2025-06») → Tag → neuer Wert.</summary>
    public static Dictionary<string, SortedDictionary<string, Dictionary<string, string>>> LadeMutationen()
    {
        var zeilen = LiesCsv(Pfad("SWISSCEC", "Testmandant", "testcase_differences_export.csv"));
        var kopf = zeilen[0].Select(h => h.Trim()).ToArray();
        int iFall = Array.IndexOf(kopf, "testcase"), iTag = Array.IndexOf(kopf, "sscTag"),
            iMon = Array.IndexOf(kopf, "month"), iNeu = Array.IndexOf(kopf, "newValue");
        var res = new Dictionary<string, SortedDictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var z in zeilen.Skip(1))
        {
            if (z.Length <= Math.Max(iNeu, iMon) || z[iFall].Length < 4) continue;
            var tf = z[iFall][..4];
            var monat = z[iMon].Length >= 7 ? z[iMon][..7] : z[iMon];
            if (!res.TryGetValue(tf, out var proMonat)) res[tf] = proMonat = new SortedDictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            if (!proMonat.TryGetValue(monat, out var tags)) proMonat[monat] = tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            tags[z[iTag].Trim()] = z[iNeu];
        }
        return res;
    }

    /// <summary>Personenstand per Monat (Stammdaten + Mutationen bis inkl. diesem Monat).</summary>
    public static Person StandPer(Person stamm,
        SortedDictionary<string, Dictionary<string, string>>? mutationen, string monat)
    {
        var p = new Person { Tf = stamm.Tf, Name = stamm.Name };
        foreach (var kv in stamm.Werte) p.Werte[kv.Key] = kv.Value;
        if (mutationen != null)
            foreach (var m in mutationen.Where(m => string.CompareOrdinal(m.Key, monat) <= 0))
                foreach (var kv in m.Value) p.Werte[kv.Key] = kv.Value;
        return p;
    }

    // ── Lohnarten je Testfall und Monat ─────────────────────────────────────
    /// <summary>tf → «2025-08» → Lohnart-Code → Betrag.</summary>
    public static Dictionary<string, SortedDictionary<string, Dictionary<string, decimal>>> LadeLohnarten()
    {
        var zeilen = LiesCsv(Pfad("SWISSCEC", "Testmandant", "wagetypes_export.csv"));
        var kopf = zeilen[0].Select(h => h.Trim()).ToArray();
        var monate = kopf.Select((h, i) => (h, i)).Where(x => Regex.IsMatch(x.h, @"^\d{4}-\d{2}-01$")).ToList();
        var res = new Dictionary<string, SortedDictionary<string, Dictionary<string, decimal>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var z in zeilen.Skip(1))
        {
            if (z.Length < kopf.Length || string.IsNullOrWhiteSpace(z[0])) continue;
            var tf = z[0].Trim();
            var code = z[1].Trim();
            foreach (var (h, i) in monate)
            {
                if (Dez(z[i]) is not { } betrag || betrag == 0m) continue;
                if (!res.TryGetValue(tf, out var proMonat)) res[tf] = proMonat = new SortedDictionary<string, Dictionary<string, decimal>>(StringComparer.Ordinal);
                var monat = h[..7];
                if (!proMonat.TryGetValue(monat, out var arten)) proMonat[monat] = arten = new Dictionary<string, decimal>(StringComparer.Ordinal);
                arten[code] = arten.GetValueOrDefault(code) + betrag;
            }
        }
        return res;
    }

    // ── Swissdec-Lohnartenkatalog (SV-Pflichten) ────────────────────────────
    public sealed record Lohnart(string Code, string Bezeichnung, bool Brutto, bool Ahv, bool Uvg, bool Uvgz, bool Ktg, bool Bvg, bool Qst);

    public static Dictionary<string, Lohnart> LadeKatalog()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Pfad("Assets", "Swissdec", "SwissdecLohnarten.json")));
        var res = new Dictionary<string, Lohnart>(StringComparer.Ordinal);
        foreach (var e in doc.RootElement.GetProperty("lohnarten").EnumerateArray())
        {
            bool F(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;
            var code = e.GetProperty("code").GetString()!;
            res[code] = new Lohnart(code, e.GetProperty("bezeichnung").GetString() ?? "",
                F("brutto"), F("ahv"), F("uvg"), F("uvgz"), F("ktg"), F("bvg"), F("qst"));
        }
        return res;
    }

    // ── Referenz-XML ────────────────────────────────────────────────────────
    private static readonly XNamespace Sd = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration";
    private static readonly XNamespace C  = "urn:ch:swissdec:common:v3:20260306";

    /// <summary>YTD-Werte einer Person aus der RETROSPECTIVE (alle Blöcke summiert).</summary>
    public sealed record XmlYtd(decimal Ahv, decimal Alv, decimal Alvz, decimal Uvg, decimal Uvgz, decimal Ktg);

    /// <summary>Monatswerte aus der MONTHLY (Statistik).</summary>
    public sealed record XmlMonat(decimal SozialAbzuege, decimal Bvg);

    public static Dictionary<string, XmlYtd> LadeXmlYtd(int jahr, int monat)
    {
        // Dateinamen nicht raten: der Januar heisst «RefXML_2025-01_RETROSPECTIVE (1).xml».
        var pfad = Directory.GetFiles(Pfad("SWISSCEC", "RefXML"), $"RefXML_{jahr}-{monat:00}_RETROSPECTIVE*.xml")
                            .OrderBy(f => f.Length).FirstOrDefault();
        var res = new Dictionary<string, XmlYtd>(StringComparer.OrdinalIgnoreCase);
        if (pfad == null) return res;
        foreach (var p in XDocument.Load(pfad).Descendants(Sd + "Person"))
        {
            var tf = TfVon(p);
            if (tf == null) continue;
            decimal S(XName name) => p.Descendants(name).Sum(e => decimal.Parse(e.Value, CultureInfo.InvariantCulture));
            res[tf] = new XmlYtd(
                S(Sd + "AHV-AVS-BaseSalary"), S(Sd + "ALV-AC-Income"), S(Sd + "ALVZ-ACS-Income"),
                S(Sd + "UVG-LAA-ContributorySalary"), S(Sd + "UVGZ-LAAC-ContributorySalary"),
                S(Sd + "KTG-AMC-ContributorySalary"));
        }
        return res;
    }

    public static Dictionary<string, XmlMonat> LadeXmlMonat(int jahr, int monat)
    {
        var pfad = Directory.GetFiles(Pfad("SWISSCEC", "RefXML"), $"RefXML_{jahr}-{monat:00}_MONTHLY*.xml")
                            .OrderBy(f => f.Length).FirstOrDefault();
        var res = new Dictionary<string, XmlMonat>(StringComparer.OrdinalIgnoreCase);
        if (pfad == null) return res;
        foreach (var p in XDocument.Load(pfad).Descendants(Sd + "Person"))
        {
            var tf = TfVon(p);
            if (tf == null) continue;
            var mv = p.Descendants(Sd + "MonthlyValues").FirstOrDefault();
            if (mv == null) continue;
            decimal V(string name) => decimal.TryParse(mv.Element(Sd + name)?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
            // Vorzeichen behalten: Swissdec meldet Abzüge negativ, Rückerstattungen positiv.
            res[tf] = new XmlMonat(-V("SocialContributions"), -V("BVG-LPP-RegularContribution"));
        }
        return res;
    }

    private static string? TfVon(XElement person)
    {
        var id = person.Element(C + "Work")?.Attribute("workID")?.Value;
        return id != null && id.StartsWith("#work_TF", StringComparison.Ordinal) ? id.Substring(6, 4) : null;
    }
}

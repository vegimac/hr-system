using System.Text.Json;
using System.Text.RegularExpressions;

namespace HrSystem.Services.Elm;

/// <summary>
/// Bericht des Swissdec Quality Tools (Lohntest, Walter 15.10.2026) für die Anzeige in OneCrew:
/// je Monat (Sequenz) und Testfall jede Abweichung mit Soll (Referenz Muster AG), Ist (unsere Meldung)
/// und dem Feld. Rein — liest nur das JSON der Quality-Tool-Schnittstelle, rechnet nichts um.
/// Das Quality Tool liefert ausschliesslich Abweichungen; ein Monat ohne Einträge ist erledigt
/// oder noch nicht gesendet — das sagt die Punktzahl (Fortschritt über alle Monate).
/// </summary>
public static class QualityToolBericht
{
    public record Abweichung(string Feld, string? Soll, string? Ist, string Art, string Pfad,
        string? DeclarationId, string? Gesendet);
    public record Fall(string Id, string Name, string Typ, List<Abweichung> Abweichungen);
    public record Monat(string Sequenz, string Teilprozess, List<Fall> Faelle)
    {
        public int Anzahl => Faelle.Sum(f => f.Abweichungen.Count);
    }
    public record Bericht(string? MonitoringId, double Punkte, string? Meldung, List<Monat> Monate)
    {
        public int Anzahl => Monate.Sum(m => m.Anzahl);
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private record RReport(string? MonitoringId, double? Score, string? ApiMessage, List<RSequence>? Sequences);
    private record RSequence(string? Sequence, List<RSubprocess>? Subprocesses);
    private record RSubprocess(string? Subprocess, List<RTestcase>? Testcases);
    private record RTestcase(string? TestcaseId, string? Name, string? Type, List<RResult>? Results);
    private record RResult(string? DeclarationID, string? RequestTs, string? Outcome, string? Type,
        string? Controlvalue, string? Testvalue, string? Controlxpath, string? Testxpath);

    public static Bericht Lies(string json)
    {
        var r = JsonSerializer.Deserialize<RReport>(json, Json) ?? new RReport(null, null, null, null);
        var monate = new List<Monat>();
        foreach (var s in r.Sequences ?? new())
        foreach (var sp in s.Subprocesses ?? new())
        {
            var faelle = (sp.Testcases ?? new())
                .Select(t => new Fall(
                    t.TestcaseId ?? "",
                    FallName(t.TestcaseId, t.Name),
                    t.Type ?? "",
                    (t.Results ?? new()).Select(Abw).ToList()))
                .Where(f => f.Abweichungen.Count > 0)
                .OrderBy(f => SortSchluessel(f.Id))
                .ToList();
            monate.Add(new Monat(s.Sequence ?? "", sp.Subprocess ?? "", faelle));
        }
        return new Bericht(r.MonitoringId, r.Score ?? 0, r.ApiMessage,
            monate.OrderBy(m => m.Sequenz, StringComparer.Ordinal).ThenBy(m => m.Teilprozess).ToList());
    }

    private static Abweichung Abw(RResult x)
    {
        var soll = Wert(x.Controlvalue);
        var ist = Wert(x.Testvalue);
        var pfad = Wert(x.Controlxpath) ?? Wert(x.Testxpath) ?? "";
        var art = x.Type switch
        {
            "child_lookup" when soll == null => "Element zu viel",
            "child_lookup" when ist == null => "Element fehlt",
            "child_lookup" => "Element anders",
            "child_nodelist_length" => "Anzahl Elemente",
            _ => "Wert",
        };
        if (x.Type == "child_lookup")
        {
            soll = ElementName(soll);
            ist = ElementName(ist);
        }
        return new Abweichung(Feld(pfad), soll, ist, art, pfad, x.DeclarationID, x.RequestTs);
    }

    /// <summary>Das Quality Tool schreibt fehlende Werte als Text «null».</summary>
    private static string? Wert(string? s) => string.IsNullOrEmpty(s) || s == "null" ? null : s;

    /// <summary>«{urn:…}SporadicBenefits» → «SporadicBenefits».</summary>
    private static string? ElementName(string? s)
        => s == null ? null : s.Contains('}') ? s[(s.LastIndexOf('}') + 1)..] : s;

    private static readonly Regex Index = new(@"\[\d+\]", RegexOptions.Compiled);

    /// <summary>
    /// Lesbares Feld aus dem XPath: die letzten drei Elemente ohne Positionen, das letzte übersetzt.
    /// «…/TaxAtSourceSalary[1]/Current[1]/TaxAtSource[1]/text()[1]» → «TaxAtSourceSalary › Current › Quellensteuer».
    /// </summary>
    public static string Feld(string? xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath)) return "(ohne Angabe)";
        var teile = Index.Replace(xpath, "").Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t != "text()").ToList();
        if (teile.Count == 0) return xpath;
        var letzte = teile.TakeLast(3).ToList();
        letzte[^1] = Deutsch.TryGetValue(letzte[^1], out var d) ? d : letzte[^1];
        return string.Join(" › ", letzte);
    }

    private static readonly Dictionary<string, string> Deutsch = new()
    {
        ["TaxAtSource"] = "Quellensteuer",
        ["TotalTaxAtSource"] = "Total Quellensteuer",
        ["TaxAtSourceCode"] = "QST-Code",
        ["TaxableEarning"] = "QST-pflichtiger Lohn",
        ["AscertainedTaxableEarning"] = "Satzbestimmender Lohn",
        ["HourlyOrLessonSalary"] = "Stunden-/Lektionenlohn",
        ["SocialContributions"] = "Sozialbeiträge",
        ["FamilyIncomeSupplement"] = "Familienzulagen",
        ["GrossSalary"] = "Bruttolohn",
        ["AHV-ALV-Income"] = "AHV-Lohn",
        ["ALV-Income"] = "ALV-Lohn",
        ["WorkedHours"] = "Arbeitsstunden",
        ["ActivityRate"] = "Beschäftigungsgrad",
        ["WeeklyHours"] = "Wochenstunden",
        ["SporadicBenefits"] = "Aperiodische Leistungen",
    };

    /// <summary>«14» → Name «TF14 Egli Anna»; Summen-Fälle behalten ihren Namen.</summary>
    private static string FallName(string? id, string? name)
    {
        if (id == "SalaryTotals") return "Summen der Meldung";
        if (id == "catchAll") return "Übrige Abweichungen";
        var n = (name ?? "").Trim();
        if (id is null || n == id || !id.All(char.IsDigit)) return n.Length > 0 ? n : id ?? "";
        return $"TF{id} {n}";
    }

    /// <summary>Personen nach Testfall-Nummer, Summen und Sammelfälle danach.</summary>
    private static (int, int, string) SortSchluessel(string id)
        => int.TryParse(id, out var n) ? (0, n, "") : (1, 0, id);
}

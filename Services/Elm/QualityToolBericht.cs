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
    /// <param name="Feld">Kurzer Pfad auf Deutsch (z.B. «Statistik › Monatswerte › Bruttolohn»).</param>
    /// <param name="Klartext">Ein Satz, was die Abweichung bedeutet.</param>
    public record Abweichung(string Feld, string? Soll, string? Ist, string Art, string Pfad,
        string? DeclarationId, string? Gesendet, string? Klartext = null);
    public record Fall(string Id, string Name, string Typ, List<Abweichung> Abweichungen);
    public record Monat(string Sequenz, string Teilprozess, List<Fall> Faelle)
    {
        public int Anzahl => Faelle.Sum(f => f.Abweichungen.Count);
    }
    public record Bericht(string? MonitoringId, double Punkte, string? Meldung, List<Monat> Monate)
    {
        public int Anzahl => Monate.Sum(m => m.Anzahl);
    }

    /// <summary>
    /// Ein Monat im Lauf — aus QT-Bericht und unseren Sendungen zusammengebaut
    /// (Walter 16.10.2026: Punktzahl allein sagt nicht, welcher Monat noch offen ist).
    /// </summary>
    /// <param name="Status">ok = keine Abweichung (QT listet nur Differenzen), rot = Abweichungen,
    /// warn = Dialog/Freigabe bei uns offen, grau = noch nicht an QT gesendet.</param>
    public record MonatStand(string Sequenz, string Teilprozess, string Status, string Text,
        int Anzahl, Monat? Bericht);

    /// <summary>
    /// Alle Monate, die wir an das Quality Tool gesendet haben, plus Monate mit Abweichungen
    /// aus dem Bericht. Gesendet ohne Eintrag im Bericht → «Stimmt».
    /// </summary>
    public static List<MonatStand> Uebersicht(Bericht bericht, IEnumerable<ElmVorgang> qtSendungen)
    {
        var abw = bericht.Monate
            .Where(m => m.Teilprozess is "" or "MonthlySalary")
            .GroupBy(m => m.Sequenz, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Pro Periode die jüngste QT-Sendung (Ersatz schlägt die ältere).
        var gesendet = new Dictionary<string, ElmVorgang>(StringComparer.Ordinal);
        foreach (var v in qtSendungen
                     .Where(v => v.Art == "monthly" && v.Url != null
                                 && v.Url.Contains("qualitytool", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(v => v.Gesendet))
        {
            var (jahr, monat) = ElmMeldungAnzeige.Periode(v);
            if (jahr == null || monat == null) continue;
            gesendet[$"{jahr.Value:D4}-{monat.Value:D2}"] = v;
        }

        var keys = gesendet.Keys.Union(abw.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var liste = new List<MonatStand>();
        foreach (var seq in keys)
        {
            abw.TryGetValue(seq, out var mon);
            gesendet.TryGetValue(seq, out var vorgang);
            var anzahl = mon?.Anzahl ?? 0;
            if (anzahl > 0)
            {
                liste.Add(new MonatStand(seq, mon!.Teilprozess, "rot",
                    anzahl == 1 ? "1 Abweichung" : $"{anzahl} Abweichungen", anzahl, mon));
                continue;
            }
            if (vorgang == null)
            {
                liste.Add(new MonatStand(seq, "MonthlySalary", "grau", "Noch nicht gesendet", 0, null));
                continue;
            }
            var stand = ElmMeldungAnzeige.Meldung(vorgang);
            if (stand.Stufe is ElmMeldungAnzeige.Warn or ElmMeldungAnzeige.Rot)
            {
                liste.Add(new MonatStand(seq, "MonthlySalary", "warn", stand.Text, 0, null));
                continue;
            }
            liste.Add(new MonatStand(seq, "MonthlySalary", "ok", "Stimmt", 0, null));
        }
        return liste;
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
        if (x.Type is "child_lookup" or "child_nodelist_length")
        {
            soll = Bezeichner(soll);
            ist = Bezeichner(ist);
        }
        var feld = Feld(pfad);
        return new Abweichung(feld, soll, ist, art, pfad, x.DeclarationID, x.RequestTs,
            Klartext(art, feld, soll, ist));
    }

    /// <summary>Das Quality Tool schreibt fehlende Werte als Text «null».</summary>
    private static string? Wert(string? s) => string.IsNullOrEmpty(s) || s == "null" ? null : s;

    /// <summary>«{urn:…}MarriagePartner» → «Ehepartner» (deutscher Anzeigename).</summary>
    private static string? Bezeichner(string? s)
    {
        if (s == null) return null;
        var name = s.Contains('}') ? s[(s.LastIndexOf('}') + 1)..] : s;
        return Deutsch.TryGetValue(name, out var d) ? d : name;
    }

    private static readonly Regex Index = new(@"\[\d+\]", RegexOptions.Compiled);

    /// <summary>
    /// Lesbarer Pfad aus dem XPath: die letzten drei Elemente ohne Positionen, alle auf Deutsch.
    /// «…/StatisticSalary[1]/AnnualValues[1]/Earnings13th[1]/text()[1]»
    /// → «Statistik › Jahreswerte › 13. Monatslohn».
    /// </summary>
    public static string Feld(string? xpath)
    {
        if (string.IsNullOrWhiteSpace(xpath)) return "(ohne Angabe)";
        var teile = Index.Replace(xpath, "").Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t != "text()").ToList();
        if (teile.Count == 0) return xpath;
        return string.Join(" › ", teile.TakeLast(3).Select(Name));
    }

    private static string Name(string english)
        => Deutsch.TryGetValue(english, out var d) ? d : english;

    /// <summary>Ein Satz für Walter: was die Abweichung praktisch heisst.</summary>
    public static string? Klartext(string art, string feld, string? soll, string? ist)
    {
        return art switch
        {
            "Element fehlt" => $"Bei uns fehlt «{ist ?? "dieses Element"}» — Swissdec erwartet es unter «{feld}».",
            "Element zu viel" => $"Wir haben «{soll ?? ist ?? "dieses Element"}» gemeldet — Swissdec erwartet es nicht.",
            "Element anders" => $"Element weicht ab: Swissdec «{soll ?? "–"}», OneCrew «{ist ?? "–"}».",
            "Anzahl Elemente" => $"Anzahl Unterpunkte unter «{feld}»: Swissdec {soll ?? "–"}, OneCrew {ist ?? "–"} (oft fehlt oder ist etwas zu viel).",
            "Wert" when IstBetrag(soll) || IstBetrag(ist)
                => $"Betrag weicht ab: Swissdec {soll ?? "–"}, OneCrew {ist ?? "–"}.",
            "Wert" => $"Wert weicht ab: Swissdec «{soll ?? "–"}», OneCrew «{ist ?? "–"}».",
            _ => null,
        };
    }

    private static bool IstBetrag(string? s)
        => s != null && Regex.IsMatch(s, @"^-?\d+([.,]\d+)?$");

    private static readonly Dictionary<string, string> Deutsch = new(StringComparer.Ordinal)
    {
        // Blöcke
        ["TaxAtSourceSalaries"] = "Quellensteuer",
        ["TaxAtSourceSalary"] = "Quellensteuer-Zeile",
        ["StatisticSalaries"] = "Statistik",
        ["StatisticSalary"] = "Statistik",
        ["AdditionalParticulars"] = "Zusatzangaben",
        ["MonthlyValues"] = "Monatswerte",
        ["AnnualValues"] = "Jahreswerte",
        ["Current"] = "Laufender Monat",
        ["Old"] = "Korrektur alt",
        ["New"] = "Korrektur neu",
        ["Particulars"] = "Personalien",
        ["Addresses"] = "Adressen",
        ["Address"] = "Adresse",
        ["Work"] = "Anstellung",
        ["WorkingTime"] = "Arbeitszeit",
        ["KindOfWagePayment"] = "Lohnart",
        ["SalaryTotals"] = "Summen",
        ["TaxAtSourceTotals"] = "QST-Summen",
        ["TotalMonth"] = "Monatssumme",
        ["OtherActivities"] = "Weitere Erwerbstätigkeit",
        ["Residence"] = "Wohnsitz",
        ["KindOfResidence"] = "Art des Wohnsitzes",
        ["TaxAtSourceCategory"] = "QST-Kategorie",
        ["DeclarationCategory"] = "An-/Abmeldung",
        ["Entry"] = "Eintritt",
        ["Withdrawal"] = "Austritt",
        ["Mutation"] = "Mutation",
        ["Children"] = "Kinder",
        ["MarriagePartner"] = "Ehepartner",
        ["SingleParentFamily"] = "Alleinerziehend",
        ["CivilStatus"] = "Zivilstand",
        ["Denomination"] = "Konfession",
        // Beträge / Codes
        ["TaxAtSource"] = "Quellensteuer-Betrag",
        ["TotalTaxAtSource"] = "Total Quellensteuer",
        ["TaxAtSourceCode"] = "QST-Code",
        ["TaxableEarning"] = "QST-pflichtiger Lohn",
        ["AscertainedTaxableEarning"] = "Satzbestimmender Lohn",
        ["HourlyOrLessonSalary"] = "Stunden-/Lektionenlohn",
        ["SocialContributions"] = "Sozialbeiträge (AHV/ALV/NBU)",
        ["FamilyIncomeSupplement"] = "Familienzulagen",
        ["GrossSalary"] = "Bruttolohn",
        ["GrossBaseSalaryAndRegularAllowance"] = "Bruttolohn + regelmässige Zulagen",
        ["Allowances"] = "Zulagen",
        ["PaymentsByThird"] = "Drittleistungen (Taggelder)",
        ["BVG-LPP-RegularContribution"] = "BVG-Beitrag",
        ["ShortTimeWorkCompensation"] = "Kurzarbeitsentschädigung",
        ["Earnings13th"] = "13. Monatslohn",
        ["Overtime"] = "Überstunden",
        ["SporadicBenefits"] = "Aperiodische Leistungen",
        ["FringeBenefits"] = "Naturalleistungen",
        ["CapitalPayment"] = "Kapitalleistung",
        ["OtherBenefits"] = "Übrige Leistungen",
        ["AHV-ALV-Income"] = "AHV-Lohn",
        ["ALV-Income"] = "ALV-Lohn",
        ["WorkedHours"] = "Arbeitsstunden",
        ["ActivityRate"] = "Beschäftigungsgrad",
        ["WeeklyHours"] = "Wochenstunden",
        ["Period"] = "Zeitraum",
        ["from"] = "von",
        ["until"] = "bis",
        ["ValidAsOf"] = "Gültig ab",
        ["Reason"] = "Grund",
        ["Status"] = "Status",
        ["ZIP-Code"] = "PLZ",
        ["City"] = "Ort",
        ["Street"] = "Strasse",
        ["Country"] = "Land",
        ["ResidenceCanton"] = "Wohnkanton",
        ["MunicipalityID"] = "BFS-Gemeindenummer",
        ["WorkMunicipalityID"] = "Arbeitsgemeinde (BFS)",
        ["TaxAtSourceCanton"] = "QST-Kanton",
        ["TaxAtSourceMunicipalityID"] = "QST-Gemeinde",
        ["CantonCH"] = "Kanton CH",
        ["AbroadCountry"] = "Wohnsitzstaat",
        ["Weekly"] = "Wochenaufenthalt",
        ["Daily"] = "Tägliche Rückkehr",
        ["Education"] = "Ausbildung",
        ["Position"] = "Stellung",
        ["JobTitle"] = "Berufsbezeichnung",
        ["LeaveEntitlement"] = "Ferienanspruch (Tage)",
        ["EntryDate"] = "Eintrittsdatum",
        ["WithdrawalDate"] = "Austrittsdatum",
        ["ResidenceCategory"] = "Aufenthaltsbewilligung",
        ["SV-AS-Number"] = "AHV-Nummer",
        ["EmployeeNumber"] = "Personalnummer",
        ["Lastname"] = "Nachname",
        ["Firstname"] = "Vorname",
        ["DateOfBirth"] = "Geburtsdatum",
        ["Sex"] = "Geschlecht",
        ["Nationality"] = "Nationalität",
        ["LanguageCode"] = "Sprache",
        ["CurrentMonth"] = "Meldemonat",
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

namespace HrSystem.Services;

/// <summary>
/// EMA-Gründe für die Aufgabenliste (Walter 30.10.2026, Systemtest 04_01_03 und 04_03_01).
/// Erkennt die Gründe aus den vorhandenen Daten. Kein manueller Grund.
/// QST-Mutationen gelten ab dem Folgemonat (oder am 1., wenn der Wechsel schon auf den 1. fällt).
/// Eintritt ins Unternehmen und Austritt aus dem Unternehmen behalten ihr Datum.
/// Teilpensionierung, Arbeitsunterbruch nur Risiko und nicht versichert gibt es hier nicht:
/// dafür liegt kein Fakt vor, und ein erfundener Grund wäre falsch.
/// </summary>
public static class EmaAufgabeRegel
{
    public const string Bvg = "BVG";
    public const string Qst = "QST";
    public static readonly DateOnly ToDoAb = new(2026, 1, 1);

    public sealed record Aufgabe(
        int EmployeeId, string Domaene, string Art, string Grund, string Text, DateOnly Stichtag, string? Zusatz);

    public sealed record BvgSpanne(int EmployeeId, DateOnly Von, DateOnly? Bis);
    public sealed record BvgCode(int EmployeeId, DateOnly Von, DateOnly? Bis, string? Code, string? Eintrittsgrund, bool? VollArbeitsfaehig);
    public sealed record Abwesenheit(int EmployeeId, DateOnly Von, DateOnly Bis, string Art);
    public sealed record Wechsel(int EmployeeId, DateOnly Datum, string? Wert);
    public sealed record Person(int EmployeeId, DateOnly? Eintritt, DateOnly? Austritt, string? Austrittsgrund, string? Nationalitaet);

    public sealed record QstVersion(
        int EmployeeId, DateOnly ValidFrom, DateOnly? ValidTo, string? Code, string? Kanton,
        int Kinder, bool Kirche, string? Gemeinde, string? PartnerArbeitsort, string? Bewilligung);

    public static List<Aufgabe> BvgAufgaben(
        IEnumerable<BvgSpanne> spannen, IEnumerable<BvgCode> codes, IEnumerable<Abwesenheit> absenzen,
        IEnumerable<Wechsel> lohn, IEnumerable<Wechsel> adresse, IEnumerable<Wechsel> zivilstand,
        IEnumerable<Person> personen)
    {
        var res = new List<Aufgabe>();
        var codeListe = codes.ToList();
        var krank = absenzen.Where(a => IstKrank(a.Art)).ToList();
        var personenBy = personen.ToDictionary(p => p.EmployeeId);
        var intervals = spannen
            .GroupBy(s => s.EmployeeId)
            .ToDictionary(g => g.Key, g => Merge(g.Select(s => (s.Von, s.Bis))));
        foreach (var g in codeListe.GroupBy(c => c.EmployeeId))
        {
            if (!intervals.ContainsKey(g.Key))
                intervals[g.Key] = Merge(g.Select(c => (c.Von, c.Bis)));
        }

        foreach (var (emp, ivs) in intervals)
        {
            personenBy.TryGetValue(emp, out var person);
            var eigeneCodes = codeListe.Where(c => c.EmployeeId == emp).OrderBy(c => c.Von).ToList();
            var eigeneKrank = krank.Where(a => a.EmployeeId == emp).ToList();
            foreach (var iv in ivs)
            {
                var code = CodeAm(eigeneCodes, iv.Von);
                bool unterbruch = eigeneKrank.Any(a => a.Bis == iv.Von.AddDays(-1))
                    || string.Equals(code?.Eintrittsgrund, "interruptionOfEmployment", StringComparison.OrdinalIgnoreCase);
                bool fit = code?.VollArbeitsfaehig ?? !eigeneKrank.Any(a => a.Von <= iv.Von && a.Bis >= iv.Von);
                Add(res, emp, Bvg, "EINTRITT",
                    unterbruch ? "interruptionOfEmployment" : "entryCompany",
                    unterbruch ? "Beginn nach Arbeitsunterbruch" : "Eintritt Vorsorgeplan",
                    iv.Von, Fit(fit));

                if (iv.Bis == null) continue;
                var ende = iv.Bis.Value;
                bool tod = person?.Austritt == ende
                    && string.Equals(person.Austrittsgrund, "TODESFALL", StringComparison.OrdinalIgnoreCase);
                bool arbeitsunterbruch = eigeneKrank.Any(a => a.Von == ende || a.Von == ende.AddDays(1));
                bool fitEnde = !eigeneKrank.Any(a => a.Von <= ende && a.Bis >= ende);
                if (tod)
                    Add(res, emp, Bvg, "AUSTRITT", "deceased", "Verstorben", ende, Fit(fitEnde));
                else if (arbeitsunterbruch && person?.Austritt != ende)
                    Add(res, emp, Bvg, "AUSTRITT", "interruptionOfEmployment", "Beginn Arbeitsunterbruch", ende, Fit(fitEnde));
                else
                    Add(res, emp, Bvg, "AUSTRITT", "withdrawalPensionPlan", "Austritt Vorsorgeplan", ende, Fit(fitEnde));
            }

            string? letzter = null;
            foreach (var c in eigeneCodes)
            {
                if (letzter != null && !Gleich(letzter, c.Code) && LiegtIn(ivs, c.Von) && ivs.All(iv => iv.Von != c.Von))
                    Add(res, emp, Bvg, "MUTATION", "plan", "Vorsorgeplan, Betrieb, Kategorie", c.Von, null);
                letzter = c.Code;
            }
            foreach (var a in eigeneKrank)
            {
                if (!LiegtIn(ivs, a.Von) || ivs.Any(iv => iv.Von == a.Von)) continue;
                Add(res, emp, Bvg, "MUTATION", "incapacity", "Arbeitsunfähigkeit", a.Von, null);
                if (LiegtIn(ivs, a.Bis.AddDays(1)))
                    Add(res, emp, Bvg, "MUTATION", "fullyFit", "vollständig arbeitsfähig", a.Bis.AddDays(1), null);
            }
            string? lohnVor = null;
            foreach (var w in lohn.Where(w => w.EmployeeId == emp).OrderBy(w => w.Datum))
            {
                if (lohnVor != null && !Gleich(lohnVor, w.Wert) && LiegtIn(ivs, w.Datum) && ivs.All(iv => iv.Von != w.Datum))
                    Add(res, emp, Bvg, "MUTATION", "salary", "Lohnänderung", w.Datum, null);
                lohnVor = w.Wert;
            }
            string? adrVor = null;
            foreach (var w in adresse.Where(w => w.EmployeeId == emp).OrderBy(w => w.Datum))
            {
                if (adrVor != null && !Gleich(adrVor, w.Wert) && LiegtIn(ivs, w.Datum))
                    Add(res, emp, Bvg, "MUTATION", "address", "Wohnadresse", w.Datum, null);
                adrVor = w.Wert;
            }
            string? zivVor = null;
            foreach (var w in zivilstand.Where(w => w.EmployeeId == emp).OrderBy(w => w.Datum))
            {
                if (zivVor != null && !Gleich(zivVor, w.Wert) && LiegtIn(ivs, w.Datum))
                    Add(res, emp, Bvg, "MUTATION", "civilStatus", "Zivilstandsänderung", w.Datum, null);
                zivVor = w.Wert;
            }
        }
        return res.Where(a => a.Stichtag >= ToDoAb)
            .OrderBy(a => a.Stichtag).ThenBy(a => a.Grund).ToList();
    }

    public static List<Aufgabe> QstAufgaben(
        IEnumerable<QstVersion> versionen, IEnumerable<Wechsel> zivilstand, IEnumerable<Person> personen)
    {
        var res = new List<Aufgabe>();
        var ziv = zivilstand.ToLookup(z => z.EmployeeId);
        var personenBy = personen.ToDictionary(p => p.EmployeeId);
        foreach (var g in versionen.GroupBy(v => v.EmployeeId))
        {
            var liste = g.OrderBy(v => v.ValidFrom).ToList();
            personenBy.TryGetValue(g.Key, out var person);
            for (int i = 0; i < liste.Count; i++)
            {
                var v = liste[i];
                var vor = i == 0 ? null : liste[i - 1];
                bool nahtlos = vor != null && (vor.ValidTo == null || v.ValidFrom <= vor.ValidTo.Value.AddDays(1));
                if (vor == null || !nahtlos)
                {
                    bool eintritt = person?.Eintritt != null
                        && Math.Abs(v.ValidFrom.DayNumber - person.Eintritt.Value.DayNumber) <= 31;
                    Add(res, g.Key, Qst, "EINTRITT",
                        eintritt ? "entryCompany" : "cantonChange",
                        eintritt ? "Eintritt in das Unternehmen" : "Kantonswechsel",
                        eintritt ? person!.Eintritt!.Value : MutationsDatum(v.ValidFrom), null);
                }
                else
                    Add(res, g.Key, Qst, GrundArt(vor, v, person, ziv[g.Key]), GrundCode(vor, v, person, ziv[g.Key]),
                        GrundText(vor, v, person, ziv[g.Key]), GrundDatum(vor, v, person, ziv[g.Key]), null);

                bool lueckeDanach = i < liste.Count - 1 && v.ValidTo != null
                    && liste[i + 1].ValidFrom > v.ValidTo.Value.AddDays(1);
                bool letzte = i == liste.Count - 1 || lueckeDanach;
                if (v.ValidTo != null && letzte && !(i < liste.Count - 1 && liste[i + 1].ValidFrom <= v.ValidTo.Value.AddDays(1)))
                {
                    var ende = v.ValidTo.Value;
                    bool firma = person?.Austritt != null && person.Austritt.Value <= ende.AddDays(31)
                        && person.Austritt.Value >= v.ValidFrom;
                    Add(res, g.Key, Qst, "AUSTRITT",
                        firma ? "withdrawalCompany" : AustrittGrund(v, person),
                        firma ? "Austritt aus dem Unternehmen" : AustrittText(v, person),
                        firma ? person!.Austritt!.Value : MutationsDatum(ende), null);
                }
            }
        }
        return res.Where(a => a.Stichtag >= ToDoAb)
            .GroupBy(a => (a.EmployeeId, a.Domaene, a.Grund, a.Stichtag))
            .Select(g => g.First())
            .OrderBy(a => a.Stichtag).ThenBy(a => a.Grund).ToList();
    }

    /// <summary>Mutation gilt am 1. des Folgemonats. Liegt der Wechsel schon auf dem 1., bleibt er.</summary>
    public static DateOnly MutationsDatum(DateOnly ausloeser) =>
        QstKantonswechselService.FolgeMonatErster(ausloeser);

    private static string GrundArt(QstVersion vor, QstVersion neu, Person? person, IEnumerable<Wechsel> ziv)
    {
        var code = GrundCode(vor, neu, person, ziv);
        return code is "withdrawalCompany" or "settled-C" or "naturalization" ? "AUSTRITT" : "MUTATION";
    }

    private static string GrundCode(QstVersion vor, QstVersion neu, Person? person, IEnumerable<Wechsel> ziv)
    {
        if (IstNull(neu.Code))
        {
            if (person?.Austritt != null && person.Austritt.Value <= neu.ValidFrom.AddDays(31)
                && person.Austritt.Value >= vor.ValidFrom)
                return "withdrawalCompany";
            return AustrittGrund(neu, person);
        }
        if (KantonAnders(vor, neu)) return "cantonChange";
        var (a, n) = (Parse(vor.Code), Parse(neu.Code));
        if (a != null && n != null && a.Value.Buchstabe != n.Value.Buchstabe)
            return ziv.Any(z => z.Datum > vor.ValidFrom && z.Datum <= neu.ValidFrom) ? "civilstate" : "partnerWork";
        if (a != null && n != null && a.Value.Kinder != n.Value.Kinder) return "childrenDeduction";
        if (a != null && n != null && a.Value.Kirche != n.Value.Kirche) return "churchTax";
        if (vor.Kirche != neu.Kirche) return "churchTax";
        if (vor.Kinder != neu.Kinder) return "childrenDeduction";
        if (PartnerExWechsel(vor.PartnerArbeitsort, neu.PartnerArbeitsort)) return "partnerIncome";
        if (!Gleich(vor.Gemeinde, neu.Gemeinde) || KantonAnders(vor, neu)) return "residence";
        return "others";
    }

    private static string GrundText(QstVersion vor, QstVersion neu, Person? person, IEnumerable<Wechsel> ziv) =>
        GrundCode(vor, neu, person, ziv) switch
        {
            "withdrawalCompany" => "Austritt aus dem Unternehmen",
            "settled-C" => "Niederlassung C",
            "naturalization" => "Einbürgerung",
            "cantonChange" => "Kantonswechsel",
            "civilstate" => "Zivilstand",
            "partnerWork" => "Arbeit des Partners",
            "childrenDeduction" => "Kinderabzug",
            "churchTax" => "Kirchensteuer",
            "partnerIncome" => "Wechsel Einkünfte Partner CH/EX",
            "residence" => "Wohnadresse",
            _ => "Andere",
        };

    private static DateOnly GrundDatum(QstVersion vor, QstVersion neu, Person? person, IEnumerable<Wechsel> ziv)
    {
        var code = GrundCode(vor, neu, person, ziv);
        if (code == "withdrawalCompany" && person?.Austritt != null) return person.Austritt.Value;
        return MutationsDatum(neu.ValidFrom);
    }

    private static string AustrittGrund(QstVersion v, Person? person)
    {
        if (string.Equals(v.Bewilligung, "C", StringComparison.OrdinalIgnoreCase)) return "settled-C";
        if (string.Equals(person?.Nationalitaet, "CH", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v.Bewilligung, "CH", StringComparison.OrdinalIgnoreCase))
            return "naturalization";
        if (KantonLeer(v)) return "cantonChange";
        return "others";
    }

    private static string AustrittText(QstVersion v, Person? person) => AustrittGrund(v, person) switch
    {
        "settled-C" => "Niederlassung C",
        "naturalization" => "Einbürgerung",
        "cantonChange" => "Kantonswechsel",
        _ => "Andere",
    };

    private static bool KantonLeer(QstVersion v) => string.IsNullOrWhiteSpace(v.Kanton);
    private static bool KantonAnders(QstVersion a, QstVersion b) =>
        !string.IsNullOrWhiteSpace(a.Kanton) && !string.IsNullOrWhiteSpace(b.Kanton)
        && !string.Equals(a.Kanton.Trim(), b.Kanton.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool PartnerExWechsel(string? a, string? b)
    {
        bool Ex(string? s) => string.Equals(s?.Trim(), "EX", StringComparison.OrdinalIgnoreCase);
        bool Ch(string? s)
        {
            var t = (s ?? "").Trim();
            return t.Length == 2 && !Ex(t);
        }
        return (Ex(a) && Ch(b)) || (Ch(a) && Ex(b));
    }

    private static bool IstNull(string? code) =>
        QstVordefinierteKategorie.Parse(code) is { } k && QstVordefinierteKategorie.IstNullAbzug(k.Art);

    private readonly record struct CodeTeile(string Buchstabe, int Kinder, bool Kirche);

    private static CodeTeile? Parse(string? code)
    {
        var m = System.Text.RegularExpressions.Regex.Match((code ?? "").Trim().ToUpperInvariant(), @"^([A-Z]{1,2})(\d)([YN])$");
        if (!m.Success) return null;
        return new CodeTeile(m.Groups[1].Value, int.Parse(m.Groups[2].Value), m.Groups[3].Value == "Y");
    }

    private static string Fit(bool fit) => fit ? "100 % arbeitsfähig" : "nicht voll arbeitsfähig";

    private static bool IstKrank(string art) =>
        art.Equals("KRANK", StringComparison.OrdinalIgnoreCase) || art.Equals("UNFALL", StringComparison.OrdinalIgnoreCase);

    private static bool Gleich(string? a, string? b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool LiegtIn(List<(DateOnly Von, DateOnly? Bis)> ivs, DateOnly tag) =>
        ivs.Any(iv => iv.Von <= tag && (iv.Bis == null || iv.Bis >= tag));

    private static BvgCode? CodeAm(List<BvgCode> codes, DateOnly tag) =>
        codes.Where(c => c.Von <= tag && (c.Bis == null || c.Bis >= tag)).OrderByDescending(c => c.Von).FirstOrDefault();

    private static List<(DateOnly Von, DateOnly? Bis)> Merge(IEnumerable<(DateOnly Von, DateOnly? Bis)> raw)
    {
        var sorted = raw.OrderBy(x => x.Von).ToList();
        var res = new List<(DateOnly Von, DateOnly? Bis)>();
        foreach (var c in sorted)
        {
            if (res.Count > 0)
            {
                var last = res[^1];
                if (last.Bis == null || c.Von <= last.Bis.Value.AddDays(1))
                {
                    DateOnly? bis = last.Bis == null || c.Bis == null ? null
                        : (c.Bis.Value > last.Bis.Value ? c.Bis : last.Bis);
                    res[^1] = (last.Von, bis);
                    continue;
                }
            }
            res.Add(c);
        }
        return res;
    }

    private static void Add(List<Aufgabe> res, int emp, string domaene, string art, string grund, string text, DateOnly stichtag, string? zusatz)
    {
        if (res.Any(a => a.EmployeeId == emp && a.Domaene == domaene && a.Grund == grund && a.Stichtag == stichtag))
            return;
        res.Add(new Aufgabe(emp, domaene, art, grund, text, stichtag, zusatz));
    }
}

/// <summary>
/// Nachzahlung nach Austritt im Vorjahr (Systemtest 04_04_04, RL 9.6.5).
/// Jährlicher Charakter = Leistungsanspruch im Austritt: Verhältnisse des Austrittsmonats
/// und Hinweis auf den Lohnausweis dieses Jahres. Monatlicher Charakter = Verhältnisse
/// des Auszahlungsmonats. Gleicher Jahr wie der Austritt bleibt beim Auszahlungsmonat.
/// </summary>
public static class NachzahlungQstZeit
{
    public static bool LeistungsanspruchMitAustritt(bool nachAustritt, DateOnly? austritt, int auszahlungsjahr, bool nurJaehrlich) =>
        nachAustritt && nurJaehrlich && austritt != null && austritt.Value.Year < auszahlungsjahr;
}

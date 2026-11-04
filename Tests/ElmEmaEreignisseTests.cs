using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HrSystem.Services.Elm;
using HrSystem.Tests.Swissdec;
using Xunit;
using E = HrSystem.Services.Elm.ElmEmaEreignisse;

namespace HrSystem.Tests;

/// <summary>
/// EMA gegen die Referenz: alle Ereignisse aus SWISSCEC/RefXML/*_EMA.xml und die BVG-Basen der
/// PROSPECTIVE-Dateien, gerechnet aus den Testmandant-Exporten ohne Datenbank.
/// </summary>
public class ElmEmaEreignisseTests
{
    private static readonly string[] Monate = new[] { "2024-11", "2024-12" }
        .Concat(Enumerable.Range(1, 12).Select(m => $"2025-{m:00}"))
        .Concat(new[] { "2026-01", "2026-02" }).ToArray();

    private static DateOnly Erster(string monat) => DateOnly.ParseExact(monat + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Die vom Anwender ausgelösten Ereignisse nach RL 8.6.8.
    private static readonly Dictionary<string, E.Manuell[]> Manuell = new()
    {
        ["TF01"] = new[] { new E.Manuell(Erster("2025-03"), E.Withdrawal, "interruptionOfEmploymentNotInsured") },
        ["TF12"] = new[] { new E.Manuell(Erster("2025-10"), E.Mutation, "partialRetirement") },
        ["TF36"] = new[]
        {
            new E.Manuell(Erster("2025-09"), E.Mutation, "incapacityToWork"),
            new E.Manuell(Erster("2025-11"), E.Mutation, "fullyFitForWork", new DateOnly(2025, 11, 15)),
        },
        ["TF40"] = new[]
        {
            new E.Manuell(Erster("2025-03"), E.Withdrawal, "interruptionOfEmployment"),
            new E.Manuell(Erster("2025-10"), E.Withdrawal, "interruptionOfEmployment"),
        },
    };

    private static ElmBvgBasis.Vertrag Vertrag(TestmandantDaten.Person s) => new(
        StundenOderLektionenlohn: s.W("PersonContractHourly") != null,
        FestesPensum: (s.W("PersonAgreedWeeklyHours") ?? s.W("PersonAgreedWeeklyLessons")) != null && s.W("PersonActivityRateUnsteady") == null,
        Wochenstunden: s.D("PersonAgreedWeeklyHours"),
        Wochenlektionen: s.D("PersonAgreedWeeklyLessons"),
        Stundensatz: s.D("PersonContractHourlyWagePaidByHour"),
        Lektionensatz: s.D("PersonContractHourlyWagePaidByLesson"),
        Prozent13: s.D("PersonContractHourly13th"),
        Mit13: s.W("PersonContractMonthly13th") != null,
        Mit14: s.W("PersonContractMonthly14th") != null);

    private static char Art(TestmandantDaten.Person s)
        => s.W("PersonContractHourly") != null ? 'H' : s.W("PersonContractMonthly") != null ? 'M' : 'N';

    private static Dictionary<string, E.Person> LadePersonen()
    {
        var stamm = TestmandantDaten.LadePersonen();
        var mut = TestmandantDaten.LadeMutationen();
        var lohn = TestmandantDaten.LadeLohnarten();
        var jahr = new[] { "2025-01", "2026-01" }.ToDictionary(m => m, BasenAusProspective);
        var res = new Dictionary<string, E.Person>();
        foreach (var (tf, p) in stamm)
        {
            mut.TryGetValue(tf, out var mp);
            lohn.TryGetValue(tf, out var lp);
            var nr = int.Parse(tf[2..]).ToString();
            var monate = Monate.Select(m =>
            {
                var s = TestmandantDaten.StandPer(p, mp, m);
                return new E.Monatsstand(Erster(m), s.Datum("PersonEntryDate"), s.Datum("PersonWithdrawalDate"),
                    s.W("PersonWorkplace"), s.W("PersonBVGLPPInsured") == "1", s.W("PersonBVGLPPCode1"),
                    s.D("PersonBVGLPPManuallyBase"), Art(s), Vertrag(s),
                    lp != null && lp.TryGetValue(m, out var la) ? la : new Dictionary<string, decimal>());
            }).ToList();
            List<(DateOnly, DateOnly)> Wechsel(string tag) => (mp ?? new())
                .Where(x => x.Value.TryGetValue(tag, out var v) && !string.IsNullOrWhiteSpace(v))
                .Select(x => (TestmandantDaten.DatumVon(x.Value[tag])!.Value, Erster(x.Key))).ToList();
            var ende = TestmandantDaten.StandPer(p, mp, Monate[^1]);
            res[nr] = new E.Person(p.Datum("PersonDateOfBirth")!.Value, p.W("PersonSex"), ende.Datum("PersonDateOfDeath"),
                p.W("PersonAHVALVSpecialCase") != null, monate,
                Wechsel("PersonCivilStatusValidAsOf"), Wechsel("PersonDepartureDate"),
                Manuell.TryGetValue(tf, out var man) ? man : Array.Empty<E.Manuell>(),
                jahr.Where(j => j.Value.ContainsKey(nr)).ToDictionary(j => Erster(j.Key), j => j.Value[nr]));
        }
        return res;
    }

    private static string Datei(string name)
        => Path.Combine(TestmandantDaten.RepoRoot, "SWISSCEC", "RefXML", name);

    private static Dictionary<string, decimal> BasenAusProspective(string monat)
    {
        var res = new Dictionary<string, decimal>();
        foreach (var p in XDocument.Load(Datei($"RefXML_{monat}_PROSPECTIVE.xml")).Descendants().Where(x => x.Name.LocalName == "Person"))
        {
            var basis = p.Descendants().FirstOrDefault(x => x.Name.LocalName == "BVG-LPP-AnnualBasis");
            if (basis == null) continue;
            var nr = p.Descendants().First(x => x.Name.LocalName == "EmployeeNumber").Value;
            res[nr] = decimal.Parse(basis.Value, CultureInfo.InvariantCulture);
        }
        return res;
    }

    private static string Zeile(string monat, string nr, string domaene, string art, string ab, string? grund, string? code, decimal? basis, string? kanton)
        => $"{monat} #{nr} {domaene} {art} {ab} {grund} {code} {basis?.ToString("0.00", CultureInfo.InvariantCulture)} {kanton}";

    private static List<string> Referenz()
    {
        var res = new List<string>();
        foreach (var pfad in Directory.GetFiles(Path.Combine(TestmandantDaten.RepoRoot, "SWISSCEC", "RefXML"), "*_EMA.xml"))
        {
            var monat = Regex.Match(pfad, @"(\d{4}-\d{2})_EMA").Groups[1].Value;
            foreach (var p in XDocument.Load(pfad).Descendants().Where(x => x.Name.LocalName == "Person"))
            {
                var nr = p.Descendants().First(x => x.Name.LocalName == "EmployeeNumber").Value;
                foreach (var (name, dom) in new[] { ("AHV-AVS-ChangeNotification", E.Ahv), ("FAK-CAF-ChangeNotification", E.Fak), ("BVG-LPP-ChangeNotification", E.Bvg) })
                    foreach (var n in p.Descendants().Where(x => x.Name.LocalName == name))
                    {
                        var kopf = n.Elements().First(x => x.Name.LocalName is E.Entry or E.Mutation or E.Withdrawal);
                        string? Wert(XElement e, string t) => e.Elements().FirstOrDefault(x => x.Name.LocalName == t)?.Value;
                        var b = Wert(n, "BVG-LPP-AnnualBasis");
                        res.Add(Zeile(monat, nr, dom, kopf.Name.LocalName, Wert(kopf, "ValidAsOf")!, Wert(kopf, "Reason"),
                            Wert(n, "BVG-LPP-Code"), b == null ? null : decimal.Parse(b, CultureInfo.InvariantCulture), Wert(n, "FAK-CAF-WorkplaceCanton")));
                    }
            }
        }
        return res;
    }

    private static List<string> Rechnung() => LadePersonen()
        .SelectMany(kv => E.Erkenne(kv.Value, ElmBvgBasis.AusKatalog).Select(x => Zeile(
            x.Monat.ToString("yyyy-MM"), kv.Key, x.Domaene, x.Art, x.ValidAsOf.ToString("yyyy-MM-dd"),
            x.Grund, x.BvgCode, x.Basis, x.Kanton)))
        .ToList();

    [Fact]
    public void Alle_Ereignisse_und_Basen_wie_die_Referenz()
    {
        var soll = Referenz();
        var ist = Rechnung();
        Assert.Equal(291, soll.Count);
        var fehlt = soll.Except(ist).OrderBy(x => x).ToList();
        var zuviel = ist.Except(soll).OrderBy(x => x).ToList();
        Assert.True(fehlt.Count == 0 && zuviel.Count == 0,
            $"Fehlt:\n{string.Join("\n", fehlt)}\nZu viel:\n{string.Join("\n", zuviel)}");
        Assert.Equal(soll.Count, ist.Count);
    }

    [Fact]
    public void Reihenfolge_je_Person_und_Domaene_wie_die_Referenz()
    {
        static IEnumerable<string> Nach(IEnumerable<string> z) => z.GroupBy(x => string.Join(' ', x.Split(' ')[..3]))
            .OrderBy(g => g.Key).SelectMany(g => g);
        Assert.Equal(Nach(Referenz()), Nach(Rechnung()));
    }

    /// <summary>Die drei Blöcke je Person, Zeichen für Zeichen ohne Namespaces, Adressat und Work kommen aus der Referenz.</summary>
    [Fact]
    public void Xml_Bloecke_wie_die_Referenz()
    {
        var personen = LadePersonen();
        static string Flach(XElement x) => string.Concat(x.DescendantsAndSelf().Select(d =>
            $"<{d.Name.LocalName} {string.Join(",", d.Attributes().Where(a => a.Name.LocalName is not "workIDRef").OrderBy(a => a.Name.LocalName).Select(a => a.Value))}>{(d.HasElements ? "" : d.Value)}"));
        int verglichen = 0;
        foreach (var pfad in Directory.GetFiles(Path.Combine(TestmandantDaten.RepoRoot, "SWISSCEC", "RefXML"), "*_EMA.xml"))
        {
            var monat = Erster(Regex.Match(pfad, @"(\d{4}-\d{2})_EMA").Groups[1].Value);
            foreach (var r in XDocument.Load(pfad).Descendants().Where(x => x.Name.LocalName == "Person"))
            {
                var nr = r.Descendants().First(x => x.Name.LocalName == "EmployeeNumber").Value;
                var soll = r.Elements().Where(x => x.Name.LocalName.EndsWith("-ChangeNotifications")).ToList();
                if (soll.Count == 0) continue;
                var ereignisse = E.Erkenne(personen[nr], ElmBvgBasis.AusKatalog, monat);
                var ahvId = soll.FirstOrDefault(s => s.Name.LocalName.StartsWith("AHV"))?.Elements().First().Attribute("addresseeIDRef")!.Value;
                var fakJeKanton = soll.FirstOrDefault(s => s.Name.LocalName.StartsWith("FAK"))?.Elements()
                    .GroupBy(x => x.Elements().Last().Value).ToDictionary(g => g.Key, g => g.First().Attribute("addresseeIDRef")!.Value);
                var ist = E.Bloecke(ereignisse,
                    e => e.Domaene == E.Ahv ? ahvId! : e.Domaene == E.Fak ? fakJeKanton![e.Kanton!] : "#BVG",
                    _ => "#w", e => e.Grund != "deceased").ToList();
                Assert.Equal(string.Concat(soll.Select(Flach)), string.Concat(ist.Select(Flach)));
                verglichen += soll.Sum(s => s.Elements().Count());
            }
        }
        // 2 Ereignisse weniger wären ein stiller Verlust: alle 291 müssen durch den Vergleich.
        Assert.Equal(291, verglichen);
    }

    [Fact]
    public void Ohne_freigegebene_Jahresmeldung_rechnet_der_Januar_selbst()
    {
        // Auf der Testinstanz ist keine Prospective freigegeben (TF37 Oberli Januar 2025: keine Lohnänderung).
        var ist = LadePersonen().SelectMany(kv => E.Erkenne(kv.Value with { Jahresmeldungen = new Dictionary<DateOnly, decimal>() }, ElmBvgBasis.AusKatalog)
            .Select(x => Zeile(x.Monat.ToString("yyyy-MM"), kv.Key, x.Domaene, x.Art, x.ValidAsOf.ToString("yyyy-MM-dd"), x.Grund, x.BvgCode, x.Basis, x.Kanton))).ToList();
        var soll = Referenz();
        Assert.Empty(soll.Except(ist));
        Assert.Empty(ist.Except(soll));
    }

    private static HrSystem.Models.Employment V(string von, string? bis, string? art = null) => new()
    {
        ContractStartDate = DateTime.Parse(von, CultureInfo.InvariantCulture),
        ContractEndDate = bis == null ? null : DateTime.Parse(bis, CultureInfo.InvariantCulture),
        EndeArt = art,
    };

    [Fact]
    public void Unterbruch_am_Vertrag_ergibt_den_Austrittsgrund()
    {
        var u = ElmEmaBuilder.UnterbruecheAusVertraegen(new[]
        {
            V("2025-01-01", "2025-03-31", "interruptionOfEmployment"),
            V("2025-10-01", "2025-10-31"),
        }).ToList();
        var z = Assert.Single(u);
        Assert.Equal((new DateOnly(2025, 3, 1), E.Withdrawal, "interruptionOfEmployment"), (z.Monat, z.Art, z.Grund));
    }

    [Fact]
    public void Vertragswechsel_ohne_Luecke_ist_kein_Unterbruch()
    {
        // Andere Funktion, nahtloser Folgevertrag: Eine vergessene Angabe darf keine Meldung auslösen.
        Assert.Empty(ElmEmaBuilder.UnterbruecheAusVertraegen(new[]
        {
            V("2025-01-01", "2025-03-31", "interruptionOfEmployment"),
            V("2025-04-01", null),
        }));
    }

    [Fact]
    public void Teilpensionierung_am_Vertrag_wirkt_ab_Vertragsbeginn()
    {
        var neu = V("2025-10-01", "2025-12-31");
        neu.Teilpensionierung = true;
        var z = Assert.Single(ElmEmaBuilder.TeilpensionierungenAusVertraegen(new[] { V("2025-02-27", "2025-09-30"), neu }));
        Assert.Equal((new DateOnly(2025, 10, 1), E.Mutation, "partialRetirement", (DateOnly?)new DateOnly(2025, 10, 1)), (z.Monat, z.Art, z.Grund, z.ValidAsOf));
    }

    [Fact]
    public void Todesfall_am_Vertrag()
    {
        var e = new HrSystem.Models.Employee();
        Assert.Equal(new DateOnly(2025, 10, 30), ElmEmaBuilder.Todestag(e, new[] { V("2025-02-01", "2025-10-30", HrSystem.Models.Employment.EndeTodesfall) }));
        Assert.Null(ElmEmaBuilder.Todestag(e, new[] { V("2025-02-01", "2025-10-30") }));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("interruptionOfEmploymentOnlyRisk", true)]
    [InlineData("deceased", true)]
    [InlineData("retirement", false)]
    public void Art_des_Endes_wird_geprueft(string? art, bool gueltig)
        => Assert.Equal(gueltig, HrSystem.Models.Employment.EndeArtGueltig(art));

    [Fact]
    public void Monatslohn_aus_dem_Vertrag_nicht_anteilig()
    {
        var em = new HrSystem.Models.Employment { EmploymentModel = "FIX", MonthlySalary = 12000m };
        var lohn = ElmEmaBuilder.MitVertragslohn(new Dictionary<string, decimal> { ["1000"] = 8400m, ["1033"] = 200m }, em);
        Assert.Equal(12000m, lohn["1000"]);
        Assert.Equal(200m, lohn["1033"]);
    }

    [Fact]
    public void Dreizehnter_im_Stundenlohn_zaehlt_in_die_Rueckschau()
    {
        var em = new HrSystem.Models.Employment { EmploymentModel = "FLEX" };
        var lohn = ElmEmaBuilder.MitVertragslohn(new Dictionary<string, decimal> { ["1200"] = 204.75m, ["1201"] = 100m }, em);
        Assert.Equal(304.75m, lohn["1201"]);
        Assert.False(lohn.ContainsKey("1200"));
    }

    [Fact]
    public void Festes_Pensum_im_Stundenlohn_aus_den_Wochenstunden()
    {
        var blanc = new HrSystem.Models.Employment { EmploymentModel = "FLEX", WeeklyHours = 8.4m, HourlyRate = 30m, ThirteenthSalary = false };
        var v = ElmEmaBuilder.Vertrag(blanc, 8.33m);
        Assert.True(v.FestesPensum);
        Assert.Equal(13104m, ElmBvgBasis.Voraussichtlich(v, new Dictionary<string, decimal>(), ElmBvgBasis.AusKatalog));
        Assert.False(ElmEmaBuilder.Vertrag(new HrSystem.Models.Employment { EmploymentModel = "FLEX", HourlyRate = 30m }, 8.33m).FestesPensum);
    }

    [Theory]
    [InlineData("2025-01")]
    [InlineData("2026-01")]
    public void Prospective_Bestand_und_Basen_wie_die_Referenz(string monat)
    {
        var stichtag = Erster(monat);
        var soll = BasenAusProspective(monat);
        var ist = new Dictionary<string, decimal>();
        foreach (var (nr, p) in LadePersonen())
        {
            // Die Referenz-Basen selbst nicht als Eingabe verwenden.
            var ohne = p with { Jahresmeldungen = new Dictionary<DateOnly, decimal>() };
            var ereignisse = E.Erkenne(ohne, ElmBvgBasis.AusKatalog);
            if (E.BvgPeriodeAm(ereignisse, stichtag) is not { } ab) continue;
            var s = p.Monate.First(m => m.Monat == stichtag);
            var laeufe = p.Monate.Select(m => new ElmBvgBasis.LohnMonat(m.Monat, m.Lohnarten));
            ist[nr] = ElmBvgBasis.FuerJahresmeldung(stichtag, s.ManuelleBasis, s.Vertrag, s.Lohnarten, laeufe, ab, ElmBvgBasis.AusKatalog);
        }
        Assert.Equal(soll.OrderBy(x => x.Key), ist.OrderBy(x => x.Key));
    }

    [Theory]
    [InlineData("1960-12-16", "F", "2024-12-16")]  // Burri
    [InlineData("1961-06-30", "F", "2025-09-30")]  // Paganini, AHV 21: 64 + 3 Monate
    [InlineData("1961-02-05", "F", "2025-05-05")]  // Pia Lusser
    [InlineData("1960-02-05", "M", "2025-02-05")]  // Hans Lusser
    [InlineData("1964-01-10", "F", "2029-01-10")]
    public void Referenzdatum_mit_AHV21(string geburt, string sex, string soll)
        => Assert.Equal(DateOnly.Parse(soll, CultureInfo.InvariantCulture), E.Referenzdatum(DateOnly.Parse(geburt, CultureInfo.InvariantCulture), sex));

    [Theory]
    [InlineData("2025-03-26", "2025-04-01")]
    [InlineData("2025-08-31", "2025-09-01")]
    [InlineData("2025-09-01", "2025-09-01")]
    public void Wirkung_am_Ersten(string d, string soll)
        => Assert.Equal(DateOnly.Parse(soll, CultureInfo.InvariantCulture), E.ErsterFolgemonat(DateOnly.Parse(d, CultureInfo.InvariantCulture)));

    [Fact]
    public void Basis_Lamon_voraussichtlich_plus_rueckwirkend()
    {
        // 32 × 50 × 52 × 1.0833 + Schichtzulage 329 × 360 / 270
        var v = new ElmBvgBasis.Vertrag(true, true, 32m, null, 50m, 50m, 8.33m, false, false);
        var voraus = ElmBvgBasis.Voraussichtlich(v, new Dictionary<string, decimal>(), ElmBvgBasis.AusKatalog);
        Assert.Equal(90130.56m, voraus);
        var laeufe = new[] { new ElmBvgBasis.LohnMonat(new DateOnly(2025, 3, 1), new Dictionary<string, decimal> { ["1070"] = 329m, ["1160"] = 700m }) };
        var rueck = ElmBvgBasis.Rueckwirkend(true, laeufe, new DateOnly(2025, 9, 1), new DateOnly(2025, 1, 1), ElmBvgBasis.AusKatalog);
        Assert.Equal(90569.25m, ElmBvgBasis.Runde5Rappen(voraus + rueck));
    }

    [Fact]
    public void Basis_ohne_festes_Pensum_zaehlt_alle_Gruppen_auf_360_Tage()
    {
        var laeufe = new[] { new ElmBvgBasis.LohnMonat(new DateOnly(2025, 1, 1), new Dictionary<string, decimal> { ["1005"] = 1000m, ["1160"] = 100m, ["2065"] = 500m, ["1000"] = 9999m }) };
        var r = ElmBvgBasis.Rueckwirkend(false, laeufe, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 1), ElmBvgBasis.AusKatalog);
        Assert.Equal(1600m * 12m, r);
    }
}

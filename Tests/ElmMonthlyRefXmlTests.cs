using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Der Feld-für-Feld-Vergleich einer Monatsmeldung gegen die Swissdec-Referenz
/// (Walter 27.09.2026). Getestet wird das WERKZEUG — die eigentliche Meldung
/// entsteht aus der Datenbank der Testinstanz und wird dort im Meldungs-Cockpit
/// gegen `SWISSCEC/RefXML/RefXML_202411_MONTHLY.xml` gehalten.
/// </summary>
public class ElmXmlVergleichTests
{
    private const string Kopf =
        """
        <sdcst:DeclareMonthlySalary xmlns:ep="urn:ch:swissdec:basis:v1:20260306:components"
            xmlns:sdc="urn:ch:swissdec:elm:v6:20260306:salarydeclaration:container"
            xmlns:c="urn:ch:swissdec:common:v3:20260306"
            xmlns:sd="urn:ch:swissdec:elm:v6:20260306:salarydeclaration"
            xmlns:sdcst="urn:ch:swissdec:elm:v6:20260306:salarydeclaration:service:types">
          <sd:MonthlySalaryDeclaration>
        """;
    private const string Fuss = "</sd:MonthlySalaryDeclaration></sdcst:DeclareMonthlySalary>";

    private static string Meldung(string persNr, string lohn, string sozial = "-640.50")
        => Kopf + $"""
          <sd:Staff>
            <sd:Person>
              <c:Particulars>
                <c:EmployeeNumber>{persNr}</c:EmployeeNumber>
                <c:Lastname>Burri</c:Lastname>
                <c:Firstname>Heidi</c:Firstname>
              </c:Particulars>
              <sd:StatisticSalaries>
                <sd:StatisticSalary>
                  <sd:MonthlyValues>
                    <sd:GrossBaseSalaryAndRegularAllowance>{lohn}</sd:GrossBaseSalaryAndRegularAllowance>
                    <sd:SocialContributions>{sozial}</sd:SocialContributions>
                  </sd:MonthlyValues>
                </sd:StatisticSalary>
              </sd:StatisticSalaries>
            </sd:Person>
          </sd:Staff>
        """ + Fuss;

    [Fact]
    public void GleicheMeldung_KeinUnterschied()
    {
        var e = ElmXmlVergleich.Vergleiche(Meldung("7", "8000.00"), Meldung("7", "8000.00"));
        Assert.Empty(e.Unterschiede);
        Assert.True(e.Geprueft > 0);
    }

    [Fact]
    public void AbweichenderBetrag_IstOffen()
    {
        var e = ElmXmlVergleich.Vergleiche(Meldung("7", "8000.00"), Meldung("7", "8100.00"));
        var u = Assert.Single(e.Unterschiede);
        Assert.Equal("8000.00", u.Ist);
        Assert.Equal("8100.00", u.Soll);
        Assert.False(u.IstBewusst);
        Assert.Equal(1, e.Offen);
    }

    [Fact]
    public void SozialabgabenBisFuenfRappen_SindBewusst_A5()
    {
        // A5 im Abweichungsprotokoll: die Statistiksumme der Referenz ist auf
        // 5 Rappen gerundet, unsere Belege sind rappengenau.
        var e = ElmXmlVergleich.Vergleiche(Meldung("7", "8000.00", "-640.48"), Meldung("7", "8000.00", "-640.50"));
        var u = Assert.Single(e.Unterschiede);
        Assert.True(u.IstBewusst);
        Assert.StartsWith("A5", u.Bewusst);
        Assert.Equal(0, e.Offen);
    }

    [Fact]
    public void GroessereAbweichungBeiSozialabgaben_BleibtOffen()
    {
        var e = ElmXmlVergleich.Vergleiche(Meldung("7", "8000.00", "-600.00"), Meldung("7", "8000.00", "-640.50"));
        Assert.Equal(1, e.Offen);
    }

    [Fact]
    public void PersonenWerdenUeberDiePersonalnummerZugeordnet()
    {
        // Andere Reihenfolge darf keinen Unterschied ergeben; eine fehlende Person schon.
        var e = ElmXmlVergleich.Vergleiche(Meldung("7", "8000.00"), Meldung("44", "8000.00"));
        Assert.Equal(2, e.Unterschiede.Count);
        Assert.Contains(e.Unterschiede, u => u.Ist == "vorhanden" && u.Soll == "fehlt");
        Assert.Contains(e.Unterschiede, u => u.Ist == "fehlt" && u.Soll == "vorhanden");
    }

    [Fact]
    public void BerichtNenntZahlenUndFelder()
    {
        var e = ElmXmlVergleich.Vergleiche(Meldung("7", "8000.00"), Meldung("7", "8100.00"));
        var md = ElmXmlVergleich.Bericht(e, "ELM-Monatsmeldung 2024-11", "RefXML_202411_MONTHLY.xml");
        Assert.Contains("RefXML_202411_MONTHLY.xml", md);
        Assert.Contains("8100.00", md);
        Assert.Contains("**offen**", md);
    }
}

/// <summary>
/// Arbeitsorte der Monatsmeldung (Walter 28.09.2026). Das Quality Tool erwartet im
/// November 2024 nur #LU und #BE — die Filialen, an denen jemand Lohn hat —, nicht
/// alle sechs der Muster AG.
/// </summary>
public class ElmMonatsArbeitsorteTests
{
    private static HrSystem.Models.CompanyProfile Filiale(int id, string kanton) => new()
    {
        Id = id, RestaurantCode = kanton, BranchName = kanton, KantonCode = kanton, ZipCode = "6003", City = "Luzern"
    };

    private static ElmGemeinsam.RechtseinheitStamm MusterAg()
    {
        var filialen = new List<HrSystem.Models.CompanyProfile>
        {
            Filiale(1, "LU"), Filiale(2, "BE"), Filiale(3, "VD"), Filiale(4, "TI"), Filiale(5, "AG"), Filiale(6, "ZG"),
        };
        return new ElmGemeinsam.RechtseinheitStamm(null, filialen, filialen[0], "CHE-999.999.996",
            "Muster AG", "Bahnhofstrasse 1", "6003", "Luzern", new Dictionary<int, int>());
    }

    [Fact]
    public void NurFilialenMitLohn_InDerFirmenbeschreibung()
    {
        var stamm = ElmMonthlyDeclarationBuilder.NurArbeitsorteMitLohn(MusterAg(), new HashSet<int> { 1, 2 });
        var firma = ElmGemeinsam.CompanyDescription(stamm, Array.Empty<System.Xml.Linq.XElement>());

        var ids = firma.Elements().Where(x => x.Name.LocalName == "Workplace")
            .Select(x => (string?)x.Attribute("workplaceID")).ToList();
        Assert.Equal(new[] { "#LU", "#BE" }, ids);
    }

    [Fact]
    public void Reihenfolge_BleibtWieInDenStammdaten()
    {
        var stamm = ElmMonthlyDeclarationBuilder.NurArbeitsorteMitLohn(MusterAg(), new HashSet<int> { 6, 2 });
        Assert.Equal(new[] { 2, 6 }, stamm.Filialen.Select(b => b.Id));
    }

    [Fact]
    public void Rest_DerRechtseinheit_BleibtUnveraendert()
    {
        var voll = MusterAg();
        var stamm = ElmMonthlyDeclarationBuilder.NurArbeitsorteMitLohn(voll, new HashSet<int> { 2 });
        Assert.Same(voll.Haupt, stamm.Haupt);
        Assert.Equal("CHE-999.999.996", stamm.Uid);
        Assert.Equal(6, voll.Filialen.Count);
    }
}

/// <summary>
/// Keine ungerundeten Beträge in der Meldung (Walter 28.09.2026, Swissdec-Berater) —
/// auch BVG geht auf 5 Rappen; Quality Tool TF16 Aebi Nov 2024 erwartet −758.35.
/// </summary>
public class ElmBvgRundungTests
{
    [Theory]
    [InlineData(-758.33, "-758.35")]   // TF16 Aebi, CSV 5050 = 758.33
    [InlineData(-408.33, "-408.35")]
    [InlineData(-751.09, "-751.10")]
    [InlineData(-640.00, "-640.00")]
    public void BvgAufFuenfRappen(decimal beleg, string gemeldet)
        => Assert.Equal(gemeldet, ElmGemeinsam.Betrag05(beleg));
}

/// <summary>
/// Quality Tool Dezember 2024 (Walter 28.09.2026): Jahreswerte kumuliert ab Beginn der
/// Anstellung im Jahr, Austritt im Monat, Bewilligung zum Monatsende.
/// </summary>
public class ElmMonatsJahreswerteTests
{
    private static HrSystem.Models.Employment Ab(string von, string? bis) => new()
    {
        ContractStartDate = DateTime.Parse(von),
        ContractEndDate = bis == null ? null : DateTime.Parse(bis),
    };

    private static readonly DateTime DezAnfang = new(2024, 12, 1);
    private static readonly DateTime DezEnde = new(2024, 12, 31);

    [Fact]
    public void Aebi_AustrittImDezember_WiedereintrittIstNeueKette()
    {
        var a = ElmMonthlyDeclarationBuilder.Anstellung(
            new[] { Ab("2024-11-01", "2024-12-20"), Ab("2025-01-15", "2025-03-27") }, DezAnfang, DezEnde);
        Assert.Equal(new DateTime(2024, 11, 1), a!.Value.Start);
        Assert.Equal(new DateTime(2024, 12, 20), a.Value.Ende);
    }

    [Fact]
    public void Aebi_November_AustrittLiegtNachDemMonat()
    {
        var a = ElmMonthlyDeclarationBuilder.Anstellung(
            new[] { Ab("2024-11-01", "2024-12-20") }, new DateTime(2024, 11, 1), new DateTime(2024, 11, 30));
        Assert.Equal(new DateTime(2024, 12, 20), a!.Value.Ende);   // Aufrufer meldet nur Austritte im Monat
    }

    [Fact]
    public void NahtloserWechsel_IstKeinAustritt()
    {
        var a = ElmMonthlyDeclarationBuilder.Anstellung(
            new[] { Ab("2024-03-01", "2024-06-30"), Ab("2024-07-01", "2024-12-31"), Ab("2025-01-01", null) },
            DezAnfang, DezEnde);
        Assert.Equal(new DateTime(2024, 3, 1), a!.Value.Start);
        Assert.Null(a.Value.Ende);
    }

    [Fact]
    public void Egli_BewilligungC_AbDezember_NovemberNochB()
    {
        var hist = new[]
        {
            new HrSystem.Models.EmployeePermitHistory { Id = 1, PermitTypeId = 2, ValidFrom = new DateOnly(2024, 11, 1), ValidTo = new DateOnly(2024, 11, 30) },
            new HrSystem.Models.EmployeePermitHistory { Id = 2, PermitTypeId = 3, ValidFrom = new DateOnly(2024, 12, 1), ErfahrenAm = new DateOnly(2024, 12, 1) },
        };
        Assert.Equal(2, ElmMonthlyDeclarationBuilder.BewilligungAmStichtag(hist, new DateOnly(2024, 11, 30)));
        Assert.Equal(3, ElmMonthlyDeclarationBuilder.BewilligungAmStichtag(hist, new DateOnly(2024, 12, 31)));
    }

    [Fact]
    public void Bewilligung_ErstSpaeterErfahren_ZaehltNochNicht()
    {
        var hist = new[]
        {
            new HrSystem.Models.EmployeePermitHistory { Id = 1, PermitTypeId = 2, ValidFrom = new DateOnly(2024, 1, 1) },
            new HrSystem.Models.EmployeePermitHistory { Id = 2, PermitTypeId = 3, ValidFrom = new DateOnly(2024, 11, 1), ErfahrenAm = new DateOnly(2024, 12, 5) },
        };
        Assert.Equal(2, ElmMonthlyDeclarationBuilder.BewilligungAmStichtag(hist, new DateOnly(2024, 11, 30)));
    }

    [Fact]
    public void Egli_13ML_KumuliertNovemberPlusDezember()
    {
        var map = new Dictionary<string, string>();
        static System.Text.Json.JsonElement Slip(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement;
        var nov = ElmMonthlyDeclarationBuilder.Toepfe(Slip("""{"lohnLines":[{"code":"1005","betrag":1875.0},{"code":"1201","betrag":175.45}]}"""), map, null);
        var dez = ElmMonthlyDeclarationBuilder.Toepfe(Slip("""{"lohnLines":[{"code":"1005","betrag":3250.0},{"code":"1201","betrag":304.10},{"code":"6000","betrag":200.0}]}"""), map, null);
        var summe = nov[ElmStatistikCodes.Topf.Dreizehnter] + dez[ElmStatistikCodes.Topf.Dreizehnter];
        Assert.Equal("479.55", ElmGemeinsam.Betrag05(summe));
    }
}

/// <summary>Konfessions-Zuordnung der Quellensteuer-Zeile.</summary>
public class ElmKonfessionTests
{
    // Werte laut Schema (DenominationType): romanCatholic, christianCatholic,
    // reformedEvangelical, jewishCommunity, otherOrNone.
    [Theory]
    [InlineData("roemisch_katholisch", "romanCatholic")]
    [InlineData("römisch-katholisch", "romanCatholic")]
    [InlineData("reformiert", "reformedEvangelical")]
    [InlineData("evangelisch-reformiert", "reformedEvangelical")]
    [InlineData("christkatholisch", "christianCatholic")]
    [InlineData("juedisch", "jewishCommunity")]
    [InlineData("konfessionslos", "otherOrNone")]
    public void BekannteKonfessionen(string quelle, string erwartet)
        => Assert.Equal(erwartet, ElmMonthlyDeclarationBuilder.MapKonfession(quelle));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("irgendwas")]
    public void UnbekanntGibtKeinFeld(string? quelle)
        => Assert.Null(ElmMonthlyDeclarationBuilder.MapKonfession(quelle));
}

/// <summary>
/// BUR-/REE-Prüfziffer (Walter 27.09.2026). Anlass: die Muster-Filiale ZG trägt in
/// den Testdaten A38197421 (Prüfziffer falsch) und PLZ 6003 statt 6300; die Referenz
/// erwartet A38197423. Swissdec weist so etwas zurück — OneCrew soll es vorher sagen.
/// </summary>
public class ElmBurPruefzifferTests
{
    [Theory]
    [InlineData("A92978109")]   // Hauptsitz Luzern
    [InlineData("A89058593")]   // Werkhof/Büro Bern
    [InlineData("A89058588")]   // Verkauf Vevey
    [InlineData("A38197423")]   // Zug, wie die Referenz sie erwartet
    [InlineData("A63837147")]   // Schaub-Beispiel aus dem Protokoll
    public void EchteNummernGehenDurch(string bur)
        => Assert.True(ElmStammdatenPruefung.BurNummerGueltig(bur));

    [Theory]
    [InlineData("A38197421")]   // Zug aus den Testdaten — Prüfziffer falsch
    [InlineData("A92978100")]
    public void FalschePruefzifferFaelltDurch(string bur)
    {
        Assert.False(ElmStammdatenPruefung.BurNummerGueltig(bur));
        Assert.True(ElmStammdatenPruefung.BurFormatOkPruefzifferFalsch(bur));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("92978109")]     // ohne Buchstabe
    [InlineData("A9297810")]     // zu kurz
    [InlineData("AA2978109")]
    public void FalschesFormatFaelltDurch(string? bur)
    {
        Assert.False(ElmStammdatenPruefung.BurNummerGueltig(bur));
        Assert.False(ElmStammdatenPruefung.BurFormatOkPruefzifferFalsch(bur));
    }
}

/// <summary>
/// MonitoringID im Meldungskopf (Walter 27.09.2026). Auf den Swissdec-Testsystemen
/// zwingend, auf der Produktion muss sie fehlen. Sie steht als LETZTES Element im
/// RequestContext — in JEDER Meldung (Monat, Jahr, EMA).
/// </summary>
public class ElmMonitoringIdTests
{
    private static ElmEinstellungen Mit(string? wert) => new(new MiniConfig(wert));

    /// <summary>Minimale Konfiguration — nur der eine Schlüssel, ohne Zusatzpaket.</summary>
    private sealed class MiniConfig : Microsoft.Extensions.Configuration.IConfiguration
    {
        private readonly string? _wert;
        public MiniConfig(string? wert) => _wert = wert;
        public string? this[string key]
        {
            get => key == "Swissdec:MonitoringId" ? _wert : null;
            set { }
        }
        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() =>
            Array.Empty<Microsoft.Extensions.Configuration.IConfigurationSection>();
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => new NoopToken();
        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) =>
            throw new NotSupportedException();

        private sealed class NoopToken : Microsoft.Extensions.Primitives.IChangeToken
        {
            public bool ActiveChangeCallbacks => false;
            public bool HasChanged => false;
            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => new Noop();
            private sealed class Noop : IDisposable { public void Dispose() { } }
        }
    }

    [Fact]
    public void OhneId_FehltDasElement()
    {
        var ctx = ElmGemeinsam.RequestContext("Muster AG", new DateTime(2026, 9, 27), Mit(null));
        Assert.DoesNotContain(ctx.Elements(), x => x.Name.LocalName == "MonitoringID");
    }

    [Fact]
    public void OhneEinstellungen_FehltDasElement()
    {
        var ctx = ElmGemeinsam.RequestContext("Muster AG", new DateTime(2026, 9, 27));
        Assert.DoesNotContain(ctx.Elements(), x => x.Name.LocalName == "MonitoringID");
    }

    [Fact]
    public void MitId_StehtAlsLetztesElement()
    {
        var ctx = ElmGemeinsam.RequestContext("Muster AG", new DateTime(2026, 9, 27), Mit("onecrew-test-42"));
        var letztes = ctx.Elements().Last();
        Assert.Equal("MonitoringID", letztes.Name.LocalName);
        Assert.Equal("onecrew-test-42", letztes.Value);
    }

    [Theory]
    [InlineData("  abc  ", "abc")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void WertWirdBereinigt(string roh, string? erwartet)
        => Assert.Equal(erwartet, ElmEinstellungen.Bereinige(roh));

    [Fact]
    public void ZuLangWirdAufTzweiunddreissigGekuerzt()
    {
        var lang = new string('x', 40);
        var kurz = ElmEinstellungen.Bereinige(lang);
        Assert.Equal(32, kurz!.Length);
    }
}

/// <summary>
/// Ferienanspruch in der Statistik (Walter 27.09.2026). Richtlinien ELM 6.0, S. 366:
/// Ferienprozent und Ferientage werden NIE zusammen gemeldet. Beim Stunden- oder
/// Lektionenlohn steht der Prozentsatz im Block ContractualHourlyWage — dann sind
/// die Tage zwingend 0 (TF14 Egli, November 2024).
/// </summary>
public class ElmFerientageTests
{
    [Fact]
    public void Stundenlohn_MitFerienprozent_ImmerNull()
    {
        Assert.Equal(0m, ElmStatistikCodes.Ferientage(true, 5, null));
        // Auch eine Handeingabe darf das nicht aushebeln — sonst stuenden beide
        // Angaben in der Meldung.
        Assert.Equal(0m, ElmStatistikCodes.Ferientage(true, 5, 25m));
    }

    [Fact]
    public void Monatslohn_AusFerienwochen()
    {
        Assert.Equal(25m, ElmStatistikCodes.Ferientage(false, 5, null));
        Assert.Equal(30m, ElmStatistikCodes.Ferientage(false, 6, null));
    }

    [Fact]
    public void Monatslohn_HandeingabeSticht()
        => Assert.Equal(22m, ElmStatistikCodes.Ferientage(false, 5, 22m));
}

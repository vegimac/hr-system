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

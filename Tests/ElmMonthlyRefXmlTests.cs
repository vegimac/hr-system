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
    [Theory]
    [InlineData("roemisch_katholisch", "romanCatholic")]
    [InlineData("römisch-katholisch", "romanCatholic")]
    [InlineData("reformiert", "protestant")]
    [InlineData("evangelisch-reformiert", "protestant")]
    [InlineData("christkatholisch", "christCatholic")]
    [InlineData("konfessionslos", "other")]
    public void BekannteKonfessionen(string quelle, string erwartet)
        => Assert.Equal(erwartet, ElmMonthlyDeclarationBuilder.MapKonfession(quelle));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("irgendwas")]
    public void UnbekanntGibtKeinFeld(string? quelle)
        => Assert.Null(ElmMonthlyDeclarationBuilder.MapKonfession(quelle));
}

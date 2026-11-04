using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>Beträge des Lohnausweises aus den Lohnzetteln (Referenz Dezember 2024/2025).</summary>
public class ElmLohnausweisJahrTests
{
    private static (string? La, string? Name) Lp(string code) => code switch
    {
        "1212" => ("Q", "Sonderzulage"),
        "1230" => ("Q", "Dienstaltersgeschenke"),
        "1977" => ("T", "Vom AG übern. Säule 3 b"),
        _ => (null, null),
    };

    [Theory]
    [InlineData("1000", null, ElmLohnausweisJahr.Feld.Lohn)]
    [InlineData("1900", "R", ElmLohnausweisJahr.Feld.Verpflegung)]
    [InlineData("1902", "R", ElmLohnausweisJahr.Feld.Nebenleistung)]
    [InlineData("1910", "R", ElmLohnausweisJahr.Feld.Geschaeftswagen)]
    [InlineData("1962", "R", ElmLohnausweisJahr.Feld.Beteiligung)]
    [InlineData("1212", "Q", ElmLohnausweisJahr.Feld.Unregelmaessig)]
    [InlineData("1500", "Q", ElmLohnausweisJahr.Feld.Verwaltungsrat)]
    [InlineData("1420", "S", ElmLohnausweisJahr.Feld.Kapital)]
    [InlineData("1977", "T", ElmLohnausweisJahr.Feld.Uebrige)]
    [InlineData("3034", "5", ElmLohnausweisJahr.Feld.Unregelmaessig)]
    [InlineData("6050", null, ElmLohnausweisJahr.Feld.SpesenPauschalWagen)]
    [InlineData("5200", null, ElmLohnausweisJahr.Feld.Keine)]
    public void Felder(string code, string? la, ElmLohnausweisJahr.Feld soll)
        => Assert.Equal(soll, ElmLohnausweisJahr.FeldFuer(code, la));

    [Fact]
    public void Summen_und_Texte()
    {
        var nov = """{"lohnLines":[{"code":"1000","betrag":8000},{"code":"1212","betrag":3000}],"abzugLines":[{"categoryCode":"AHV","betrag":-424},{"categoryCode":"BVG","betrag":-640},{"categoryCode":"QST","betrag":-100}]}""";
        var dez = """{"lohnLines":[{"code":"1000","betrag":8000},{"code":"1230","betrag":1000}],"zulagenExtraLines":[{"code":"6050","betrag":800}],"abzugLines":[{"categoryCode":"ALV","betrag":-88},{"categoryCode":"QST_KORR","betrag":50}]}""";
        var a = ElmLohnausweisJahr.Rechne(new[] { nov, dez }, Lp);
        Assert.Equal(16000m, a.Von(ElmLohnausweisJahr.Feld.Lohn));
        Assert.Equal(4000m, a.Von(ElmLohnausweisJahr.Feld.Unregelmaessig));
        Assert.Equal("Sonderzulage, Dienstaltersgeschenke", a.TextVon(ElmLohnausweisJahr.Feld.Unregelmaessig));
        Assert.Equal(800m, a.Von(ElmLohnausweisJahr.Feld.SpesenPauschalWagen));
        Assert.Equal(20000m, a.Brutto);        // Spesen zählen nicht zum Brutto
        Assert.Equal(512m, a.Beitraege);
        Assert.Equal(640m, a.Bvg);
        Assert.Equal(18848m, a.Netto);
        Assert.Equal(50m, a.Quellensteuer);    // 100 − 50 Rückerstattung
    }
}

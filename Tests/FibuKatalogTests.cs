using System.Reflection;
using HrSystem.Services;
using HrSystem.Services.LohnFormelwerk;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Der Fibu-Katalog verwendet dieselben Nummern wie die Lohnpositionen.
/// </summary>
public class FibuKatalogTests
{
    [Fact]
    public void Jede_LohnCode_Konstante_ist_zugeordnet_oder_ohne_Buchung()
    {
        var codes = typeof(LohnCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var offen = codes.Where(c => !FibuKatalog.Kennt(c) && !FibuKatalog.OhneBuchung(c)).ToList();
        Assert.True(offen.Count == 0, "ohne Fibu-Zeile: " + string.Join(", ", offen));
    }

    [Fact]
    public void Lohn_bucht_Aufwand_vom_Vertrag_an_1920()
    {
        foreach (var code in new[] { "1000", "1005", "1061", "1065", "1076", "1160", "1161", "1166", "1168", "1200", "1300", "1301" })
        {
            var k = FibuKatalog.Fuer(code);
            Assert.NotNull(k);
            Assert.Equal(FibuKatalog.VomVertrag, k.Value.Soll);
            Assert.Equal("1920", k.Value.Haben);
        }
    }

    [Theory]
    [InlineData("2030", "2014", "1920")]
    [InlineData("2035", "2014", "1920")]
    [InlineData("2001", "2014", "1920")]
    [InlineData("3000", "2071", "1920")]
    [InlineData("3010", "2071", "1920")]
    [InlineData("3034", "2071", "1920")]
    [InlineData("6070", "4095", "1920")]
    [InlineData("5010", "1920", "2070")]
    [InlineData("5100", "1920", "2010")]
    [InlineData("5050", "1920", "2080")]
    [InlineData("600.24", "1920", "2023")]
    [InlineData("600.32", "1920", "2021")]
    [InlineData("950.1", "1920", "2050")]
    public void Eigenes_Konto(string code, string soll, string haben)
    {
        var k = FibuKatalog.Fuer(code);
        Assert.NotNull(k);
        Assert.Equal(soll, k.Value.Soll);
        Assert.Equal(haben, k.Value.Haben);
    }

    [Fact]
    public void Ahv_traegt_den_Arbeitgeber_daneben()
    {
        var k = FibuKatalog.Fuer("5010")!.Value;
        Assert.Equal("4062", k.AgSoll);
        Assert.Equal("2070", k.AgHaben);
    }

    [Fact]
    public void Depot_ist_nicht_die_Lgav_Zeile()
    {
        var lgav = FibuKatalog.Fuer("600.24")!.Value;
        var depot = FibuKatalog.Fuer("600.32")!.Value;
        Assert.Equal(24, lgav.MapSub);
        Assert.Equal(32, depot.MapSub);
        Assert.NotEqual(lgav.Haben, depot.Haben);
    }

    [Fact]
    public void Rueckstellung_ist_keine_Lohnart_1160()
    {
        Assert.Null(FibuKatalog.Fuer("4070"));
        Assert.Equal("1920", FibuKatalog.Fuer("1160")!.Value.Haben);
        Assert.Equal(FibuKatalog.VomVertrag, FibuKatalog.Fuer("1160")!.Value.Soll);
    }

    [Fact]
    public void Vortrag_erzeugt_keine_Buchung()
    {
        Assert.True(FibuKatalog.OhneBuchung("901"));
        Assert.False(FibuKatalog.Kennt("901"));
    }

    [Fact]
    public void Ausloeser_13_ml_bucht_Rueckstellung_an_1920()
    {
        var k = FibuKatalog.Fuer("180.3")!.Value;
        Assert.Equal("2017", k.Soll);
        Assert.Equal("1920", k.Haben);
        Assert.Equal(180, k.MapPosition);
    }
}

using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class AusgleichskasseNummerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("003.000")]
    [InlineData("046.000")]
    public void Gueltig_oder_leer(string? nummer) =>
        Assert.Null(AusgleichskasseNummer.Fehler(nummer));

    [Theory]
    [InlineData("000.003")]
    [InlineData("000")]
    [InlineData("000.000")]
    public void Erster_Block_null_wird_abgelehnt(string nummer)
    {
        var fehler = AusgleichskasseNummer.Fehler(nummer);
        Assert.Equal(AusgleichskasseNummer.Meldung, fehler);
        Assert.Contains("000.003", fehler);
    }

    [Fact]
    public void Fak_nennt_die_Familienausgleichskasse()
    {
        var fehler = AusgleichskasseNummer.Fehler("000.003", AusgleichskasseNummer.BegriffFak);
        Assert.Equal(AusgleichskasseNummer.MeldungZu(AusgleichskasseNummer.BegriffFak), fehler);
        Assert.Contains("Familienausgleichskasse", fehler!);
        Assert.Contains("000.003", fehler);
    }
}

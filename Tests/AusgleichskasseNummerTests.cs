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
}

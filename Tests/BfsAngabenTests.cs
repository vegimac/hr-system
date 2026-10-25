using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

public class BfsAngabenTests
{
    [Theory]
    [InlineData(BfsAngaben.Verband)]
    [InlineData(BfsAngaben.Firma)]
    [InlineData(BfsAngaben.Kollektiv)]
    [InlineData(BfsAngaben.Einzel)]
    public void Die_vier_Vereinbarungen_sind_gueltig(string code)
        => Assert.Equal(code, BfsAngaben.Code(code));

    [Fact]
    public void Leer_bleibt_im_Xml_der_Einzelarbeitsvertrag()
        => Assert.Equal(BfsAngaben.Einzel, BfsAngaben.FuerXml(null));

    [Fact]
    public void Buchungskreis_leer_wird_nicht_gemeldet()
        => Assert.Null(BfsAngaben.Kreis("  "));
}

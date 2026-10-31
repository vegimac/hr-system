using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class UvgMehrereVersichererTests
{
    [Fact]
    public void Zweiter_Uvg_bleibt_neben_dem_ersten_offen()
    {
        Assert.True(UvgMehrereVersicherer.BleibtDanebenOffen("UVG", altEmpfaengerId: 1, neuEmpfaengerId: 2));
        Assert.False(UvgMehrereVersicherer.BleibtDanebenOffen("UVG", altEmpfaengerId: 1, neuEmpfaengerId: 1));
    }

    [Fact]
    public void Ausgleichskasse_wird_weiterhin_abgeloest()
    {
        Assert.False(UvgMehrereVersicherer.BleibtDanebenOffen("AUSGLEICHSKASSE", 1, 2));
    }

    [Fact]
    public void Mehrere_offene_Versicherer_verlangen_eine_Personenzuweisung()
    {
        Assert.Equal(UvgMehrereVersicherer.ZuweisungPflicht, UvgMehrereVersicherer.ZuweisungFehlt(2, null));
        Assert.Null(UvgMehrereVersicherer.ZuweisungFehlt(2, 7));
        Assert.Null(UvgMehrereVersicherer.ZuweisungFehlt(1, null));
    }
}

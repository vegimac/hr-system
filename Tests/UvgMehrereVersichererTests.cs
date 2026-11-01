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
    public void Suva_ohne_Mitarbeiter_faellt_weg_wenn_ein_anderer_zugeordnet_ist()
    {
        var sichtbar = UvgMehrereVersicherer.Sichtbar(new[] { 1, 2 }, new[] { 1 });
        Assert.Equal(new[] { 1 }, sichtbar);
    }

    [Fact]
    public void Einziger_Versicherer_bleibt_ohne_Eintrag_am_Mitarbeiter()
    {
        var sichtbar = UvgMehrereVersicherer.Sichtbar(new[] { 2 }, Array.Empty<int>());
        Assert.Equal(new[] { 2 }, sichtbar);
    }

    [Fact]
    public void Mehrere_offene_Versicherer_verlangen_eine_Personenzuweisung()
    {
        Assert.Equal(UvgMehrereVersicherer.ZuweisungPflicht, UvgMehrereVersicherer.ZuweisungFehlt(2, null));
        Assert.Null(UvgMehrereVersicherer.ZuweisungFehlt(2, 7));
        Assert.Null(UvgMehrereVersicherer.ZuweisungFehlt(1, null));
    }
}

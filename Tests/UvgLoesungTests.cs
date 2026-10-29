using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class UvgLoesungTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("A1")]
    [InlineData("b1")]
    [InlineData("Z3")]
    public void ErsteStelleBuchstabeIstGueltig(string? code)
        => Assert.Null(UvgLoesung.ErsteStelleFehler(code));

    [Theory]
    [InlineData("1")]
    [InlineData("1A")]
    [InlineData("11")]
    [InlineData("0")]
    public void ErsteStelleZifferIstUngueltig(string code)
        => Assert.Contains("A–Z", UvgLoesung.ErsteStelleFehler(code));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void PersonenCode_fehlt(string? code)
        => Assert.Equal(UvgLoesung.CodeFehlt, UvgLoesung.PersonenCodeFehler(code, new[] { "A1" }));

    [Theory]
    [InlineData("A4")]
    [InlineData("A9")]
    [InlineData("B5")]
    public void Zweite_Stelle_nur_0_bis_3(string code)
        => Assert.Contains("0, 1, 2 oder 3", UvgLoesung.PersonenCodeFehler(code, new[] { "A1", "B2" }));

    [Fact]
    public void Betriebsteil_kommt_vom_Unternehmen()
    {
        var teile = new[] { "A1", "A2", "P3" };
        Assert.Null(UvgLoesung.PersonenCodeFehler("A0", teile));
        Assert.Null(UvgLoesung.PersonenCodeFehler("p1", teile));
        Assert.Contains("nicht hinterlegt", UvgLoesung.PersonenCodeFehler("C1", teile));
    }

    [Fact]
    public void NurUvgSaetzePruefenDieStelle()
    {
        Assert.Null(UvgLoesung.LoesungsCodeFehler("KTG", "12"));
        Assert.Null(UvgLoesung.LoesungsCodeFehler("AHV", "1"));
        Assert.Contains("A–Z", UvgLoesung.LoesungsCodeFehler("NBUV", "1A")!);
        Assert.Null(UvgLoesung.LoesungsCodeFehler("NBUV", "A1"));
    }
}

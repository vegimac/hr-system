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

    [Fact]
    public void NurUvgSaetzePruefenDieStelle()
    {
        Assert.Null(UvgLoesung.LoesungsCodeFehler("KTG", "12"));
        Assert.Null(UvgLoesung.LoesungsCodeFehler("AHV", "1"));
        Assert.Contains("A–Z", UvgLoesung.LoesungsCodeFehler("NBUV", "1A")!);
        Assert.Null(UvgLoesung.LoesungsCodeFehler("NBUV", "A1"));
    }
}

using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class UvgzLoesungTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("10")]
    [InlineData("11")]
    [InlineData("12")]
    [InlineData("A2")]
    [InlineData("b1")]
    [InlineData("1A")]
    [InlineData("Z9")]
    public void ZweiStellenSindGueltig(string? code)
        => Assert.Null(UvgzLoesung.CodeFehler(code));

    [Theory]
    [InlineData("A")]
    [InlineData("1")]
    [InlineData("ABC")]
    [InlineData("Ä1")]
    [InlineData("-1")]
    [InlineData("A!")]
    public void AndereFormIstUngueltig(string code)
        => Assert.Contains("zwei Stellen", UvgzLoesung.CodeFehler(code));

    [Fact]
    public void NurUvgzPrueftDenCode()
    {
        Assert.Null(UvgzLoesung.LoesungsCodeFehler("NBUV", "1A"));
        Assert.Null(UvgzLoesung.LoesungsCodeFehler("KTG", "12"));
        Assert.Contains("zwei Stellen", UvgzLoesung.LoesungsCodeFehler("UVGZ", "A")!);
        Assert.Null(UvgzLoesung.LoesungsCodeFehler("UVGZ", "11"));
    }
}

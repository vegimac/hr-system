using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class KassenArtTests
{
    [Theory]
    [InlineData("AUSGLEICHSKASSE", "002")]
    [InlineData("FAK", "002.000")]
    [InlineData("FAK", "001.000")]
    [InlineData("FAK", "025.000")]
    [InlineData("AUSGLEICHSKASSE", "150.000")]
    public void Kantonal(string art, string nummer) =>
        Assert.Equal(KassenArt.Kantonal, KassenArt.AusNummer(art, nummer));

    [Theory]
    [InlineData("046.000")]
    [InlineData("079.000")]
    [InlineData("028.000")]
    [InlineData("642.201")]
    public void Verband(string nummer) =>
        Assert.Equal(KassenArt.Verband, KassenArt.AusNummer("FAK", nummer));

    [Theory]
    [InlineData("AUSGLEICHSKASSE", "026.001")]
    [InlineData("FAK", "027.000")]
    [InlineData("FAK", "")]
    [InlineData("UVG", "046.000")]
    [InlineData("FAK", null)]
    public void Ohne_Zuordnung(string art, string? nummer) =>
        Assert.Null(KassenArt.AusNummer(art, nummer));
}

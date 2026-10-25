using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class KtgLoesungTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("10")]
    [InlineData("11")]
    [InlineData("A2")]
    [InlineData("b1")]
    public void ZweiStellenSindGueltig(string? code)
        => Assert.Null(KtgLoesung.LoesungsCodeFehler("KTG", code));

    [Theory]
    [InlineData("A")]
    [InlineData("Ä1")]
    [InlineData("A!")]
    public void AndereFormIstUngueltig(string code)
        => Assert.Contains("KTG-Code", KtgLoesung.LoesungsCodeFehler("KTG", code));

    [Fact]
    public void NurKtgPrueftDenCode()
        => Assert.Null(KtgLoesung.LoesungsCodeFehler("UVGZ", "A"));

    [Theory]
    [InlineData("KTG", "UVG", "UVG")]
    [InlineData("KTG", "vertrag", "VERTRAG")]
    [InlineData("KTG", null, "UVG")]
    [InlineData("UVGZ", "VERTRAG", null)]
    public void HoechstlohnArtNurBeiKtg(string sv, string? art, string? soll)
        => Assert.Equal(soll, KtgLoesung.HoechstlohnArt(sv, art));
}
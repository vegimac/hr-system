using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class BvgLoesungTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1101")]
    [InlineData("2020")]
    public void Code_bis_32_Stellen_ist_gueltig(string? code)
        => Assert.Null(BvgLoesung.CodeFehler("BVG", code));

    [Fact]
    public void Code_mit_33_Stellen_wird_abgelehnt()
        => Assert.Contains("32", BvgLoesung.CodeFehler("BVG", new string('A', 33)));

    [Fact]
    public void Anderer_SV_Code_wird_nicht_geprueft()
        => Assert.Null(BvgLoesung.CodeFehler("KTG", new string('A', 40)));

    [Fact]
    public void Ohne_Loesung_an_der_Lohnart_zaehlt_sie_weiter()
        => Assert.True(BvgLoesung.Zaehlt(new Lohnposition { BvgPflichtig = true }, "2020"));

    [Fact]
    public void Loesung_1101_zaehlt_nicht_fuer_Kaderplan()
        => Assert.False(BvgLoesung.Zaehlt(
            new Lohnposition { BvgPflichtig = true, BvgLoesungsCode = "1101" }, "2020"));

    [Fact]
    public void Loesung_1101_zaehlt_fuer_dieselbe_Person()
        => Assert.True(BvgLoesung.Zaehlt(
            new Lohnposition { BvgPflichtig = true, BvgLoesungsCode = "1101" }, "1101"));

    [Fact]
    public void Nicht_bvg_pflichtig_zaehlt_nie()
        => Assert.False(BvgLoesung.Zaehlt(new Lohnposition { BvgPflichtig = false, BvgLoesungsCode = "1101" }, "1101"));
}

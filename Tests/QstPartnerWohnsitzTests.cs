using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Wohnsitz des Ehepartners für QST-Mängel (Bewilligung / CH-Arbeitgeber).
/// Walter 15.09.2026: gleicher Haushalt übernimmt das Land des MA —
/// MA in Bergamo (IT) ⇒ Partner ebenfalls Ausland, kein CH-Arbeitgeber.
/// </summary>
public class QstPartnerWohnsitzTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("CH", true)]
    [InlineData("ch", true)]
    [InlineData("Schweiz", true)]
    [InlineData("Suisse", true)]
    [InlineData("Svizzera", true)]
    [InlineData("Switzerland", true)]
    [InlineData("IT", false)]
    [InlineData("ITALY", false)]
    [InlineData("Italia", false)]
    [InlineData("DE", false)]
    [InlineData("Germany", false)]
    public void IstLandSchweiz_erkennt_CH_und_Ausland(string? land, bool erwartet)
        => Assert.Equal(erwartet, QstPflichtCheckService.IstLandSchweiz(land));

    [Fact]
    public void Haushalt_im_Ausland_ist_Ausland()
    {
        var art = QstPflichtCheckService.BestimmePartnerWohnsitz(
            livesInSwitzerland: false,
            lebtImHaushalt: true,
            maWohnLand: "ITALY",
            hatAlternativeAdresse: false,
            alternativeAddressCountry: null);
        Assert.Equal(QstPflichtCheckService.PartnerWohnsitzArt.Ausland, art);
    }

    [Fact]
    public void Haushalt_in_der_CH_ist_Schweiz()
    {
        var art = QstPflichtCheckService.BestimmePartnerWohnsitz(
            livesInSwitzerland: false,
            lebtImHaushalt: true,
            maWohnLand: "CH",
            hatAlternativeAdresse: false,
            alternativeAddressCountry: null);
        Assert.Equal(QstPflichtCheckService.PartnerWohnsitzArt.Schweiz, art);
    }

    [Fact]
    public void Haekchen_InDerSchweiz_sticht_Auslands_Haushalt()
    {
        var art = QstPflichtCheckService.BestimmePartnerWohnsitz(
            livesInSwitzerland: true,
            lebtImHaushalt: true,
            maWohnLand: "ITALY",
            hatAlternativeAdresse: false,
            alternativeAddressCountry: null);
        Assert.Equal(QstPflichtCheckService.PartnerWohnsitzArt.Schweiz, art);
    }

    [Fact]
    public void Keine_Adresse_ohne_Haushalt_ist_Unklar()
    {
        var art = QstPflichtCheckService.BestimmePartnerWohnsitz(
            livesInSwitzerland: false,
            lebtImHaushalt: false,
            maWohnLand: "CH",
            hatAlternativeAdresse: false,
            alternativeAddressCountry: null);
        Assert.Equal(QstPflichtCheckService.PartnerWohnsitzArt.Unklar, art);
    }

    [Fact]
    public void Eigene_Auslandsadresse_ist_Ausland()
    {
        var art = QstPflichtCheckService.BestimmePartnerWohnsitz(
            livesInSwitzerland: false,
            lebtImHaushalt: false,
            maWohnLand: "CH",
            hatAlternativeAdresse: true,
            alternativeAddressCountry: "UA");
        Assert.Equal(QstPflichtCheckService.PartnerWohnsitzArt.Ausland, art);
    }
}

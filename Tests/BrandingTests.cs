using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>Logo ohne Bogen pro Server (BRANDING_OHNE_BOGEN, Walter 02.11.2026).</summary>
public class BrandingTests
{
    [Theory]
    [InlineData("/img/onecrew-logo.webp", "/img/onecrew-schriftzug.webp")]
    [InlineData("/img/onecrew-logo.png", "/img/onecrew-schriftzug.png")]
    [InlineData("/IMG/OneCrew-Logo.PNG", "/img/onecrew-schriftzug.png")]
    [InlineData("/img/onecrew-logo-hell.png", "/img/onecrew-schriftzug-hell.png")]
    public void OhneBogen_LeitetLogoUm(string pfad, string ziel)
        => Assert.Equal(ziel, Branding.Umleitung(pfad, ohneBogen: true));

    [Theory]
    [InlineData("/img/onecrew-logo.webp")]
    [InlineData("/img/elm-swissdec-icon.webp")]
    public void MitBogen_KeineUmleitung(string pfad)
        => Assert.Null(Branding.Umleitung(pfad, ohneBogen: false));

    [Fact]
    public void AndereBilder_BleibenUnveraendert()
        => Assert.Null(Branding.Umleitung("/img/swissdec-logo.png", ohneBogen: true));
}

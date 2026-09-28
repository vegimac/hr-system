using HrSystem.Models;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Januar 2025 (Walter 28.09.2026): Schema-Fehler «CountryAbroad» und fehlendes
/// KindOfResidence bei Grenzgängern; Personenadresse mit Wohnsitzland statt SWITZERLAND
/// (RefXML_2025-01_MONTHLY.xml, TF28 Arbenz, TF29 Forster, TF30 Müller).
/// </summary>
public class ElmWohnsitzAuslandTests
{
    [Fact]
    public void Wohnkanton_CH()
    {
        var r = ElmMonthlyDeclarationBuilder.QstResidence("BE", "CH", null, false, null);
        Assert.Equal("BE", r.Element(ElmGemeinsam.Sd + "CantonCH")?.Value);
    }

    [Fact]
    public void Grenzgaenger_taeglich()
    {
        var r = ElmMonthlyDeclarationBuilder.QstResidence(null, "IT", "IT", false, null);
        Assert.Equal("IT", r.Element(ElmGemeinsam.Sd + "AbroadCountry")?.Value);
        Assert.NotNull(r.Element(ElmGemeinsam.Sd + "KindOfResidence")?.Element(ElmGemeinsam.Sd + "Daily"));
    }

    [Fact]
    public void Wochenaufenthalter_mit_Adresse()
    {
        var wa = new EmployeeAddress { AddressType = "Wochenaufenthalt", Street = "Laupenstrasse 10", ZipCode = "3008", City = "Bern", Country = "CH" };
        var r = ElmMonthlyDeclarationBuilder.QstResidence(null, "IT", null, true, wa);
        var weekly = r.Element(ElmGemeinsam.Sd + "KindOfResidence")?.Element(ElmGemeinsam.Sd + "Weekly");
        Assert.Equal("IT", r.Element(ElmGemeinsam.Sd + "AbroadCountry")?.Value);
        Assert.Equal("3008", weekly?.Element(ElmGemeinsam.C + "ZIP-Code")?.Value);
        Assert.Equal("SWITZERLAND", weekly?.Element(ElmGemeinsam.C + "Country")?.Value);
    }

    [Theory]
    [InlineData("CH", "SWITZERLAND")]
    [InlineData("Schweiz", "SWITZERLAND")]
    [InlineData("IT", "ITALY")]
    [InlineData("DE", "GERMANY")]
    [InlineData(null, "SWITZERLAND")]
    public void Landname(string? land, string soll)
        => Assert.Equal(soll, ElmGemeinsam.LandName(land));
}

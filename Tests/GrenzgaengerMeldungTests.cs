using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class GrenzgaengerMeldungTests
{
    [Fact]
    public void Codice_gueltig()
        => Assert.True(CodiceFiscale.Gueltig("RSSMRA85T10A562S", out _));

    [Fact]
    public void Codice_pruefziffer_falsch()
    {
        Assert.False(CodiceFiscale.Gueltig("RSSMRA85T10A562A", out var grund));
        Assert.Contains("Prüfziffer", grund);
    }

    [Fact]
    public void Codice_zu_kurz()
        => Assert.False(CodiceFiscale.Gueltig("RSSMRA85", out _));

    [Fact]
    public void Frankreich_wohnsitz_und_arbeitsort()
        => Assert.True(GrenzgaengerMeldung.Frankreich("FR", true, "IT", false));

    [Fact]
    public void Frankreich_schweizer_bei_privatem_arbeitgeber_zaehlt()
        => Assert.True(GrenzgaengerMeldung.Frankreich("Frankreich", true, "CH", false));

    [Fact]
    public void Frankreich_schweizer_bei_oeffentlichem_arbeitgeber_faellt_weg()
        => Assert.False(GrenzgaengerMeldung.Frankreich("FR", true, "CH", true));

    [Fact]
    public void Frankreich_wohnort_schweiz_faellt_weg()
        => Assert.False(GrenzgaengerMeldung.Frankreich("CH", true, "FR", false));

    [Fact]
    public void Italien_nur_bei_wohnsitz_italien()
    {
        Assert.True(GrenzgaengerMeldung.WohnsitzItalien("IT"));
        Assert.True(GrenzgaengerMeldung.WohnsitzItalien("Italien"));
        Assert.False(GrenzgaengerMeldung.WohnsitzItalien("FR"));
        Assert.False(GrenzgaengerMeldung.WohnsitzItalien("Frankreich"));
        Assert.False(GrenzgaengerMeldung.WohnsitzItalien("CH"));
    }

    [Fact]
    public void Sfn_nur_der_tarifcode()
    {
        Assert.True(GrenzgaengerMeldung.Sfn("SFN"));
        Assert.False(GrenzgaengerMeldung.Sfn("A0N"));
    }
}

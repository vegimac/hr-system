using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// SUA-Zertifikat erneuern als Benutzer (Walter 15.10.2026, Swissdec-Zertifizierung) —
/// ELM 6.0 TransmitterRequirements Anhang C.2.3 / C.3.1.
/// </summary>
public class ElmZertifikatLaufzeitTests
{
    private static readonly DateTime Jetzt = new(2026, 10, 15, 10, 0, 0);

    private static ElmZertifikatLaufzeit.Ergebnis Jahr(int restTage, int erneuerungen = 0, bool schluessel = true)
    {
        var bis = Jetzt.AddDays(restTage).AddHours(1);
        return ElmZertifikatLaufzeit.Beurteile(bis.AddYears(-1), bis, schluessel, erneuerungen, Jetzt);
    }

    [Fact]
    public void Jahreszertifikat_100TageRest_Gruen_ErneuernErstImFenster()
    {
        var e = Jahr(100);
        Assert.Equal(ElmZertifikatLaufzeit.Ok, e.Stufe);
        Assert.Equal(ElmZertifikatLaufzeit.AktionKeine, e.Aktion);
        Assert.False(e.ErneuernErlaubt);
        Assert.False(e.AutoFaellig);
        Assert.Equal(e.GueltigBis!.Value.AddDays(-60), e.ErneuernAb);
        Assert.Contains("Erneuern ist erst ab", ElmZertifikatLaufzeit.ErneuernGesperrt(e));
    }

    [Fact]
    public void Jahreszertifikat_45TageRest_Orange_ErneuernMoeglich_NochNichtAutomatisch()
    {
        var e = Jahr(45);
        Assert.Equal(ElmZertifikatLaufzeit.Warn, e.Stufe);
        Assert.Equal(ElmZertifikatLaufzeit.AktionErneuern, e.Aktion);
        Assert.True(e.ErneuernErlaubt);
        Assert.False(e.AutoFaellig);
        Assert.Null(ElmZertifikatLaufzeit.ErneuernGesperrt(e));
    }

    [Fact]
    public void Jahreszertifikat_25TageRest_AutomatikFaellig()
    {
        var e = Jahr(25);
        Assert.Equal(ElmZertifikatLaufzeit.Warn, e.Stufe);
        Assert.True(e.AutoFaellig);
    }

    [Fact]
    public void Jahreszertifikat_10TageRest_Rot()
    {
        var e = Jahr(10);
        Assert.Equal(ElmZertifikatLaufzeit.Rot, e.Stufe);
        Assert.True(e.ErneuernErlaubt);
        Assert.True(e.AutoFaellig);
    }

    [Fact]
    public void Abgelaufen_KeinErneuern_NeuRegistrieren()
    {
        var e = Jahr(-2);
        Assert.Equal(ElmZertifikatLaufzeit.Rot, e.Stufe);
        Assert.Equal(ElmZertifikatLaufzeit.AktionNeuRegistrieren, e.Aktion);
        Assert.False(e.ErneuernErlaubt);
        Assert.False(e.AutoFaellig);
        Assert.Contains("neu registrieren", ElmZertifikatLaufzeit.ErneuernGesperrt(e));
    }

    [Fact]
    public void DreiErneuerungen_NurNochNeuRegistrieren()
    {
        var e = Jahr(20, erneuerungen: 3);
        Assert.False(e.ErneuernErlaubt);
        Assert.False(e.AutoFaellig);
        Assert.Equal(ElmZertifikatLaufzeit.AktionNeuRegistrieren, e.Aktion);
        Assert.NotNull(ElmZertifikatLaufzeit.ErneuernGesperrt(e));
    }

    [Fact]
    public void OhneSchluessel_NeuRegistrieren()
    {
        var e = Jahr(200, schluessel: false);
        Assert.Equal(ElmZertifikatLaufzeit.Rot, e.Stufe);
        Assert.Equal(ElmZertifikatLaufzeit.AktionNeuRegistrieren, e.Aktion);
        Assert.False(e.ErneuernErlaubt);
    }

    [Fact]
    public void KeinZertifikat_Rot_NeuRegistrieren()
    {
        var e = ElmZertifikatLaufzeit.Beurteile(null, null, false, 0, Jetzt);
        Assert.Equal(ElmZertifikatLaufzeit.Rot, e.Stufe);
        Assert.Equal(ElmZertifikatLaufzeit.AktionNeuRegistrieren, e.Aktion);
        Assert.Null(e.GueltigBis);
    }

    /// <summary>RefApps-Testzertifikat gilt rund 10 Tage — feste 60 Tage wären dort immer rot.</summary>
    [Theory]
    [InlineData(8, "ok", false)]
    [InlineData(4, "warn", false)]
    [InlineData(1, "warn", true)]
    [InlineData(0, "rot", true)]
    public void Testzertifikat_10Tage_SchwellenNachLaufzeit(int restTage, string stufe, bool auto)
    {
        var bis = Jetzt.AddDays(restTage).AddHours(12);
        var e = ElmZertifikatLaufzeit.Beurteile(bis.AddDays(-10), bis, true, 0, Jetzt);
        Assert.Equal(stufe, e.Stufe);
        Assert.Equal(auto, e.AutoFaellig);
        Assert.True(e.ErneuernErlaubt);
    }
}

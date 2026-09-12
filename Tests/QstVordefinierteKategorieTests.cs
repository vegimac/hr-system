using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Swissdec CategoryPredefined (Walter 11.09.2026). MEY ist nicht ESTV-Tarif M.
/// TF30 Müller Jan 2025: 5'500 × 29.5 % BE = 1'622.50.
/// </summary>
public class QstVordefinierteKategorieTests
{
    [Fact]
    public void Parse_NurVordefinierte()
    {
        Assert.Equal(QstVordefinierteKategorie.Art.Mey, QstVordefinierteKategorie.Parse("MEY")?.Art);
        Assert.Equal(QstVordefinierteKategorie.Art.Mey, QstVordefinierteKategorie.Parse("mey")?.Art);
        Assert.Null(QstVordefinierteKategorie.Parse("A0Y"));
        Assert.Null(QstVordefinierteKategorie.Parse("M0Y"));
    }

    [Fact]
    public void BeMey_Tf30_Mueller_1622_50()
    {
        var satz = QstVordefinierteKategorie.MitarbeiterbeteiligungSatz("BE");
        Assert.Equal(29.5m, satz);
        Assert.Equal(1622.50m, Math.Round(5500m * satz!.Value / 100m, 2));
    }

    [Fact]
    public void UnbekannterKanton_KeinErfundenerSatz()
    {
        Assert.Null(QstVordefinierteKategorie.MitarbeiterbeteiligungSatz("AG"));
        Assert.Null(QstVordefinierteKategorie.VerwaltungsratSatz("BE"));
    }

    [Fact]
    public void NonUndSfn_SindNullAbzug()
    {
        Assert.True(QstVordefinierteKategorie.IstNullAbzug(QstVordefinierteKategorie.Art.Non));
        Assert.True(QstVordefinierteKategorie.IstNullAbzug(QstVordefinierteKategorie.Art.Sfn));
        Assert.False(QstVordefinierteKategorie.IstNullAbzug(QstVordefinierteKategorie.Art.Mey));
    }

    [Fact]
    public void Katalog_HatAlleSiebenCodes()
    {
        var codes = QstVordefinierteKategorie.Katalog().Select(k => k.Code).ToHashSet();
        Assert.True(codes.SetEquals(new[] { "HEN", "HEY", "MEN", "MEY", "NON", "NOY", "SFN" }));
    }

    [Fact]
    public void Erkennung_Nur1960Ausland_Mey()
    {
        var e = QstSonderkategorieErkennung.Pruefe(null, true, true, false, false, true);
        Assert.Equal("MEY", e.VorschlagCode);
        Assert.False(e.BehoerdeAbklaeren);
    }

    [Fact]
    public void Erkennung_1960PlusLohn_Amt()
    {
        var e = QstSonderkategorieErkennung.Pruefe(null, true, true, false, true, true);
        Assert.Equal("MEY", e.VorschlagCode);
        Assert.True(e.BehoerdeAbklaeren);
    }

    [Fact]
    public void Erkennung_EstvM_IstKeineSonderkategorie()
    {
        Assert.Null(QstVordefinierteKategorie.Parse("M0Y"));
    }
}

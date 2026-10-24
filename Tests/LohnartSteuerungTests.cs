using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Neue Steuerungen an der Lohnart. Der Seed hält sie am bisherigen Lohn,
/// eine abweichende Einstellung muss durchschlagen.
/// </summary>
public class LohnartSteuerungTests
{
    [Fact]
    public void Qst_Charakter_folgt_dem_Seed()
    {
        Assert.True(LohnartSteuerung.ImPeriodischenQstTopf(new Lohnposition { QstPeriodisch = true, QstCharakter = "monatlich" }));
        Assert.False(LohnartSteuerung.ImPeriodischenQstTopf(new Lohnposition { QstPeriodisch = false, QstCharakter = "jaehrlich" }));
    }

    [Fact]
    public void Qst_Charakter_sticht_das_Haekchen()
    {
        Assert.False(LohnartSteuerung.ImPeriodischenQstTopf(new Lohnposition { QstPeriodisch = true, QstCharakter = "jaehrlich" }));
        Assert.True(LohnartSteuerung.ImPeriodischenQstTopf(new Lohnposition { QstPeriodisch = false, QstCharakter = "monatlich" }));
    }

    [Fact]
    public void Bvg_Stamm_Monatslohn_ist_voraussichtlich_mit_Faktor_x()
    {
        var z = LohnartSteuerung.BvgAbleiten(1, 0, "x");
        Assert.Equal(LohnartSteuerung.BvgVoraussichtlich, z.Betrachtung);
        Assert.True(z.FaktorX);
        Assert.True(z.Pflichtig);
        Assert.Equal(0, z.RueckCode);
        Assert.Equal(13m, LohnartSteuerung.BvgFaktorAufloesen(true, 0m, true));
        Assert.Equal(12m, LohnartSteuerung.BvgFaktorAufloesen(true, 0m, false));
    }

    [Fact]
    public void Bvg_Stamm_Stundenlohn_rueckwirkend_13_Monatslohn_keine()
    {
        var stunden = LohnartSteuerung.BvgAbleiten(0, 1, "");
        Assert.Equal(LohnartSteuerung.BvgRueckwirkend, stunden.Betrachtung);
        Assert.Equal(1, stunden.RueckCode);
        Assert.True(stunden.Pflichtig);

        var lektionen = LohnartSteuerung.BvgAbleiten(0, 2, "");
        Assert.Equal(2, lektionen.RueckCode);

        var ort = LohnartSteuerung.BvgAbleiten(1, 0, "12");
        Assert.Equal(LohnartSteuerung.BvgVoraussichtlich, ort.Betrachtung);
        Assert.Equal(12m, ort.Faktor);
        Assert.False(ort.FaktorX);

        var dreizehnter = LohnartSteuerung.BvgAbleiten(0, 0, "");
        Assert.Equal(LohnartSteuerung.BvgKeine, dreizehnter.Betrachtung);
        Assert.False(dreizehnter.Pflichtig);
    }

    [Fact]
    public void Bvg_voraussichtlich_rechnet_mal_Faktor_der_Monat_nicht()
    {
        Assert.Equal(65000m, LohnartSteuerung.BvgJahreslohn("voraussichtlich", 5000m, 13m, 41234m));
        Assert.Equal(41234m, LohnartSteuerung.BvgJahreslohn("rueckwirkend", 5000m, 13m, 41234m));
    }

    [Fact]
    public void SvBases_ohne_Uvg_bleibt_auf_Nbu()
    {
        var basen = new SvBases(100m, 80m, 70m, 60m, 50m);
        Assert.Equal(80m, basen.UvgBasis);
        Assert.Equal(80m, basen.UvgzBasis);
        var getrennt = basen with { Uvg = 40m, Uvgz = 20m };
        Assert.Equal(40m, getrennt.UvgBasis);
        Assert.Equal(20m, getrennt.UvgzBasis);
        Assert.Equal(80m, getrennt.Nbuv);
    }

    [Fact]
    public void Statistik_Seed_entspricht_der_Nummer()
    {
        Assert.Equal(ElmStatistikCodes.Topf.Zulagen, ElmStatistikCodes.Gespeichert("Zulagen", null));
        Assert.Equal(ElmStatistikCodes.Topf.Dreizehnter, ElmStatistikCodes.Gespeichert(null, "Dreizehnter"));
        Assert.Null(ElmStatistikCodes.Gespeichert(null, null));
        Assert.Equal(ElmStatistikCodes.Topf.Keiner, ElmStatistikCodes.Gespeichert("Keiner", null));
        Assert.Equal(ElmStatistikCodes.Topf.Zulagen, ElmStatistikCodes.TopfFuer(1070));
    }
}

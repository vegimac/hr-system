using HrSystem.Services;
using Xunit;
using M = HrSystem.Services.ArbeitsMuster;

namespace HrSystem.Tests;

/// <summary>
/// Arbeitsmuster aus den Stempelzeiten (Walter 15.10.2026): «welche MA an welchen Tagen
/// zu welchen Zeiten am meisten arbeiten» — Wochenraster für die Dienstplanung.
/// Zeitraum in allen Tests: Mo 05.10.2026 – So 18.10.2026 = jeder Wochentag zweimal.
/// </summary>
public class ArbeitsMusterTests
{
    private static readonly DateOnly Von = new(2026, 10, 5);
    private static readonly DateOnly Bis = new(2026, 10, 18);
    private static M.Stempel S(int ma, int tag, int vonH, int vonM, int bisTag, int bisH, int bisM) =>
        new(ma, new DateTime(2026, 10, tag, vonH, vonM, 0), new DateTime(2026, 10, bisTag, bisH, bisM, 0));

    [Fact]
    public void Jeden_Freitag_abends_ergibt_volle_Besetzung_und_Hauptzeit()
    {
        var e = M.Rechne(new[] { S(1, 9, 17, 0, 9, 23, 0), S(1, 16, 17, 0, 16, 23, 0) }, Von, Bis);

        Assert.All(e.WochentagAnzahl, n => Assert.Equal(2, n));
        for (var std = 17; std < 23; std++)
        {
            Assert.Equal(1.0m, e.Raster[4][std].Besetzung);
            Assert.Equal(1.0m, Assert.Single(e.Raster[4][std].Mitarbeiter).Anteil);
        }
        Assert.Equal(0m, e.Raster[4][23].Besetzung);
        var ma = Assert.Single(e.Mitarbeiter);
        Assert.Equal(12m, ma.Stunden);
        Assert.Equal(6m, ma.StundenProWoche);
        Assert.Equal("Fr 17–23", ma.Hauptzeiten);
    }

    [Fact]
    public void Schicht_ueber_Mitternacht_zaehlt_am_richtigen_Tag()
    {
        var e = M.Rechne(new[] { S(2, 10, 22, 0, 11, 2, 0) }, Von, Bis);

        Assert.Equal(0.5m, e.Raster[5][22].Besetzung);
        Assert.Equal(0.5m, e.Raster[5][23].Besetzung);
        Assert.Equal(0.5m, e.Raster[6][0].Besetzung);
        Assert.Equal(0.5m, e.Raster[6][1].Besetzung);
        Assert.Equal(0m, e.Raster[6][2].Besetzung);
    }

    [Fact]
    public void Wenige_Minuten_zaehlen_zur_Besetzung_aber_nicht_als_Anwesenheit()
    {
        var e = M.Rechne(new[] { S(3, 5, 10, 50, 5, 12, 0) }, Von, Bis);

        Assert.Equal(0.1m, e.Raster[0][10].Besetzung);
        Assert.Empty(e.Raster[0][10].Mitarbeiter);
        Assert.Equal(0.5m, Assert.Single(e.Raster[0][11].Mitarbeiter).Anteil);
    }

    [Fact]
    public void Gleiche_Zeiten_an_mehreren_Tagen_werden_zusammengefasst()
    {
        var e = M.Rechne(new[]
        {
            S(4, 5, 11, 0, 5, 14, 0), S(4, 6, 11, 0, 6, 14, 0),
            S(4, 12, 11, 0, 12, 14, 0), S(4, 13, 11, 0, 13, 14, 0),
            S(4, 9, 18, 0, 9, 22, 0),
        }, Von, Bis);

        Assert.Equal("Mo, Di 11–14 · Fr 18–22", Assert.Single(e.Mitarbeiter).Hauptzeiten);
    }

    [Fact]
    public void Stempel_ausserhalb_des_Zeitraums_werden_abgeschnitten()
    {
        var e = M.Rechne(new[] { S(5, 4, 22, 0, 5, 2, 0) }, Von, Bis);

        Assert.Equal(0m, e.Raster[6][22].Besetzung);
        Assert.Equal(0.5m, e.Raster[0][0].Besetzung);
        Assert.Equal(2m, Assert.Single(e.Mitarbeiter).Stunden);
    }
}

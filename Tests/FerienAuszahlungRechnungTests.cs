using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Ferien auszahlen ohne Bezug: Tage gedeckelt auf das Guthaben, Geld im
/// Verhältnis aus dem Topf, «Saldo per 31.12.» abzüglich seit 1.1. Bezogenem.
/// Kunstdaten.
/// </summary>
public class FerienAuszahlungRechnungTests
{
    private static FerienAuszahlungRechnung.Posten Tage(decimal t, int id = 1) =>
        new(id, FerienAuszahlungRechnung.ArtTage, t);

    private static FerienAuszahlungRechnung.Posten Vorjahr(int id = 1) =>
        new(id, FerienAuszahlungRechnung.ArtVorjahr, null);

    [Fact]
    public void Tage_im_Verhaeltnis_aus_dem_Topf()
    {
        var e = FerienAuszahlungRechnung.TageAufloesen(new[] { Tage(4m) }, 10m, null, 0m);
        var z = FerienAuszahlungRechnung.ZeilenAusTopf(e, 1000m, 10m).Single();
        Assert.Equal(4m, z.Tage);
        Assert.Equal(400m, z.Betrag);
        Assert.Equal(100m, z.Satz);
        Assert.Null(z.Hinweis);
    }

    [Fact]
    public void Nie_mehr_Tage_als_vorhanden_kein_Vorbezug()
    {
        var e = FerienAuszahlungRechnung.TageAufloesen(new[] { Tage(12m) }, 10m, null, 0m);
        var z = FerienAuszahlungRechnung.ZeilenAusTopf(e, 1000m, 10m).Single();
        Assert.Equal(10m, z.Tage);
        Assert.Equal(1000m, z.Betrag);
        Assert.Contains("max.", z.Hinweis);
    }

    [Fact]
    public void Negatives_Guthaben_zahlt_nichts_aus()
    {
        var e = FerienAuszahlungRechnung.TageAufloesen(new[] { Tage(3m) }, -2m, null, 0m);
        var z = FerienAuszahlungRechnung.ZeilenAusTopf(e, -150m, -2m).Single();
        Assert.Equal(0m, z.Tage);
        Assert.Equal(0m, z.Betrag);
    }

    [Fact]
    public void Saldo_Vorjahr_ganz_ausbezahlt_wenn_nichts_bezogen()
    {
        var vj = new FerienAuszahlungRechnung.Vorjahr(6m, 540m);
        var e = FerienAuszahlungRechnung.TageAufloesen(new[] { Vorjahr() }, 9m, vj, 0m);
        var z = FerienAuszahlungRechnung.ZeilenAusTopf(e, 800m, 9m).Single();
        Assert.Equal(6m, z.Tage);
        Assert.Equal(540m, z.Betrag);
    }

    [Fact]
    public void Saldo_Vorjahr_abzueglich_seit_Januar_bezogener_Tage()
    {
        var vj = new FerienAuszahlungRechnung.Vorjahr(6m, 540m);
        var e = FerienAuszahlungRechnung.TageAufloesen(new[] { Vorjahr() }, 7m, vj, 2m);
        var z = FerienAuszahlungRechnung.ZeilenAusTopf(e, 700m, 7m).Single();
        Assert.Equal(4m, z.Tage);
        Assert.Equal(360m, z.Betrag);
    }

    [Fact]
    public void Saldo_Vorjahr_nie_mehr_Geld_als_im_Topf()
    {
        var vj = new FerienAuszahlungRechnung.Vorjahr(6m, 540m);
        var e = FerienAuszahlungRechnung.TageAufloesen(new[] { Vorjahr() }, 9m, vj, 0m);
        var z = FerienAuszahlungRechnung.ZeilenAusTopf(e, 500m, 9m).Single();
        Assert.Equal(500m, z.Betrag);
    }

    [Fact]
    public void Saldo_Vorjahr_unbekannt_zahlt_nichts_und_sagt_warum()
    {
        var e = FerienAuszahlungRechnung.TageAufloesen(new[] { Vorjahr() }, 9m, null, 0m).Single();
        Assert.Equal(0m, e.Tage);
        Assert.Contains("unbekannt", e.Hinweis);
    }

    [Fact]
    public void Zwei_Eintraege_teilen_sich_Tage_und_Topf()
    {
        var e = FerienAuszahlungRechnung.TageAufloesen(new[] { Tage(3m, 1), Tage(3m, 2) }, 5m, null, 0m);
        var z = FerienAuszahlungRechnung.ZeilenAusTopf(e, 500m, 5m);
        Assert.Equal(3m, z[0].Tage);
        Assert.Equal(300m, z[0].Betrag);
        Assert.Equal(2m, z[1].Tage);
        Assert.Equal(200m, z[1].Betrag);
    }

    [Fact]
    public void Fix_Tage_mal_Tagessatz()
    {
        var e = FerienAuszahlungRechnung.TageAufloesen(new[] { Tage(2m) }, 8m, null, 0m);
        var z = FerienAuszahlungRechnung.ZeilenFix(e, 147.95m).Single();
        Assert.Equal(295.90m, z.Betrag);
    }

    [Fact]
    public void Bezeichnung_nennt_Saldo_Stichtag()
    {
        var z = new FerienAuszahlungRechnung.Zeile(1, FerienAuszahlungRechnung.ArtVorjahr, 6m, 540m, 90m, null);
        Assert.Equal("Ferien-Auszahlung Saldo 31.12.2025 (6.00 Tage)",
            FerienAuszahlungRechnung.Bezeichnung(z, "Ferien-Auszahlung", 2025));
    }
}

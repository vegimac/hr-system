using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

public class ElmAhvJahreslohnTests
{
    static ElmAhvJahreslohn.Monat M(int monat, decimal basis, ElmAhvJahreslohn.Art art, decimal? alv = null)
        => new(monat, basis, alv ?? Math.Min(basis, 12350m), art);

    [Fact]
    public void Normales_Jahr_bleibt_ein_Abschnitt_mit_gedeckelter_Alv()
    {
        var a = Assert.Single(ElmAhvJahreslohn.Abschnitte(new[]
        {
            M(1, 5000m, ElmAhvJahreslohn.Art.Normal),
            M(2, 20000m, ElmAhvJahreslohn.Art.Normal),
        }));
        Assert.Equal(ElmAhvJahreslohn.Art.Normal, a.Art);
        Assert.Equal(1, a.Von);
        Assert.Equal(2, a.Bis);
        Assert.Equal(25000m, a.Basis);
        Assert.Equal(25000m, a.AhvLohn);
        Assert.Equal(0m, a.AhvFrei);
        Assert.Equal(17350m, a.AlvLohn);
        Assert.Equal(0m, a.AlvFrei);
    }

    [Fact]
    public void Sonderfall_meldet_den_Lohn_als_frei()
    {
        var a = Assert.Single(ElmAhvJahreslohn.Abschnitte(new[]
        {
            M(2, 5000m, ElmAhvJahreslohn.Art.Frei),
        }));
        Assert.Equal(5000m, a.Basis);
        Assert.Equal(0m, a.AhvLohn);
        Assert.Equal(5000m, a.AhvFrei);
        Assert.Equal(0m, a.AlvLohn);
        Assert.Equal(5000m, a.AlvFrei);
    }

    [Fact]
    public void Verzicht_ab_Maerz_ergibt_zwei_Abschnitte()
    {
        var monate = new List<ElmAhvJahreslohn.Monat>
        {
            M(1, 4000m, ElmAhvJahreslohn.Art.Normal),
            M(2, 4000m, ElmAhvJahreslohn.Art.Normal),
        };
        for (var m = 3; m <= 12; m++)
            monate.Add(M(m, 4000m, ElmAhvJahreslohn.Art.Verzicht));

        var a = ElmAhvJahreslohn.Abschnitte(monate);
        Assert.Equal(2, a.Count);

        Assert.Equal(ElmAhvJahreslohn.Art.Normal, a[0].Art);
        Assert.Equal(1, a[0].Von);
        Assert.Equal(2, a[0].Bis);
        Assert.Equal(8000m, a[0].AhvLohn);
        Assert.Equal(8000m, a[0].AlvLohn);
        Assert.Equal(0m, a[0].AlvFrei);

        Assert.Equal(ElmAhvJahreslohn.Art.Verzicht, a[1].Art);
        Assert.Equal(3, a[1].Von);
        Assert.Equal(12, a[1].Bis);
        Assert.Equal(40000m, a[1].AhvLohn);
        Assert.Equal(0m, a[1].AhvFrei);
        Assert.Equal(0m, a[1].AlvLohn);
        Assert.Equal(40000m, a[1].AlvFrei);
    }

    [Fact]
    public void Luecke_im_Jahr_verbindet_die_Monate_nicht()
    {
        var a = ElmAhvJahreslohn.Abschnitte(new[]
        {
            M(1, 1000m, ElmAhvJahreslohn.Art.Frei),
            M(3, 1000m, ElmAhvJahreslohn.Art.Frei),
        });
        Assert.Equal(2, a.Count);
        Assert.Equal(1, a[0].Bis);
        Assert.Equal(3, a[1].Von);
    }

    private static ElmAhvJahreslohn.Monat Z(int nr, decimal sv, decimal? ahv, decimal? alv, decimal? alvz, DateTime kette) =>
        new(nr, sv, 0m, ElmAhvJahreslohn.Art.Normal) { Kette = kette, Lohnzettel = new ElmAhvJahreslohn.Zeilen(ahv, alv, alvz) };

    [Fact]
    public void Lohn_nach_Austritt_zaehlt_zur_beendeten_Anstellung_mit_ALVZ_aus_dem_Lohnzettel()
    {
        // Referenz TF41 Meier Max: Mai-Bonus im Abschnitt 01.01.–31.03., Juli eigene Anstellung.
        var q1 = new DateTime(2025, 1, 1);
        var a = ElmAhvJahreslohn.Abschnitte(new[]
        {
            Z(1, 10000m, 10000m, 10000m, 0m, q1),
            Z(2, 30000m, 30000m, 14700m, 15300m, q1),
            Z(3, 10000m, 10000m, 12350m, -2350m, q1),
            Z(5, 30000m, 30000m, 0m, 30000m, q1),
            Z(7, 10000m, 10000m, 12350m, -2350m, new DateTime(2025, 7, 1)),
        });
        Assert.Equal(2, a.Count);
        Assert.Equal((1, 5), (a[0].Von, a[0].Bis));
        Assert.Equal(80000m, a[0].AhvLohn);
        Assert.Equal(37050m, a[0].AlvLohn);
        Assert.Equal(42950m, a[0].AlvzLohn);
        Assert.True(a[0].MitAlvz);
        Assert.Equal(0m, a[0].AlvFrei);
        Assert.Equal(7, a[1].Von);
    }

    [Fact]
    public void Ueber_dem_ALVZ_Hoechstlohn_steht_der_Rest_in_ALV_Open()
    {
        // Referenz TF04 Fankhauser Dezember 2025.
        var a = Assert.Single(ElmAhvJahreslohn.Abschnitte(new[] { Z(12, 420000m, 420000m, 148200m, 222300m, new DateTime(2025, 1, 1)) }));
        Assert.Equal(49500m, a.AlvFrei);
    }

    [Fact]
    public void Ohne_ALV_Zeile_gilt_der_Freibetrag_im_eigenen_Abschnitt()
    {
        // Referenz TF03 Lusser Pia ab Juni: 4700 Lohn, 1400 Freibetrag, ALV nur noch offen.
        var k = new DateTime(2025, 1, 1);
        var a = ElmAhvJahreslohn.Abschnitte(new[]
        {
            Z(5, 5500m, 5500m, 5500m, 0m, k),
            Z(6, 4700m, 3300m, null, null, k),
        });
        Assert.Equal(2, a.Count);
        Assert.Equal(ElmAhvJahreslohn.Art.Rentner, a[1].Art);
        Assert.Equal(3300m, a[1].AhvLohn);
        Assert.Equal(1400m, a[1].AhvFrei);
        Assert.Equal(0m, a[1].AlvLohn);
        Assert.Equal(4700m, a[1].AlvFrei);
        Assert.False(a[1].MitAlvz);
    }

    [Fact]
    public void Zeilen_kommen_aus_den_Kategorien_des_Lohnzettels()
    {
        var z = ElmAhvJahreslohn.AusLohnzetteln(new[]
        {
            "{\"abzugLines\":[{\"categoryCode\":\"AHV\",\"basis\":100.5},{\"categoryCode\":\"ALV\",\"basis\":90},{\"categoryCode\":\"KTG\",\"basis\":7}]}",
            null,
        });
        Assert.Equal(100.5m, z!.Value.Ahv);
        Assert.Equal(90m, z.Value.Alv);
        Assert.Null(z.Value.Alvz);
    }
}

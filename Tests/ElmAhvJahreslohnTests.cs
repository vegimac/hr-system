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
}

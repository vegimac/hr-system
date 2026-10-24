using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class AhvBeitragspflichtTests
{
    [Fact]
    public void Gesetzlich_Frauen_1961_bis_1964()
    {
        var g = AhvBeitragspflichtStand.Gesetzlich;
        Assert.Equal(64 * 12 + 3, g.ReferenzalterMonate("female", 1961));
        Assert.Equal(64 * 12 + 6, g.ReferenzalterMonate("weiblich", 1962));
        Assert.Equal(64 * 12 + 9, g.ReferenzalterMonate("f", 1963));
        Assert.Equal(65 * 12, g.ReferenzalterMonate("female", 1964));
        Assert.Equal(64 * 12, g.ReferenzalterMonate("female", 1960));
    }

    [Fact]
    public void Ende_Maenner_aus_dem_Kasten()
    {
        var stand = new AhvBeitragspflichtStand(18, 66, 65, AhvBeitragspflichtStand.Gesetzlich.Uebergaenge);
        Assert.Equal(66 * 12, stand.ReferenzalterMonate("male", 1960));
        Assert.Equal(66 * 12, PayrollCalculations.GetReferenzalterMonate("male", 1960, stand));
    }

    [Fact]
    public void Beginn_gilt_fuer_die_AHV_Zeile_mit_oberer_Grenze()
    {
        var stand = AhvBeitragspflichtStand.Gesetzlich with { Beginn = 19 };
        var normal = new DeductionRule { CategoryCode = "AHV", MinAge = 18, MaxAge = 64 };
        var rentner = new DeductionRule { CategoryCode = "AHV", MinAge = 65, MaxAge = null };
        var alv = new DeductionRule { CategoryCode = "ALV", MinAge = 18, MaxAge = 64 };

        Assert.False(PayrollCalculations.PasstSvAlter(normal, 18, stand));
        Assert.True(PayrollCalculations.PasstSvAlter(normal, 19, stand));
        Assert.False(PayrollCalculations.PasstSvAlter(rentner, 64, stand));
        Assert.True(PayrollCalculations.PasstSvAlter(rentner, 65, stand));
        Assert.False(PayrollCalculations.PasstSvAlter(alv, 17, stand));
        Assert.True(PayrollCalculations.PasstSvAlter(alv, 18, stand));
    }
}

using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class LohnausweisBemerkungsSchriftTests
{
    const float Zeile15 = 456.38f;

    [Fact]
    public void Kurze_Zeile_bleibt_bei_9_Punkt()
    {
        var g = LohnausweisPdfService.BemerkungsSchrift(
            "Spesenreglement durch Kanton LU am 15.04.2021 genehmigt", Zeile15);
        Assert.Equal(9f, g);
    }

    [Fact]
    public void Zwei_Genehmigungen_in_einer_Zeile_werden_kleiner()
    {
        var g = LohnausweisPdfService.BemerkungsSchrift(
            "Expatriateruling durch Kanton LU am 15.04.2021 genehmigt · Verkehrswert durch Kanton LU am 15.09.2010 genehmigt",
            Zeile15);
        Assert.True(g < 9f, "Schrift " + g);
        Assert.True(g >= 6f, "Schrift " + g);
    }
}

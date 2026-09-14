using Xunit;
using HrSystem.Services;

namespace HrSystem.Tests;

public class VerwarnungPdfTests
{
    [Fact]
    public void KuerzeBemerkung_LangeTexte_AufEineSeite()
    {
        Assert.Null(VerwarnungPdfService.KuerzeBemerkung("  "));
        Assert.Equal("kurz", VerwarnungPdfService.KuerzeBemerkung("kurz"));
        var lang = new string('x', 900);
        var k = VerwarnungPdfService.KuerzeBemerkung(lang);
        Assert.NotNull(k);
        Assert.True(k!.Length <= 480);
        Assert.EndsWith("…", k);
    }
}

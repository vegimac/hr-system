using HrSystem.Controllers;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Ort-Vergleich im Mirus-Adressvergleich: Kantons-Suffix ignorieren.
/// </summary>
public class MirusAddressCityCompareTests
{
    [Theory]
    [InlineData("Roggwil (BE)", "Roggwil", true)]
    [InlineData("Roggwil BE", "Roggwil", true)]
    [InlineData("Roggwil", "Roggwil", true)]
    [InlineData("Herzogebuchsee", "Herzogenbuchsee", false)]
    [InlineData("Bützberg", "Thunstetten", false)]
    public void NormCity_Vergleich(string a, string b, bool expectSame)
    {
        var na = MirusAddressCompareController.NormCityForTest(a);
        var nb = MirusAddressCompareController.NormCityForTest(b);
        Assert.Equal(expectSame, na == nb && na.Length > 0);
    }

    [Theory]
    [InlineData("c/o ORS Service AG", "c/o ORS Service AG / Lyssachstrasse 23", true)]
    [InlineData("c/o ORS Service AG / Lyssachstrasse 23", "c/o ORS Service AG", true)]
    [InlineData("Waldhofstrasse 9", "c/o ORS Service AG", false)]
    [InlineData("", "c/o ORS Service AG", false)]
    public void SoftStreet_Zusatzadresse(string a, string b, bool expect)
        => Assert.Equal(expect, MirusAddressCompareController.SoftStreetMatchForTest(a, b));
}

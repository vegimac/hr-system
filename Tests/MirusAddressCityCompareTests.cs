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
}

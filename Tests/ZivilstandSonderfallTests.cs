using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// easy@work gewinnt bei normalen Zivilständen, OneCrew bei Sonderfällen, die easy@work nicht kennt
/// (Walter 02.11.2026, TF12 Casanova: durch Verschollenerklärung aufgelöste Partnerschaft).
/// </summary>
public class ZivilstandSonderfallTests
{
    [Theory]
    [InlineData("aufgeloeste_partnerschaft_verschollen")]
    [InlineData("aufgeloeste_partnerschaft_tod")]
    [InlineData("aufgeloeste_partnerschaft")]
    [InlineData("unbekannt")]
    public void Sonderfall_BleibtInOneCrew(string z)
        => Assert.True(ZivilstandHistorieService.IstOneCrewSonderfall(z));

    [Theory]
    [InlineData("ledig")]
    [InlineData("Verheiratet")]
    [InlineData("geschieden")]
    [InlineData("verwitwet")]
    [InlineData("getrennt")]
    [InlineData("eingetragene_partnerschaft")]
    [InlineData("konkubinat")]
    [InlineData(null)]
    [InlineData("")]
    public void EasyAtWorkKennt_EasyGewinnt(string? z)
        => Assert.False(ZivilstandHistorieService.IstOneCrewSonderfall(z));

    [Fact]
    public void JederEasyCode_IstKeinSonderfall()
    {
        // Was MapMaritalStatus aus easy@work liefert, darf nie als Sonderfall gelten.
        foreach (var code in new[] { "m", "s", "d", "w", "e", "p", "c" })
        {
            var z = HrSystem.Services.EasyAtWork.EasyAtWorkEmployeeSyncService.MapMaritalStatus(code);
            Assert.False(ZivilstandHistorieService.IstOneCrewSonderfall(z), code);
        }
    }
}

using HrSystem.Services;
using HrSystem.Services.LohnFormelwerk;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Verwendete Mirus-Lohnarten → Swissdec (Walter 21.10.2026).
/// </summary>
public class MirusSwissdecZuordnungTests
{
    [Theory]
    [InlineData("10.1", "1000")]
    [InlineData("10.2", "1000")]
    [InlineData("10.3", "1000")]
    [InlineData("10.5", "1000")]
    [InlineData("20.1", "1005")]
    [InlineData("55.3", "1005")]
    [InlineData("55.1", "1061")]
    [InlineData("55.10", "1076")]
    [InlineData("40.1", "1160")]
    [InlineData("195.5", "1160")]
    [InlineData("50.1", "1161")]
    [InlineData("195.2", "1161")]
    [InlineData("180.1", "1200")]
    [InlineData("200.5", "1210")]
    [InlineData("190.1", "3000")]
    [InlineData("190.2", "3000")]
    [InlineData("70.1", "1301")]
    [InlineData("70.2", "2035")]
    [InlineData("60.2", "1300")]
    [InlineData("60.3", "2030")]
    [InlineData("65.2", "2050")]
    [InlineData("75.2", "2050")]
    [InlineData("120.1", "2001")]
    [InlineData("120.2", "2002")]
    [InlineData("80.3", "2000")]
    [InlineData("565.1", "5102")]
    [InlineData("600.5", "5212")]
    [InlineData("600.24", "600.24")]
    [InlineData("950.1", "950.1")]
    public void Verwendete_Mirus_zeigen_auf_Swissdec(string mirus, string swissdec)
        => Assert.Equal(swissdec, LohnCodeAlias.Normiere(mirus));

    [Fact]
    public void Erfundene_Suffixe_sind_keine_eigenen_Ziele()
    {
        Assert.Equal("1000", LohnCodeAlias.Normiere("1000.2"));
        Assert.Equal("1005", LohnCodeAlias.Normiere("1005.3m"));
        Assert.Equal("1161", LohnCodeAlias.Normiere("1161.50"));
        Assert.False(LohnCodes.IstSwissdecStammNr("1000.2"));
        Assert.True(LohnCodes.IstSwissdecStammNr("1000"));
        Assert.True(LohnCodes.IstSaldoVortrag("901"));
        Assert.True(LohnCodes.IstSaldoVortrag("906"));
        Assert.False(LohnCodes.IstSaldoVortrag("1000"));
        Assert.False(LohnCodes.IstSaldoVortrag("600.24"));
    }

    [Fact]
    public void Beleg_nimmt_Swissdec_statt_Mirus()
    {
        Assert.Equal("1000", LohnCodes.SwissdecVon("10.1"));
        Assert.Equal("3000", LohnCodes.SwissdecVon("190.1"));
        Assert.Equal("1000", LohnCodes.SwissdecVon("1000"));
    }

    [Fact]
    public void SwissdecZuordnung_stimmt_mit_Alias_ueberein()
    {
        foreach (var (mirus, swiss) in new[]
        {
            ("10.2", "1000"), ("55.3", "1005"), ("195.6", "1160"), ("565.1", "5102")
        })
        {
            Assert.Equal(swiss, SwissdecLohnartZuordnung.FuerOneCrewCode(mirus));
            Assert.Equal(swiss, LohnCodeAlias.Normiere(mirus));
        }
    }
}

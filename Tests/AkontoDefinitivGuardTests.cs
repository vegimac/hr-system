using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class AkontoDefinitivGuardTests
{
    [Theory]
    [InlineData("OFFEN", "offen", true)]
    [InlineData("AUSBEZAHLT", "offen", true)]
    [InlineData("UEBERSPRUNGEN", "offen", true)]
    [InlineData("IN_BEARBEITUNG_GF", "offen", false)]
    [InlineData("BEI_HR", "offen", false)]
    [InlineData("IN_BEARBEITUNG_GF", "provisorisch_abgeschlossen", true)]
    [InlineData("BEI_HR", "abgeschlossen", true)]
    public void AkontoStrangFertig(string ak, string def, bool expected)
        => Assert.Equal(expected, AkontoDefinitivGuard.IsAkontoStrangFertig(ak, def));

    [Fact]
    public void PeriodeKomplett_Requires_Definitiv_Abgeschlossen()
    {
        Assert.False(AkontoDefinitivGuard.IsPeriodeKomplett("AUSBEZAHLT", "provisorisch_abgeschlossen"));
        Assert.True(AkontoDefinitivGuard.IsPeriodeKomplett("OFFEN", "abgeschlossen"));
        Assert.True(AkontoDefinitivGuard.IsPeriodeKomplett("IN_BEARBEITUNG_GF", "abgeschlossen"));
    }
}

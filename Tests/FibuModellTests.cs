using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Fibu ohne Kostenstellen (Walter 19.10.2026): Vertrags-Modell → Gruppe / Sollkonto.
/// CREW = MTP+FIX → 4000, FLEX → 4001, MGMT = FIX-M → 4055.
/// </summary>
public class FibuModellTests
{
    [Theory]
    [InlineData("MTP", FibuModell.Crew)]
    [InlineData("FIX", FibuModell.Crew)]
    [InlineData("FLEX", FibuModell.Flex)]
    [InlineData("FIX-M", FibuModell.Mgmt)]
    [InlineData("fix-m", FibuModell.Mgmt)]
    [InlineData(null, FibuModell.Crew)]
    [InlineData("", FibuModell.Crew)]
    [InlineData("UNBEKANNT", FibuModell.Crew)]
    public void Gruppe_AusVertragsmodell(string? model, string erwartet)
        => Assert.Equal(erwartet, FibuModell.Gruppe(model));

    [Theory]
    [InlineData("MTP", "4000")]
    [InlineData("FIX", "4000")]
    [InlineData("FLEX", "4001")]
    [InlineData("FIX-M", "4055")]
    public void AufwandSoll_ProModell(string model, string konto)
        => Assert.Equal(konto, FibuModell.AufwandSoll(model));

    [Theory]
    [InlineData("200", FibuModell.Flex)]
    [InlineData("300", FibuModell.Mgmt)]
    [InlineData("400", FibuModell.Mgmt)]
    [InlineData("100", FibuModell.Crew)]
    [InlineData(null, null)]
    [InlineData("999", null)]
    public void GruppeAusLegacyKst(string? kst, string? erwartet)
        => Assert.Equal(erwartet, FibuModell.GruppeAusLegacyKst(kst));

    [Theory]
    [InlineData(FibuModell.Flex, "200")]
    [InlineData(FibuModell.Mgmt, "300")]
    [InlineData(FibuModell.Crew, "100")]
    public void LegacyKostenstelle_NurFallback(string gruppe, string kst)
        => Assert.Equal(kst, FibuModell.LegacyKostenstelle(gruppe));

    [Fact]
    public void Bezeichnung_Klartext()
    {
        Assert.Equal("Crew Flex", FibuModell.Bezeichnung("FLEX"));
        Assert.Equal("Management", FibuModell.Bezeichnung("FIX-M"));
        Assert.Equal("Crew Fix / MTP", FibuModell.Bezeichnung("MTP"));
    }
}

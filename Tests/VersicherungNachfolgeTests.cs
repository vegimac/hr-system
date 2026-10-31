using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class VersicherungNachfolgeTests
{
    [Fact]
    public void Loeschen_des_Nachfolgers_oeffnet_den_Vorgaenger()
    {
        var vonNeu = new DateOnly(2026, 1, 1);
        Assert.True(VersicherungNachfolge.WurdeDurchNachfolgerBeendet(new DateOnly(2025, 12, 31), vonNeu));
        Assert.Null(VersicherungNachfolge.EndeNachLoeschen(null, null));
    }

    [Fact]
    public void Ein_spaeterer_Eintrag_bleibt_die_Grenze()
    {
        var ende = VersicherungNachfolge.EndeNachLoeschen(null, new DateOnly(2026, 7, 1));
        Assert.Equal(new DateOnly(2026, 6, 30), ende);
    }

    [Fact]
    public void Wieder_oeffnen_scheitert_an_einem_Nachfolger()
    {
        var von = new DateOnly(2025, 1, 1);
        Assert.True(VersicherungNachfolge.BlockiertWiederOeffnen(von, null));
        Assert.False(VersicherungNachfolge.BlockiertWiederOeffnen(von, new DateOnly(2024, 12, 31)));
    }

    [Fact]
    public void Ahv_schliesst_nur_denselben_Code()
    {
        Assert.True(VersicherungNachfolge.GleicherSchluss("AHV", "SONDERFALL", "SONDERFALL"));
        Assert.False(VersicherungNachfolge.GleicherSchluss("AHV", "SONDERFALL", "VERZICHT"));
        Assert.True(VersicherungNachfolge.GleicherSchluss("UVG", "A1", "B0"));
    }
}

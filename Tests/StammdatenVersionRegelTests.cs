using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class StammdatenVersionRegelTests
{
    private static readonly DateOnly Heute = new(2026, 10, 1);

    [Fact]
    public void Zeitlage_beginnt_morgen_ist_geplant()
        => Assert.Equal(StammdatenVersionRegel.Geplant,
            StammdatenVersionRegel.Zeitlage(new DateOnly(2026, 10, 2), null, Heute));

    [Fact]
    public void Zeitlage_beginnt_heute_ist_aktuell()
        => Assert.Equal(StammdatenVersionRegel.Aktuell,
            StammdatenVersionRegel.Zeitlage(Heute, null, Heute));

    [Fact]
    public void Zeitlage_endet_heute_ist_noch_aktuell()
        => Assert.Equal(StammdatenVersionRegel.Aktuell,
            StammdatenVersionRegel.Zeitlage(new DateOnly(2026, 1, 1), Heute, Heute));

    [Fact]
    public void Zeitlage_endete_gestern_ist_vergangen()
        => Assert.Equal(StammdatenVersionRegel.Vergangen,
            StammdatenVersionRegel.Zeitlage(new DateOnly(2025, 1, 1), new DateOnly(2026, 9, 30), Heute));

    [Theory]
    [InlineData(StammdatenVersionRegel.Geplant, false, true)]
    [InlineData(StammdatenVersionRegel.Aktuell, false, true)]
    [InlineData(StammdatenVersionRegel.Aktuell, true, false)]
    [InlineData(StammdatenVersionRegel.Vergangen, false, false)]
    [InlineData(StammdatenVersionRegel.Vergangen, true, false)]
    public void Bearbeitbar_nach_Zeitlage_und_Lohnlauf(string zeitlage, bool inLohn, bool erwartet)
        => Assert.Equal(erwartet, StammdatenVersionRegel.Bearbeitbar(zeitlage, inLohn));

    [Fact]
    public void Sperrgrund_vergangen_geht_vor_Lohnlauf()
        => Assert.Contains("abgelaufen", StammdatenVersionRegel.Sperrgrund(StammdatenVersionRegel.Vergangen, true));

    [Fact]
    public void Sperrgrund_leer_wenn_bearbeitbar()
        => Assert.Null(StammdatenVersionRegel.Sperrgrund(StammdatenVersionRegel.Aktuell, false));

    [Fact]
    public void Neue_Version_frühestens_morgen()
    {
        Assert.False(StammdatenVersionRegel.NeuerStartErlaubt(Heute, Heute));
        Assert.False(StammdatenVersionRegel.NeuerStartErlaubt(new DateOnly(2026, 9, 1), Heute));
        Assert.True(StammdatenVersionRegel.NeuerStartErlaubt(new DateOnly(2026, 10, 2), Heute));
    }
}

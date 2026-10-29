using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class LohnausweisFassungTests
{
    private sealed record Zeile(int Ab, int? Bis);

    [Fact]
    public void FuerJahr_nimmt_die_offene_Fassung()
    {
        var alle = new[] { new Zeile(2000, null) };
        var treffer = LohnausweisFassung.FuerJahr(alle, 2025, z => z.Ab, z => z.Bis);
        Assert.Equal(2000, treffer!.Ab);
    }

    [Fact]
    public void FuerJahr_nimmt_die_Fassung_des_Jahres_und_nicht_die_spaetere()
    {
        var alle = new[] { new Zeile(2000, 2025), new Zeile(2026, null) };
        Assert.Equal(2000, LohnausweisFassung.FuerJahr(alle, 2024, z => z.Ab, z => z.Bis)!.Ab);
        Assert.Equal(2026, LohnausweisFassung.FuerJahr(alle, 2026, z => z.Ab, z => z.Bis)!.Ab);
    }

    [Fact]
    public void Spaeteres_Jahr_schliesst_die_alte_Fassung_per_Ende_des_Vorjahres()
    {
        var fehler = LohnausweisFassung.Entscheiden(2000, 2027, out var bis);
        Assert.Null(fehler);
        Assert.Equal(2026, bis);
    }

    [Fact]
    public void Gleiches_Jahr_schreibt_die_offene_Fassung_fort()
    {
        var fehler = LohnausweisFassung.Entscheiden(2026, 2026, out var bis);
        Assert.Null(fehler);
        Assert.Null(bis);
    }

    [Fact]
    public void Vorversion_ist_die_juengste_vor_der_offenen()
    {
        var alle = new[] { new Zeile(2000, 2025), new Zeile(2026, null) };
        var vor = LohnausweisFassung.Vorversion(alle, 2026, z => z.Ab);
        Assert.Equal(2000, vor!.Ab);
    }

    [Fact]
    public void Frueheres_Jahr_bleibt_geschlossen()
    {
        var fehler = LohnausweisFassung.Entscheiden(2026, 2024, out _);
        Assert.NotNull(fehler);
    }
}

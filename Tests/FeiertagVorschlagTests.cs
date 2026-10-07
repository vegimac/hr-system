using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>Feiertage pro Filiale und Jahresvorschlag (Walter 07.10.2026).</summary>
public class FeiertagVorschlagTests
{
    [Theory]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2028, 4, 16)]
    public void Ostern(int jahr, int monat, int tag) =>
        Assert.Equal(new DateOnly(jahr, monat, tag), FeiertagVorschlag.Ostern(jahr));

    [Fact]
    public void Bewegliche_Feiertage_2026()
    {
        var k = FeiertagVorschlag.Fuer(2026, "AG").ToDictionary(x => x.Bezeichnung);
        Assert.Equal(new DateOnly(2026, 4, 3), k["Karfreitag"].Datum);
        Assert.Equal(new DateOnly(2026, 5, 14), k["Auffahrt"].Datum);
        Assert.Equal(new DateOnly(2026, 5, 25), k["Pfingstmontag"].Datum);
        Assert.Equal(new DateOnly(2026, 6, 4), k["Fronleichnam"].Datum);
        Assert.False(k["Fronleichnam"].Vorgewaehlt);
        Assert.True(k["Bundesfeiertag"].Sonntagsgleich && k["Bundesfeiertag"].National);
    }

    [Fact]
    public void Katholischer_Kanton_hat_Fronleichnam_vorgewaehlt()
    {
        var k = FeiertagVorschlag.Fuer(2026, "LU").ToDictionary(x => x.Bezeichnung);
        Assert.True(k["Fronleichnam"].Vorgewaehlt);
        Assert.True(k["Allerheiligen"].Vorgewaehlt);
    }

    [Fact]
    public void Geltung_national_kanton_filiale()
    {
        var nat = new DienstplanFeiertag { Scope = "NATIONAL" };
        var ag = new DienstplanFeiertag { Scope = "KANTON", KantonCode = "AG" };
        var fil = new DienstplanFeiertag { Scope = "FILIALE", CompanyProfileId = 7 };
        Assert.True(FeiertagVorschlag.GiltFuer(nat, 7, "LU"));
        Assert.True(FeiertagVorschlag.GiltFuer(ag, 3, "ag"));
        Assert.False(FeiertagVorschlag.GiltFuer(ag, 3, "LU"));
        Assert.True(FeiertagVorschlag.GiltFuer(fil, 7, null));
        Assert.False(FeiertagVorschlag.GiltFuer(fil, 8, null));
    }

    [Fact]
    public void Erster_August_immer_sonntagsgleich()
    {
        Assert.True(FeiertagVorschlag.IstSonntagsgleich(new DienstplanFeiertag { Datum = new DateOnly(2026, 8, 1) }));
        Assert.False(FeiertagVorschlag.IstSonntagsgleich(new DienstplanFeiertag { Datum = new DateOnly(2026, 12, 25) }));
    }
}

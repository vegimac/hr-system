using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
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

    static string[] Sonntagsgleich(string kanton) =>
        FeiertagVorschlag.Fuer(2026, kanton).Where(x => x.Sonntagsgleich).Select(x => x.Bezeichnung).ToArray();

    [Theory]
    [InlineData("AG")]
    [InlineData("BE")]
    public void Aargau_und_Bern_acht_Tage_plus_erster_August(string kanton) =>
        Assert.Equal(new[] { "Neujahr", "Berchtoldstag", "Karfreitag", "Ostermontag", "Auffahrt", "Pfingstmontag",
                             "Bundesfeiertag", "Weihnachten", "Stephanstag" }, Sonntagsgleich(kanton));

    [Fact]
    public void Luzern_acht_Tage_plus_erster_August()
    {
        Assert.Equal(new[] { "Neujahr", "Karfreitag", "Auffahrt", "Fronleichnam", "Bundesfeiertag",
                             "Mariä Himmelfahrt", "Allerheiligen", "Weihnachten", "Stephanstag" }, Sonntagsgleich("LU"));
        var k = FeiertagVorschlag.Fuer(2026, "LU").ToDictionary(x => x.Bezeichnung);
        Assert.False(k["Berchtoldstag"].Vorgewaehlt);
        Assert.True(k["Ostermontag"].Vorgewaehlt && !k["Ostermontag"].Sonntagsgleich);
        Assert.True(k["Mariä Empfängnis"].Vorgewaehlt && !k["Mariä Empfängnis"].Sonntagsgleich);
        Assert.False(k["Josefstag"].Vorgewaehlt);
    }

    [Fact]
    public void Kanton_ohne_Liste_nur_erster_August_sonntagsgleich()
    {
        Assert.Equal(new[] { "Bundesfeiertag" }, Sonntagsgleich("ZH"));
        Assert.False(FeiertagVorschlag.KantonslisteVorhanden("ZH"));
        Assert.Contains("keine Liste", FeiertagVorschlag.Hinweis("ZH"));
    }

    static AppDbContext NeueDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase("FeiertagAltbestand_" + Guid.NewGuid()).Options);

    static DienstplanFeiertag National(int m, int t, string name) =>
        new() { Datum = new DateOnly(2026, m, t), Bezeichnung = name, Scope = "NATIONAL" };

    [Fact]
    public async Task Altbestand_wird_auf_Kantone_verteilt()
    {
        using var db = NeueDb();
        db.CompanyProfiles.AddRange(
            new CompanyProfile { Id = 1, BranchName = "Seedorf", CompanyName = "Test", KantonCode = "AG" },
            new CompanyProfile { Id = 2, BranchName = "Bergwil", CompanyName = "Test", KantonCode = "LU" },
            new CompanyProfile { Id = 3, BranchName = "Talheim", CompanyName = "Test", KantonCode = "BE" });
        db.DienstplanFeiertage.AddRange(
            National(1, 1, "Neujahr"), National(1, 2, "Berchtoldstag"), National(4, 6, "Ostermontag"),
            National(8, 1, "Bundesfeier"),
            new DienstplanFeiertag { Datum = new DateOnly(2026, 6, 4), Bezeichnung = "Fronleichnam", Scope = "KANTON", KantonCode = "LU" },
            new DienstplanFeiertag { Datum = new DateOnly(2026, 12, 8), Bezeichnung = "Mariä Empfängnis", Scope = "KANTON", KantonCode = "LU" });
        await db.SaveChangesAsync();

        var r = await FeiertagAltbestand.KantonslistenAnwendenAsync(db);

        var alle = await db.DienstplanFeiertage.ToListAsync();
        Assert.False(r.Uebersprungen);
        Assert.True(alle.Single(x => x.Bezeichnung == "Neujahr").Sonntagsgleich);
        Assert.Equal("NATIONAL", alle.Single(x => x.Bezeichnung == "Neujahr").Scope);

        var berchtold = alle.Where(x => x.Bezeichnung == "Berchtoldstag").ToList();
        Assert.Equal(new[] { "AG", "BE" }, berchtold.Select(x => x.KantonCode).OrderBy(x => x));
        Assert.All(berchtold, x => Assert.True(x.Scope == "KANTON" && x.Sonntagsgleich));

        var ostern = alle.Where(x => x.Bezeichnung == "Ostermontag").ToDictionary(x => x.KantonCode!);
        Assert.Equal(3, ostern.Count);
        Assert.True(ostern["AG"].Sonntagsgleich && ostern["BE"].Sonntagsgleich);
        Assert.False(ostern["LU"].Sonntagsgleich);

        Assert.True(alle.Single(x => x.Bezeichnung == "Fronleichnam").Sonntagsgleich);
        Assert.False(alle.Single(x => x.Bezeichnung == "Mariä Empfängnis").Sonntagsgleich);
        Assert.Equal(2, r.Aufgeteilt);
    }

    [Fact]
    public async Task Altbestand_unbekannter_Kanton_laesst_nationale_Eintraege_stehen()
    {
        using var db = NeueDb();
        db.CompanyProfiles.AddRange(
            new CompanyProfile { Id = 1, BranchName = "Seedorf", CompanyName = "Test", KantonCode = "AG" },
            new CompanyProfile { Id = 2, BranchName = "Flusstal", CompanyName = "Test", KantonCode = "ZH" });
        db.DienstplanFeiertage.Add(National(1, 2, "Berchtoldstag"));
        await db.SaveChangesAsync();

        var r = await FeiertagAltbestand.KantonslistenAnwendenAsync(db);

        Assert.True(r.Uebersprungen);
        var f = await db.DienstplanFeiertage.SingleAsync();
        Assert.Equal("NATIONAL", f.Scope);
        Assert.False(f.Sonntagsgleich);
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

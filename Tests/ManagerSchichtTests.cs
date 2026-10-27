using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Arbeitszeit-Verstösse: Manager der Schicht = FIX-M-Stempel, der die
/// Verstosszeit überlappt. Mutterschutz 17:30–20:47 zählt den Manager
/// von 16:00–23:00, nicht die Frühschicht bis 14:00.
/// </summary>
public class ManagerSchichtTests
{
    static DateTime Um(int h, int min, int tag = 8) => new(2026, 10, tag, h, min, 0);

    [Fact]
    public void Spaetdienst_trifft_den_Abendverstoss()
    {
        var verstoss = new[] { new ManagerSchicht.Fenster(Um(17, 30), Um(20, 47)) };
        var schichten = new[]
        {
            new ManagerSchicht.Schicht(1, Um(6, 0), Um(14, 0)),
            new ManagerSchicht.Schicht(2, Um(16, 0), Um(23, 0)),
        };

        var dabei = ManagerSchicht.Dabei(verstoss, schichten);

        Assert.Equal(new[] { 2 }, dabei);
    }

    [Fact]
    public void Uebergabe_nennt_beide_Manager()
    {
        var verstoss = new[] { new ManagerSchicht.Fenster(Um(17, 30), Um(20, 47)) };
        var schichten = new[]
        {
            new ManagerSchicht.Schicht(2, Um(11, 0), Um(21, 0)),
            new ManagerSchicht.Schicht(3, Um(20, 0), Um(2, 0).AddDays(1)),
        };

        var dabei = ManagerSchicht.Dabei(verstoss, schichten);

        Assert.Equal(new[] { 2, 3 }, dabei.OrderBy(id => id));
    }

    [Fact]
    public void Genau_beim_Beginn_gegangen_zaehlt_nicht()
    {
        var verstoss = new[] { new ManagerSchicht.Fenster(Um(17, 30), Um(20, 47)) };
        var schichten = new[] { new ManagerSchicht.Schicht(1, Um(9, 0), Um(17, 30)) };

        Assert.Empty(ManagerSchicht.Dabei(verstoss, schichten));
    }

    [Fact]
    public void Ohne_Stempel_am_Verstoss_kein_Manager()
    {
        var schichten = new[] { new ManagerSchicht.Schicht(2, Um(16, 0), Um(23, 0)) };
        Assert.Empty(ManagerSchicht.Dabei(Array.Empty<ManagerSchicht.Fenster>(), schichten));
    }

    [Fact]
    public void Filter_behaelt_nur_die_Schicht_des_Managers()
    {
        var anna = new StempelManager(2, "Anna", "Müller");
        var luca = new StempelManager(3, "Luca", "Keller");
        var zeileAnna = new StempelVerstossZeile("MUTTER", "Mutterschutz", new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 8),
            "Abend", 1, 0, "ArGV1", new List<StempelZeit>(), new List<StempelManager> { anna });
        var zeileBeide = zeileAnna with { Manager = new List<StempelManager> { anna, luca } };
        var zeileOhne = zeileAnna with { Manager = new List<StempelManager>() };
        var daten = new StempelVerstoesseDaten("Reinach", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 27), 40,
            new List<StempelVerstossArt> { new("MUTTER", "Mutterschutz", "Art. 61", 3) },
            new List<StempelVerstossMa>
            {
                new(10, "1", "Sara", "Frei", new List<StempelVerstossZeile> { zeileAnna, zeileOhne }),
                new(11, "2", "Mia", "Horn", new List<StempelVerstossZeile> { zeileBeide }),
            },
            new List<string>(), ManagerAuswahl: new List<StempelManager> { anna, luca });

        var nurAnna = daten.FuerManager(2);
        Assert.Equal("Anna Müller", nurAnna.ManagerFilter);
        Assert.Equal(2, nurAnna.Mitarbeiter.Sum(m => m.Verstoesse.Count));
        Assert.Equal(2, nurAnna.ProArt[0].Anzahl);
        Assert.All(nurAnna.Mitarbeiter.SelectMany(m => m.Verstoesse), v => Assert.Contains(v.Manager!, x => x.Id == 2));

        var ohne = daten.FuerManager(0);
        Assert.Equal("kein Manager gestempelt", ohne.ManagerFilter);
        Assert.Single(ohne.Mitarbeiter);
        Assert.Single(ohne.Mitarbeiter[0].Verstoesse);
    }
}

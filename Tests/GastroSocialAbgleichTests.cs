using HrSystem.Services;
using Xunit;
using Z = HrSystem.Services.GastroSocialAbgleich.Zeile;
using M = HrSystem.Services.GastroSocialAbgleich.Ma;

namespace HrSystem.Tests;

public class GastroSocialAbgleichTests
{
    private static readonly DateTime Heute = new(2026, 10, 11);

    private static Z Gs(string vor, string name, string? ahv, DateTime? geb = null, string status = "Beschäftigt", string? lst = null)
        => new() { Nr = 2, Vorname = vor, Nachname = name, Ahv = ahv, Geburtsdatum = geb, Status = status, Leistungen = lst };

    private static M Oc(int id, string vor, string name, string? ahv, bool aktiv = true, bool inFiliale = true,
        DateTime? geb = null, DateTime? austritt = null, string? andere = null, bool ohneLohn = false)
        => new()
        {
            Id = id, Vorname = vor, Nachname = name, Ahv = ahv, Geburtsdatum = geb,
            InFiliale = inFiliale, AktivInFiliale = aktiv, AustrittFiliale = austritt,
            AktivInAndererFiliale = andere, OhneLohn = ohneLohn,
        };

    [Fact]
    public void Passt_per_AHV_auch_mit_anderer_Schreibweise()
    {
        var r = GastroSocialAbgleich.Vergleiche(
            new[] { Gs("Anna", "Muster", "756.1234.5678.97") },
            new[] { Oc(1, "Anna", "Muster-Meier", "7561234567897") }, Heute);
        Assert.Single(r.Gemeldet);
        Assert.Equal("AHV", r.Gemeldet[0].MatchArt);
        Assert.Empty(r.NichtGemeldet);
        Assert.Empty(r.Abmelden);
    }

    [Fact]
    public void Ausgetreten_mit_FZ_kommt_in_Abmelden()
    {
        var r = GastroSocialAbgleich.Vergleiche(
            new[] { Gs("Anna", "Muster", "756.1234.5678.97", lst: "FZ") },
            new[] { Oc(1, "Anna", "Muster", "756.1234.5678.97", aktiv: false, austritt: new DateTime(2026, 8, 31)) }, Heute);
        var t = Assert.Single(r.Abmelden);
        Assert.Contains("31.08.2026", t.Hinweis);
        Assert.Equal("FZ", t.Leistungen);
    }

    [Fact]
    public void Uebertritt_in_andere_Filiale_kommt_in_Abmelden()
    {
        var r = GastroSocialAbgleich.Vergleiche(
            new[] { Gs("Anna", "Muster", "756.1234.5678.97") },
            new[] { Oc(1, "Anna", "Muster", "756.1234.5678.97", aktiv: false, inFiliale: false, andere: "122 Sursee") }, Heute);
        var t = Assert.Single(r.Abmelden);
        Assert.Contains("122 Sursee", t.Hinweis);
    }

    [Fact]
    public void Aktiv_ohne_Zeile_ist_nicht_gemeldet_Phantom_nicht()
    {
        var r = GastroSocialAbgleich.Vergleiche(
            new[] { Gs("Anna", "Muster", "756.1234.5678.97") },
            new[]
            {
                Oc(1, "Anna", "Muster", "756.1234.5678.97"),
                Oc(2, "Ben", "Neu", "756.9999.9999.91"),
                Oc(3, "Phan", "Tom", null, ohneLohn: true),
                Oc(4, "Alt", "Weg", null, aktiv: false),
            }, Heute);
        var t = Assert.Single(r.NichtGemeldet);
        Assert.Equal(2, t.EmployeeId);
    }

    [Fact]
    public void Ohne_Treffer_ist_nicht_zuordenbar()
    {
        var r = GastroSocialAbgleich.Vergleiche(
            new[] { Gs("Fremd", "Person", "756.1111.2222.33") },
            new[] { Oc(1, "Anna", "Muster", "756.1234.5678.97") }, Heute);
        Assert.Single(r.NichtZuordenbar);
        Assert.Single(r.NichtGemeldet);
    }

    [Fact]
    public void Name_und_Geburtsdatum_wenn_AHV_abweicht_mit_Hinweis()
    {
        var geb = new DateTime(1990, 5, 1);
        var r = GastroSocialAbgleich.Vergleiche(
            new[] { Gs("L'udmila", "Nováková", "756.1111.2222.33", geb) },
            new[] { Oc(1, "Ľudmila", "Novakova", "756.1234.5678.97", geb: geb) }, Heute);
        var t = Assert.Single(r.Gemeldet);
        Assert.Equal("NAME_GEB", t.MatchArt);
        Assert.Contains("weicht ab", t.Hinweis);
    }

    [Fact]
    public void Bei_GastroSocial_ausgetreten_aber_aktiv_ist_nicht_gemeldet()
    {
        var r = GastroSocialAbgleich.Vergleiche(
            new[] { Gs("Anna", "Muster", "756.1234.5678.97", status: "Ausgetreten") },
            new[] { Oc(1, "Anna", "Muster", "756.1234.5678.97") }, Heute);
        var t = Assert.Single(r.NichtGemeldet);
        Assert.Contains("neu anmelden", t.Hinweis);
        Assert.Empty(r.Gemeldet);
    }
}

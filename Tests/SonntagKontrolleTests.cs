using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Freie Sonntage (Walter 12.10.2026): 4 pro Jahr, mit Familienbetreuung 12,
/// anteilig; frei = kein Stempel und keine Absenz am Sonntag.
/// </summary>
public class SonntagKontrolleTests
{
    static readonly DateOnly Jan1 = new(2026, 1, 1);

    static SonntagKontrolle.Ergebnis R(
        DateOnly stichtag,
        DateOnly? datenAb = null,
        (DateOnly, DateOnly?)? anstellung = null,
        (DateOnly, DateOnly?)? familie = null,
        IEnumerable<DateOnly>? stempel = null,
        IEnumerable<(DateOnly, DateOnly)>? absenzen = null)
        => SonntagKontrolle.Berechne(new SonntagKontrolle.Eingabe(
            2026, stichtag, datenAb ?? Jan1,
            new List<(DateOnly, DateOnly?)> { anstellung ?? (new DateOnly(2020, 1, 1), null) },
            familie,
            (stempel ?? []).ToHashSet(),
            (absenzen ?? []).ToList()));

    static IEnumerable<DateOnly> Sonntage(DateOnly von, DateOnly bis)
    {
        for (var d = von; d <= bis; d = d.AddDays(1))
            if (d.DayOfWeek == DayOfWeek.Sunday) yield return d;
    }

    [Fact]
    public void GanzesJahr_Soll4_Familie12()
    {
        Assert.Equal(4, R(new DateOnly(2026, 12, 31)).Soll);
        Assert.Equal(12, R(new DateOnly(2026, 12, 31), familie: (new DateOnly(2020, 1, 1), null)).Soll);
    }

    [Fact]
    public void EintrittMitteJahr_Anteilig()
    {
        var r = R(new DateOnly(2026, 12, 31), anstellung: (new DateOnly(2026, 7, 1), null));
        Assert.Equal(2, r.Soll);   // 184/365 × 4 = 2.02
    }

    [Fact]
    public void FamilieHalbesJahr_MischSatz()
    {
        var r = R(new DateOnly(2026, 12, 31), familie: (new DateOnly(2026, 7, 1), null));
        Assert.Equal(8, r.Soll);   // 181/365 × 4 + 184/365 × 12 = 1.98 + 6.05
        Assert.True(r.MitFamilie);
    }

    [Fact]
    public void StempelUndAbsenzMachenSonntagUnfrei()
    {
        var alle = Sonntage(Jan1, new DateOnly(2026, 3, 31)).ToList();   // 13 Sonntage
        var r = R(new DateOnly(2026, 3, 31),
            stempel: alle.Take(10),
            absenzen: [(alle[10], alle[10])]);
        Assert.Equal(2, r.FreiBisher);
        Assert.Equal(11, r.GearbeitetBisher);
    }

    [Fact]
    public void Status_OkKnappRot()
    {
        var stichtag = new DateOnly(2026, 12, 13);   // danach noch 20.12. und 27.12.
        var alleBisher = Sonntage(Jan1, stichtag).ToList();

        Assert.Equal("ok", R(stichtag, stempel: alleBisher.Skip(4)).Status);

        var knapp = R(stichtag, stempel: alleBisher.Skip(2));      // 2 frei, fehlen 2, bleiben 2
        Assert.Equal("knapp", knapp.Status);
        Assert.Equal(2, knapp.Fehlend);
        Assert.Equal(2, knapp.Verbleibend);

        var rot = R(stichtag, stempel: alleBisher);                 // 0 frei, fehlen 4, bleiben 2
        Assert.Equal("rot", rot.Status);
    }

    [Fact]
    public void GeplanteAbsenzZaehltNichtAlsVerbleibend()
    {
        var r = R(new DateOnly(2026, 12, 13), absenzen: [(new DateOnly(2026, 12, 20), new DateOnly(2026, 12, 27))]);
        Assert.Equal(0, r.Verbleibend);
    }

    [Fact]
    public void ZaehltErstAbErstenStempelMonat()
    {
        var r = R(new DateOnly(2026, 12, 31), datenAb: new DateOnly(2026, 7, 1));
        Assert.Equal(new DateOnly(2026, 7, 1), r.ZaehlAb);
        Assert.Equal(2, r.Soll);
        Assert.Equal(Sonntage(new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31)).Count(), r.FreiBisher);
    }

    [Fact]
    public void Pdf_LaesstSichErzeugen()
    {
        var zeilen = new List<SonntagListePdfService.Zeile>
        {
            new("1220001", "Aurelio Ruben", "Artiles Santana", true, 12, 3, 38, 9, 11, "knapp"),
            new("1220002", "Bruno", "Beispiel", false, 4, 0, 41, 4, 2, "rot"),
            new("1220003", "Carla", "Test", false, 4, 9, 32, 0, 11, "ok"),
            new("1220004", "Dora", "Muster", false, 2, 1, 10, 1, 11, "offen"),
        };
        var bytes = new SonntagListePdfService().Generate(
            new("122 Teststadt", 2026, new DateOnly(2026, 1, 1), false, 4, 1, 1, zeilen));
        Assert.True(bytes.Length > 1000);
    }

    [Fact]
    public void OhneStempeldaten_KeineDaten()
    {
        var r = SonntagKontrolle.Berechne(new SonntagKontrolle.Eingabe(
            2026, new DateOnly(2026, 10, 12), null, [(Jan1, null)], null, new HashSet<DateOnly>(), []));
        Assert.Equal("keine_daten", r.Status);
    }
}

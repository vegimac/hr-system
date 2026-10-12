using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Freie Sonntage (Walter 12.10.2026): 4 pro Jahr, mit Familienbetreuung 12,
/// anteilig; frei = kein Stempel und keine Absenz am Sonntag; volle Krankheit/
/// Unfall u.ä. kürzt das Soll und zählt weder frei noch nicht frei.
/// </summary>
public class SonntagKontrolleTests
{
    static readonly DateOnly Jan1 = new(2026, 1, 1);
    static readonly DateOnly Dez31 = new(2026, 12, 31);

    static SonntagKontrolle.Absenz A(DateOnly von, DateOnly bis, string typ = "FERIEN", decimal prozent = 100)
        => new(von, bis, typ, prozent);

    static SonntagKontrolle.Ergebnis R(
        DateOnly stichtag,
        DateOnly? datenAb = null,
        (DateOnly, DateOnly?)? anstellung = null,
        (DateOnly, DateOnly?)? familie = null,
        IEnumerable<DateOnly>? stempel = null,
        IEnumerable<SonntagKontrolle.Absenz>? absenzen = null)
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
        Assert.Equal(4, R(Dez31).Soll);
        Assert.Equal(12, R(Dez31, familie: (new DateOnly(2020, 1, 1), null)).Soll);
    }

    [Fact]
    public void EintrittMitteJahr_Anteilig()
    {
        Assert.Equal(3, R(Dez31, anstellung: (new DateOnly(2026, 7, 1), null)).Soll);   // 184/365 × 4 = 2,02 → auf 3
    }

    [Fact]
    public void ImmerAufrunden_GanzesJahrBleibtGenau()
    {
        // 21 Tage krank bei 12: 344/365 × 12 = 11,31 → 12 (Walter: 9,1 gibt 10)
        var r = R(Dez31, familie: (new DateOnly(2020, 1, 1), null),
            absenzen: [A(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 21), "KRANK")]);
        Assert.Equal(11.31m, r.SollGenau);
        Assert.Equal(12, r.Soll);
        Assert.Equal(4, R(Dez31).Soll);   // kein Rundungsrest macht aus 4 eine 5
    }

    [Fact]
    public void FamilieHalbesJahr_MischSatz()
    {
        var r = R(Dez31, familie: (new DateOnly(2026, 7, 1), null));
        Assert.Equal(9, r.Soll);   // 181/365 × 4 + 184/365 × 12 = 8,03 → auf 9
        Assert.True(r.MitFamilie);
    }

    [Fact]
    public void StempelUndFerienMachenSonntagNichtFrei()
    {
        var alle = Sonntage(Jan1, new DateOnly(2026, 3, 31)).ToList();   // 13 Sonntage
        var r = R(new DateOnly(2026, 3, 31), stempel: alle.Take(10), absenzen: [A(alle[10], alle[10])]);
        Assert.Equal(2, r.FreiBisher);
        Assert.Equal(11, r.NichtFreiBisher);
    }

    [Fact]
    public void FastGanzesJahrKrank_KeinAnspruch_NichtRot()
    {
        // Familie, krank 01.01.–30.09., danach jeden Sonntag gearbeitet
        var r = R(new DateOnly(2026, 10, 12),
            familie: (new DateOnly(2020, 1, 1), null),
            stempel: Sonntage(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 12)),
            absenzen: [A(Jan1, new DateOnly(2026, 9, 30), "KRANK")]);
        Assert.Equal(4, r.Soll);                         // 92/365 × 12 = 3,02 → auf 4
        Assert.Equal(Sonntage(Jan1, new DateOnly(2026, 9, 30)).Count(), r.KrankBisher);
        Assert.Equal(0, r.FreiBisher);
        Assert.Equal(2, r.NichtFreiBisher);
        Assert.NotEqual("rot", r.Status);                // 11 Sonntage bleiben für 4
    }

    [Fact]
    public void Teilkrankheit_IstNormalerTag()
    {
        var r = R(Dez31, absenzen: [A(Jan1, Dez31, "KRANK", 50)]);
        Assert.Equal(4, r.Soll);
        Assert.Equal(0, r.KrankBisher);
        Assert.Equal(Sonntage(Jan1, Dez31).Count(), r.FreiBisher);
    }

    [Fact]
    public void UnfallMilitaerMutterschaft_OhneAnspruch_FerienNicht()
    {
        foreach (var typ in new[] { "UNFALL", "MILITAER", "ZIVILSCHUTZ", "MUTTERSCHAFT", "VATERSCHAFT" })
            Assert.Equal(0, R(Dez31, absenzen: [A(Jan1, Dez31, typ)]).Soll);
        Assert.Equal(4, R(Dez31, absenzen: [A(Jan1, Dez31, "FERIEN")]).Soll);
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

        Assert.Equal("rot", R(stichtag, stempel: alleBisher).Status);   // fehlen 4, bleiben 2
    }

    [Fact]
    public void GeplanteAbsenzZaehltNichtAlsVerbleibend()
    {
        var r = R(new DateOnly(2026, 12, 13), absenzen: [A(new DateOnly(2026, 12, 20), new DateOnly(2026, 12, 27))]);
        Assert.Equal(0, r.Verbleibend);
    }

    [Fact]
    public void ZaehltErstAbErstenStempelMonat()
    {
        var r = R(Dez31, datenAb: new DateOnly(2026, 7, 1));
        Assert.Equal(new DateOnly(2026, 7, 1), r.ZaehlAb);
        Assert.Equal(3, r.Soll);
        Assert.Equal(Sonntage(new DateOnly(2026, 7, 1), Dez31).Count(), r.FreiBisher);
    }

    [Fact]
    public void Pdf_LaesstSichErzeugen()
    {
        var zeilen = new List<SonntagListePdfService.Zeile>
        {
            new("1220001", "Aurelio Ruben", "Artiles Santana", true, 12, 3, 30, 8, 9, 11, "knapp"),
            new("1220002", "Bruno", "Beispiel", false, 4, 0, 41, 0, 4, 2, "rot", new DateOnly(2026, 10, 31)),
            new("1220003", "Carla", "Test", false, 1, 0, 2, 39, 1, 11, "offen", new DateOnly(2026, 12, 15), true),
        };
        var bytes = new SonntagListePdfService().Generate(
            new("122 Teststadt", 2026, Jan1, false, 3, 1, 1, zeilen));
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

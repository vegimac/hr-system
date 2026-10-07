using HrSystem.Services;
using Xunit;
using V = HrSystem.Services.ArbeitszeitVerstoesse;

namespace HrSystem.Tests;

/// <summary>Arbeitszeit-Verstösse aus Stempelzeiten (Walter 07.10.2026) — Fantasiedaten.</summary>
public class ArbeitszeitVerstoesseTests
{
    static readonly DateOnly Mo = new(2026, 3, 2);   // Montag

    static V.Stempel S(DateOnly tag, string ein, string aus, int ausPlusTage = 0)
    {
        var d = tag.ToDateTime(TimeOnly.MinValue);
        var e = d + TimeSpan.Parse(ein);
        var a = d.AddDays(ausPlusTage) + TimeSpan.Parse(aus);
        return new V.Stempel(tag, e, a);
    }

    static List<V.Verstoss> Pruefe(DateTime? geburt, params V.Stempel[] st) =>
        V.Pruefe(new V.Person(1, geburt, st, Array.Empty<DateOnly>()), Mo, Mo.AddDays(6));

    static readonly DateTime Erwachsen = new(1990, 1, 1);

    [Fact]
    public void Genau_5_5_Stunden_ohne_Pause_ist_erlaubt()
    {
        Assert.Empty(Pruefe(Erwachsen, S(Mo, "10:00", "15:30")));
    }

    [Fact]
    public void Ueber_5_5_Stunden_ohne_Pause_meldet_Pause()
    {
        var v = Assert.Single(Pruefe(Erwachsen, S(Mo, "10:00", "15:40")));
        Assert.Equal(V.Pause, v.Art);
        Assert.Equal(15, v.Grenze);
    }

    [Fact]
    public void Kurze_Unterbrechung_zaehlt_nicht_als_Pause()
    {
        var v = Assert.Single(Pruefe(Erwachsen, S(Mo, "10:00", "13:00"), S(Mo, "13:10", "16:00")));
        Assert.Equal(V.Pause, v.Art);
        Assert.Equal(0, v.Ist);
    }

    [Fact]
    public void Ueber_9_Stunden_braucht_60_Minuten_davon_30_am_Stueck()
    {
        var v = Pruefe(Erwachsen,
            S(Mo, "08:00", "11:00"), S(Mo, "11:20", "14:30"), S(Mo, "14:50", "17:50"), S(Mo, "18:10", "18:30"));
        var p = Assert.Single(v, x => x.Art == V.Pause);
        Assert.Equal(60, p.Ist);
        Assert.Contains("längste 20 Min.", p.Text);
    }

    [Fact]
    public void Pause_genuegt_aber_Block_zu_lang()
    {
        var v = Assert.Single(Pruefe(Erwachsen, S(Mo, "08:00", "14:00"), S(Mo, "14:20", "15:00")));
        Assert.Equal(V.Block, v.Art);
        Assert.Equal(360, v.Ist);
    }

    [Fact]
    public void Nachtarbeit_ueber_9_Stunden_und_Praesenz_ueber_10()
    {
        var v = Pruefe(Erwachsen, S(Mo, "15:00", "20:00"), S(Mo, "21:00", "01:30", 1));
        Assert.Contains(v, x => x.Art == V.NachtTag && x.Ist == 570);
        Assert.Contains(v, x => x.Art == V.Praesenz && x.Grenze == 600);
    }

    [Fact]
    public void Ohne_Nacht_gilt_Praesenz_14_Stunden()
    {
        var v = Pruefe(Erwachsen, S(Mo, "07:00", "11:00"), S(Mo, "17:00", "20:30"));
        Assert.DoesNotContain(v, x => x.Art == V.Praesenz);
        v = Pruefe(Erwachsen, S(Mo, "07:00", "11:00"), S(Mo, "18:00", "22:30"));
        Assert.Contains(v, x => x.Art == V.Praesenz && x.Grenze == 840);
    }

    [Fact]
    public void Stempel_ohne_Dauer_wird_ignoriert()
    {
        var folgetag = Mo.ToDateTime(TimeOnly.MinValue).AddDays(1).AddHours(7.25);
        var v = Pruefe(Erwachsen, S(Mo, "10:00", "14:00"), new V.Stempel(Mo, folgetag, folgetag));
        Assert.Empty(v);
    }

    [Fact]
    public void Woche_ueber_50_Stunden_und_Ruhetage()
    {
        var st = Enumerable.Range(0, 6).SelectMany(i =>
        {
            var t = Mo.AddDays(i);
            return new[] { S(t, "08:00", "13:00"), S(t, "14:00", "18:00") };
        }).ToArray();
        var v = Pruefe(Erwachsen, st);
        Assert.Contains(v, x => x.Art == V.Woche && x.Ist == 54 * 60);
        Assert.Contains(v, x => x.Art == V.Ruhetage && x.Ist == 1);
    }

    [Fact]
    public void Schicht_bis_Mitternacht_macht_Folgetag_nicht_zum_Arbeitstag()
    {
        var st = Enumerable.Range(0, 5)
            .Select(i => S(Mo.AddDays(i), "19:00", "00:00", 1)).ToArray();
        Assert.DoesNotContain(Pruefe(Erwachsen, st), x => x.Art == V.Ruhetage);
    }

    [Fact]
    public void Jugendliche_nicht_nach_22_Uhr()
    {
        var geburt = Mo.ToDateTime(TimeOnly.MinValue).AddYears(-17);
        var v = Assert.Single(Pruefe(geburt, S(Mo, "17:00", "22:30")));
        Assert.Equal(V.Jugend, v.Art);
        Assert.Contains("nach 22:00", v.Text);
    }

    static HrSystem.Models.ArbeitszeitRegelWert W(string regel, string key, decimal? wert, bool vorlage = true) =>
        new() { HauptsitzId = vorlage ? 1 : null, CompanyProfileId = vorlage ? null : 7, Regel = regel, Schluessel = key, Wert = wert };

    [Fact]
    public void Filiale_uebersteuert_Vorlage_und_Vorlage_das_Gesetz()
    {
        var e = ArbeitszeitEinstellungen.Aufloesen(
            new[] { W(V.Woche, "max_std", 48), W(V.Block, "max_std", 5) },
            new[] { W(V.Woche, "max_std", 45, vorlage: false) });
        Assert.Equal(45, e.Wert(V.Woche, "max_std"));
        Assert.Equal(5, e.Wert(V.Block, "max_std"));
        Assert.Equal(14, e.Wert(V.Praesenz, "max_std"));
        Assert.Equal("Über 45 h 00 pro Woche", V.Titel(V.Woche, e));
    }

    [Fact]
    public void Ausgeschaltete_Regel_meldet_nichts()
    {
        var e = ArbeitszeitEinstellungen.Aufloesen(new[] { W(V.Pause, ArbeitszeitRegelKatalog.Aktiv, 0) }, Array.Empty<HrSystem.Models.ArbeitszeitRegelWert>());
        var v = V.Pruefe(new V.Person(1, Erwachsen, new[] { S(Mo, "10:00", "16:00") }, Array.Empty<DateOnly>()), Mo, Mo.AddDays(6), e);
        var b = Assert.Single(v);
        Assert.Equal(V.Block, b.Art);
    }

    [Fact]
    public void Eigene_Pausengrenze_wirkt()
    {
        var e = ArbeitszeitEinstellungen.Aufloesen(new[] { W(V.Pause, "zaehlt_ab_min", 10), W(V.Pause, "pause1_min", 10) }, Array.Empty<HrSystem.Models.ArbeitszeitRegelWert>());
        var st = new[] { S(Mo, "10:00", "13:00"), S(Mo, "13:12", "16:00") };
        Assert.NotEmpty(V.Pruefe(new V.Person(1, Erwachsen, st, Array.Empty<DateOnly>()), Mo, Mo.AddDays(6))
            .Where(x => x.Art == V.Pause));
        Assert.Empty(V.Pruefe(new V.Person(1, Erwachsen, st, Array.Empty<DateOnly>()), Mo, Mo.AddDays(6), e)
            .Where(x => x.Art == V.Pause));
    }

    [Fact]
    public void Jugendliche_am_Sonntag_und_am_sonntagsgleichen_Feiertag()
    {
        var geburt = Mo.ToDateTime(TimeOnly.MinValue).AddYears(-16);
        var sonntag = Mo.AddDays(6);
        var mittwoch = Mo.AddDays(2);
        var st = new[] { S(mittwoch, "10:00", "14:00"), S(sonntag, "10:00", "14:00") };
        var feiertage = new Dictionary<DateOnly, string> { [mittwoch] = "Fantasietag" };
        var v = V.Pruefe(new V.Person(1, geburt, st, Array.Empty<DateOnly>()), Mo, sonntag, null, feiertage)
            .Where(x => x.Art == V.SonntagJugend).ToList();
        Assert.Equal(2, v.Count);
        Assert.Contains("Fantasietag", v[0].Text);
        Assert.Contains("am Sonntag", v[1].Text);

        var nurFeiertage = ArbeitszeitEinstellungen.Aufloesen(new[] { W(V.SonntagJugend, "sonntage", 0) }, Array.Empty<HrSystem.Models.ArbeitszeitRegelWert>());
        Assert.Single(V.Pruefe(new V.Person(1, geburt, st, Array.Empty<DateOnly>()), Mo, sonntag, nurFeiertage, feiertage),
            x => x.Art == V.SonntagJugend);
    }

    [Fact]
    public void Sonntag_beginnt_am_Samstag_um_23_Uhr()
    {
        var geburt = Mo.ToDateTime(TimeOnly.MinValue).AddYears(-17);
        var samstag = Mo.AddDays(5);
        var v = V.Pruefe(new V.Person(1, geburt, new[] { S(samstag, "18:00", "23:30") }, Array.Empty<DateOnly>()), Mo, Mo.AddDays(6));
        var s = Assert.Single(v, x => x.Art == V.SonntagJugend);
        Assert.Equal(30, s.Ist);
    }

    [Fact]
    public void Erwachsene_am_Sonntag_kein_Verstoss()
    {
        var st = new[] { S(Mo.AddDays(6), "10:00", "14:00") };
        Assert.Empty(V.Pruefe(new V.Person(1, Erwachsen, st, Array.Empty<DateOnly>()), Mo, Mo.AddDays(6)));
    }

    [Fact]
    public void Mehr_als_18_Naechte_in_6_Wochen()
    {
        var naechte = Enumerable.Range(0, 20).Select(i => Mo.AddDays(-14 + i)).ToList();
        var v = V.Pruefe(new V.Person(1, Erwachsen, Array.Empty<V.Stempel>(), naechte), Mo, Mo.AddDays(6));
        var n = Assert.Single(v);
        Assert.Equal(V.Naechte, n.Art);
        Assert.Equal(Mo.AddDays(4), n.Von);
        Assert.Equal(20, n.Ist);
    }
}

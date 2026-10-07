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

    // Muster wie im Bericht: Schichten über Mitternacht, «freie» Tage dazwischen, aber nie 35 h am Stück.
    static V.Stempel[] WocheUeberMitternacht(string mittwochBeginn) => new[]
    {
        S(Mo.AddDays(-1), "14:00", "22:00"),
        S(Mo, "17:00", "00:45", 1),
        S(Mo.AddDays(2), mittwochBeginn, "18:00"),
        S(Mo.AddDays(3), "10:00", "16:00"),
        S(Mo.AddDays(4), "18:00", "01:00", 1),
        S(Mo.AddDays(6), "10:00", "17:00"),
        S(Mo.AddDays(7), "10:00", "14:00"),
    };

    [Fact]
    public void Schicht_ueber_Mitternacht_freier_Tag_zaehlt_aber_kein_ganzer_Ruhetag()
    {
        var v = V.Pruefe(new V.Person(1, Erwachsen, WocheUeberMitternacht("10:00"), Array.Empty<DateOnly>()), Mo, Mo.AddDays(6));
        Assert.DoesNotContain(v, x => x.Art == V.Ruhetage);
        var g = Assert.Single(v, x => x.Art == V.GanzerRuhetag);
        Assert.Equal(33 * 60 + 15, g.Ist);
        Assert.Contains("Di 03.03. 00:45", g.Text);
    }

    [Fact]
    public void Ab_35_Stunden_am_Stueck_ist_der_ganze_Ruhetag_erfuellt()
    {
        var v = V.Pruefe(new V.Person(1, Erwachsen, WocheUeberMitternacht("12:00"), Array.Empty<DateOnly>()), Mo, Mo.AddDays(6));
        Assert.DoesNotContain(v, x => x.Art == V.GanzerRuhetag);
    }

    [Fact]
    public void Ausnahme_24_Stunden_nur_wenn_eingeschaltet()
    {
        var st = WocheUeberMitternacht("10:00");
        var e = ArbeitszeitEinstellungen.Aufloesen(new[] { W(V.GanzerRuhetag, "ausnahme_24", 1) }, Array.Empty<HrSystem.Models.ArbeitszeitRegelWert>());
        Assert.DoesNotContain(V.Pruefe(new V.Person(1, Erwachsen, st, Array.Empty<DateOnly>()), Mo, Mo.AddDays(6), e),
            x => x.Art == V.GanzerRuhetag);
    }

    [Fact]
    public void Ruhezeit_einmal_pro_Woche_8_Stunden_erlaubt()
    {
        var einmal = new[] { S(Mo, "14:00", "23:00"), S(Mo.AddDays(1), "07:00", "12:00") };
        Assert.DoesNotContain(Pruefe(Erwachsen, einmal), x => x.Art == V.Ruhezeit);

        var zweimal = einmal.Concat(new[] { S(Mo.AddDays(2), "14:00", "23:00"), S(Mo.AddDays(3), "07:00", "12:00") }).ToArray();
        var r = Assert.Single(Pruefe(Erwachsen, zweimal), x => x.Art == V.Ruhezeit);
        Assert.Equal(Mo.AddDays(2), r.Von);
        Assert.Contains("schon gebraucht", r.Text);
    }

    [Fact]
    public void Ruhezeit_unter_8_Stunden_immer_Verstoss()
    {
        var r = Assert.Single(Pruefe(Erwachsen, S(Mo, "15:00", "23:30"), S(Mo.AddDays(1), "06:30", "12:00")), x => x.Art == V.Ruhezeit);
        Assert.Equal(7 * 60, r.Ist);
        Assert.Equal(11 * 60, r.Grenze);
    }

    [Fact]
    public void Jugendliche_brauchen_12_Stunden_Ruhezeit()
    {
        var geburt = Mo.ToDateTime(TimeOnly.MinValue).AddYears(-17);
        var r = Assert.Single(Pruefe(geburt, S(Mo, "10:00", "19:00"), S(Mo.AddDays(1), "06:30", "12:00")), x => x.Art == V.Ruhezeit);
        Assert.Equal(12 * 60, r.Grenze);
    }

    static V.Stempel[] Tage(int anzahl, string bis = "15:00") =>
        Enumerable.Range(0, anzahl).Select(i => S(Mo.AddDays(i), "09:00", bis)).ToArray();

    [Fact]
    public void Sieben_Tage_mit_max_9_Stunden_und_83_Stunden_frei_erlaubt()
    {
        Assert.DoesNotContain(Pruefe(Erwachsen, Tage(7)), x => x.Art == V.SiebenTage);
    }

    [Fact]
    public void Sieben_Tage_mit_einem_langen_Tag_ist_Verstoss()
    {
        var st = Tage(7).Select((s, i) => i == 3 ? S(s.Tag, "08:00", "18:00") : s).ToArray();
        var v = Assert.Single(Pruefe(Erwachsen, st), x => x.Art == V.SiebenTage);
        Assert.Contains("Do 05.03.", v.Text);
    }

    [Fact]
    public void Sieben_Tage_ohne_83_Stunden_frei_danach_ist_Verstoss()
    {
        var st = Tage(7).Append(S(Mo.AddDays(9), "09:00", "15:00")).ToArray();
        var v = Assert.Single(Pruefe(Erwachsen, st), x => x.Art == V.SiebenTage);
        Assert.Contains("danach nur", v.Text);
    }

    [Fact]
    public void Acht_Tage_in_Folge_immer_Verstoss()
    {
        var v = Assert.Single(Pruefe(Erwachsen, Tage(8)), x => x.Art == V.SiebenTage);
        Assert.Equal(8, v.Ist);
    }

    [Fact]
    public void Zwei_halbe_Ruhetage_ergeben_einen()
    {
        var st = Enumerable.Range(0, 4).Select(i => S(Mo.AddDays(i), "09:00", "17:00"))
            .Append(S(Mo.AddDays(5), "15:00", "19:00"))
            .Append(S(Mo.AddDays(6), "08:00", "12:00")).ToArray();
        Assert.DoesNotContain(Pruefe(Erwachsen, st), x => x.Art == V.Ruhetage);

        var nurEinHalber = st.Take(4).Append(S(Mo.AddDays(4), "09:00", "17:00")).Append(st[4]).ToArray();
        var r = Assert.Single(Pruefe(Erwachsen, nurEinHalber), x => x.Art == V.Ruhetage);
        Assert.Contains("und 1 halber", r.Text);
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
        Assert.Contains("Kein Arztzeugnis", n.Text);
    }

    static List<DateOnly> ZwanzigNaechte => Enumerable.Range(0, 20).Select(i => Mo.AddDays(-14 + i)).ToList();

    [Fact]
    public void Naechte_mit_gueltigem_Arztzeugnis_und_Ausnahmeregelung_kein_Verstoss()
    {
        var nw = new V.NachtNachweis(true, Mo.AddDays(-100), Mo.AddDays(400), true);
        Assert.Empty(V.Pruefe(new V.Person(1, Erwachsen, Array.Empty<V.Stempel>(), ZwanzigNaechte, nw), Mo, Mo.AddDays(6)));
    }

    [Fact]
    public void Naechte_Arztzeugnis_ohne_Ende_gilt()
    {
        var nw = new V.NachtNachweis(true, null, null, true);
        Assert.Empty(V.Pruefe(new V.Person(1, Erwachsen, Array.Empty<V.Stempel>(), ZwanzigNaechte, nw), Mo, Mo.AddDays(6)));
    }

    [Fact]
    public void Naechte_nur_vor_dem_Arztzeugnis_gemeldet()
    {
        var nw = new V.NachtNachweis(true, Mo.AddDays(5), Mo.AddDays(400), true);
        var n = Assert.Single(V.Pruefe(new V.Person(1, Erwachsen, Array.Empty<V.Stempel>(), ZwanzigNaechte, nw), Mo, Mo.AddDays(6)));
        Assert.Equal(Mo.AddDays(4), n.Von);
        Assert.Equal(Mo.AddDays(4), n.Bis);
        Assert.Contains("erst ab", n.Text);
    }

    [Fact]
    public void Naechte_Ausnahmeregelung_fehlt()
    {
        var nw = new V.NachtNachweis(true, Mo.AddDays(-100), Mo.AddDays(400), false);
        var n = Assert.Single(V.Pruefe(new V.Person(1, Erwachsen, Array.Empty<V.Stempel>(), ZwanzigNaechte, nw), Mo, Mo.AddDays(6)));
        Assert.EndsWith("Ausnahmeregelung fehlt.", n.Text);
    }
}

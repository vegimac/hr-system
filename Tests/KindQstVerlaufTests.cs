using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// QST-Verlauf am Kind (Walter 11.10.2026, docs/kind-qst-verlauf-konzept.md):
/// Monatsregel «Änderung wirkt ab Folgemonat» + Entscheide, Verhältnisse, Altbestand.
/// </summary>
public class KindQstVerlaufTests
{
    static DateOnly D(int j, int m, int t) => new(j, m, t);

    static KindQstDaten Kind(DateOnly? geb,
        KindAusbildung[]? ausb = null,
        KindVerhaeltnis[]? verh = null,
        KindQstEntscheid[]? ents = null,
        bool? haushalt = true)
        => new(geb,
            ausb ?? Array.Empty<KindAusbildung>(),
            verh ?? Array.Empty<KindVerhaeltnis>(),
            ents ?? Array.Empty<KindQstEntscheid>(),
            haushalt);

    static bool Zaehlt(KindQstDaten d, int jahr, int monat) => KindQstVerlauf.ImMonat(d, jahr, monat).Zaehlt;

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    public void Geburt_wirkt_ab_Folgemonat(int tag)
    {
        var k = Kind(D(2026, 3, tag));
        Assert.False(Zaehlt(k, 2026, 3));
        Assert.True(Zaehlt(k, 2026, 4));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    public void Achtzehnter_Geburtstag_ohne_Ausbildung(int tag)
    {
        var k = Kind(D(2008, 3, tag));
        Assert.True(Zaehlt(k, 2026, 3));
        Assert.False(Zaehlt(k, 2026, 4));
        Assert.Equal("über 18, keine Erstausbildung", KindQstVerlauf.ImMonat(k, 2026, 4).Grund);
    }

    [Fact]
    public void Lehre_ab_16_geht_ueber_den_18_Geburtstag_weiter()
    {
        var k = Kind(D(2008, 3, 15), new[] { new KindAusbildung(D(2024, 8, 1), D(2027, 7, 31), true, Art: "LEHRE") });
        for (int m = 1; m <= 12; m++) Assert.True(Zaehlt(k, 2026, m), $"Monat {m}");
        Assert.True(Zaehlt(k, 2027, 7));
        Assert.False(Zaehlt(k, 2027, 8));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    public void Lehrbeginn_wirkt_ab_Folgemonat(int tag)
    {
        // 18 im Januar 2026, Lehre erst ab August → Februar–August nicht, ab September ja.
        var k = Kind(D(2008, 1, 10), new[] { new KindAusbildung(D(2026, 8, tag), null, true, Art: "LEHRE") });
        Assert.True(Zaehlt(k, 2026, 1));
        Assert.False(Zaehlt(k, 2026, 2));
        Assert.False(Zaehlt(k, 2026, 8));
        Assert.True(Zaehlt(k, 2026, 9));
        Assert.Equal("Lehre", KindQstVerlauf.ImMonat(k, 2026, 9).Grund);
    }

    [Fact]
    public void Lehrende_31_Juli()
    {
        var k = Kind(D(2006, 5, 5), new[] { new KindAusbildung(D(2022, 8, 1), D(2026, 7, 31), true, Art: "LEHRE") });
        Assert.True(Zaehlt(k, 2026, 7));
        Assert.False(Zaehlt(k, 2026, 8));
    }

    [Fact]
    public void Lehrabbruch_und_neue_Lehre()
    {
        var k = Kind(D(2006, 5, 5), new[]
        {
            new KindAusbildung(D(2025, 8, 1), D(2026, 3, 31), true, Art: "LEHRE", EndeGrund: "ABBRUCH"),
            new KindAusbildung(D(2026, 6, 1), null, true, Art: "LEHRE"),
        });
        Assert.True(Zaehlt(k, 2026, 3));
        Assert.False(Zaehlt(k, 2026, 4));
        Assert.False(Zaehlt(k, 2026, 6));
        Assert.True(Zaehlt(k, 2026, 7));
    }

    [Fact]
    public void Lehrstellenwechsel_nahtlos_ohne_Luecke()
    {
        var k = Kind(D(2008, 5, 5), new[]
        {
            new KindAusbildung(D(2024, 8, 1), D(2026, 7, 31), true, Art: "LEHRE"),
            new KindAusbildung(D(2026, 8, 1), D(2027, 7, 31), true, Art: "LEHRE"),
        });
        Assert.True(Zaehlt(k, 2026, 8));
        var w = KindQstVerlauf.Wirkung(k, D(2026, 10, 11));
        Assert.Single(w);
        Assert.Equal(D(2008, 6, 1), w[0].VonMonat);
        Assert.Equal(D(2027, 7, 1), w[0].BisMonat);
    }

    [Fact]
    public void Studium_nach_Lehre_ungeklaert_bis_Entscheid()
    {
        var studium = new KindAusbildung(D(2026, 9, 14), null, null, Art: "STUDIUM");
        var lehre = new KindAusbildung(D(2022, 8, 1), D(2026, 7, 31), true, Art: "LEHRE");
        var ohne = Kind(D(2006, 5, 5), new[] { lehre, studium });
        var m = KindQstVerlauf.ImMonat(ohne, 2026, 10);
        Assert.False(m.Zaehlt);
        Assert.Contains("Erstausbildung ungeklärt", m.Offen);

        var mit = Kind(D(2006, 5, 5), new[] { lehre, studium },
            ents: new[] { new KindQstEntscheid(KindQstVerlauf.Anerkannt, D(2026, 10, 1), null) });
        Assert.True(Zaehlt(mit, 2026, 10));
    }

    [Fact]
    public void Haushalt_ungeklaert_zaehlt_mit_gelbem_Punkt()
    {
        var k = Kind(D(2015, 1, 1), verh: new[] { new KindVerhaeltnis(D(2015, 1, 1), null, true) });
        var m = KindQstVerlauf.ImMonat(k, 2026, 3);
        Assert.True(m.Zaehlt);
        Assert.Null(m.ImHaushalt);
        Assert.Contains("Haushalt ungeklärt", m.Offen);
    }

    [Fact]
    public void Verhaeltnis_wirkt_ab_Folgemonat()
    {
        var k = Kind(D(2015, 1, 1), verh: new[]
        {
            new KindVerhaeltnis(D(2015, 1, 1), true, true),
            new KindVerhaeltnis(D(2026, 5, 1), false, true),
        });
        Assert.True(KindQstVerlauf.ImMonat(k, 2026, 5).ImHaushalt);
        Assert.False(KindQstVerlauf.ImMonat(k, 2026, 6).ImHaushalt);
    }

    [Fact]
    public void Unterhalt_nein_zaehlt_nicht()
    {
        var k = Kind(D(2005, 1, 1),
            new[] { new KindAusbildung(D(2021, 8, 1), null, true, Art: "LEHRE") },
            new[] { new KindVerhaeltnis(D(2005, 1, 1), true, true), new KindVerhaeltnis(D(2026, 2, 10), true, false) });
        Assert.True(Zaehlt(k, 2026, 2));
        Assert.False(Zaehlt(k, 2026, 3));
        Assert.Equal("Unterhalt nicht zur Hauptsache", KindQstVerlauf.ImMonat(k, 2026, 3).Grund);
    }

    [Fact]
    public void Ohne_Unterhalt_seit_Geburt_zaehlt_nie()
    {
        var k = Kind(D(2015, 1, 1), verh: new[] { new KindVerhaeltnis(D(2015, 1, 1), false, false) });
        Assert.False(Zaehlt(k, 2026, 3));
        Assert.Empty(KindQstVerlauf.ElmZeitraeume(k));
    }

    [Fact]
    public void Unterhalt_ab_Datum_zaehlt_ab_Folgemonat()
    {
        // Stiefkind: zuerst kein Unterhalt, ab 10.04.2026 zur Hauptsache (Verfügung)
        var k = Kind(D(2015, 1, 1), verh: new[]
        {
            new KindVerhaeltnis(D(2015, 1, 1), true, false),
            new KindVerhaeltnis(D(2026, 4, 10), true, true),
        });
        Assert.False(Zaehlt(k, 2026, 4));
        Assert.True(Zaehlt(k, 2026, 5));
        Assert.NotEmpty(KindQstVerlauf.ElmZeitraeume(k));
    }

    [Fact]
    public void Altbestand_Ausbildung_ohne_Beginn_heisst_unter_18_zuerst()
    {
        var k = Kind(D(2010, 1, 1), new[] { new KindAusbildung(null, null, true) });
        Assert.Equal("unter 18 / Ausbildung", KindQstVerlauf.ImMonat(k, 2026, 3).Grund);
    }

    [Fact]
    public void Entscheid_nicht_anerkannt_trotz_Ausbildung()
    {
        var k = Kind(D(2005, 1, 1),
            new[] { new KindAusbildung(D(2021, 8, 1), null, true, Art: "LEHRE") },
            ents: new[] { new KindQstEntscheid(KindQstVerlauf.NichtAnerkannt, D(2026, 4, 1), D(2026, 6, 30)) });
        Assert.True(Zaehlt(k, 2026, 3));
        Assert.False(Zaehlt(k, 2026, 4));
        Assert.False(Zaehlt(k, 2026, 6));
        Assert.True(Zaehlt(k, 2026, 7));
    }

    [Fact]
    public void Altbestand_ohne_Datum_zaehlt_weiter_und_ist_gelb()
    {
        var k = Kind(D(2006, 10, 14), new[] { new KindAusbildung(null, null, true, Geprueft: false) });
        var m = KindQstVerlauf.ImMonat(k, 2026, 10);
        Assert.True(m.Zaehlt);
        Assert.Contains("Ausbildung nicht geprüft", m.Offen);
    }

    [Fact]
    public void Ohne_Ausbildung_zaehlt_ueber_18_nicht()
    {
        // Nur eine Ausbildungszulage, aber keine QST-Ausbildung erfasst.
        var k = Kind(D(2006, 10, 14));
        Assert.False(Zaehlt(k, 2026, 10));
    }

    [Fact]
    public void Wirkung_offen_waehrend_Ausbildung_laeuft()
    {
        var k = Kind(D(2006, 10, 14), new[] { new KindAusbildung(D(2022, 8, 1), null, true, Art: "LEHRE") });
        var w = KindQstVerlauf.Wirkung(k, D(2026, 10, 11));
        Assert.Single(w);
        Assert.Null(w[0].BisMonat);
    }

    [Fact]
    public void Wirkung_minderjaehrig_endet_im_Geburtstagsmonat()
    {
        var k = Kind(D(2011, 2, 28));
        var w = KindQstVerlauf.Wirkung(k, D(2026, 10, 11));
        Assert.Single(w);
        Assert.Equal(D(2011, 3, 1), w[0].VonMonat);
        Assert.Equal(D(2029, 2, 1), w[0].BisMonat);
        Assert.Equal("unter 18", w[0].Grund);
    }

    // ── Altbestand / Swissdec: alter Zeitraum «QST abzugsberechtigt von/bis» ──

    [Fact]
    public void Zeitraum_Standard_ergibt_keinen_Entscheid()
    {
        var geb = D(2010, 4, 2);
        Assert.Empty(KindQstVerlauf.EntscheideAusZeitraum(geb, geb, geb.AddYears(18), false));
        Assert.Empty(KindQstVerlauf.EntscheideAusZeitraum(geb, null, null, false));
    }

    [Fact]
    public void Zeitraum_spaeter_Beginn_ergibt_nicht_anerkannt_davor()
    {
        var geb = D(2010, 4, 2);
        var e = KindQstVerlauf.EntscheideAusZeitraum(geb, D(2026, 5, 1), null, false);
        Assert.Equal(2, e.Count);
        Assert.Equal(new KindQstEntscheid(KindQstVerlauf.NichtAnerkannt, geb, D(2026, 4, 30)), e[0]);
        Assert.Equal(new KindQstEntscheid(KindQstVerlauf.Anerkannt, D(2026, 5, 1), null), e[1]);

        var k = Kind(geb, ents: e.ToArray());
        Assert.False(Zaehlt(k, 2026, 4));
        Assert.True(Zaehlt(k, 2026, 5));
    }

    [Fact]
    public void Zeitraum_frueheres_Ende_ergibt_nicht_anerkannt_danach()
    {
        var geb = D(2010, 4, 2);
        var e = KindQstVerlauf.EntscheideAusZeitraum(geb, geb, D(2026, 6, 30), false);
        var k = Kind(geb, ents: e.ToArray());
        Assert.True(Zaehlt(k, 2026, 6));
        Assert.False(Zaehlt(k, 2026, 7));
    }

    [Fact]
    public void Elm_Zeitraeume_Standard_wie_bisher()
    {
        var geb = D(2010, 4, 2);
        var z = KindQstVerlauf.ElmZeitraeume(Kind(geb));
        Assert.Single(z);
        Assert.Equal(geb, z[0].Von);
        Assert.Equal(geb.AddYears(18), z[0].Bis);
    }

    [Fact]
    public void Elm_Zeitraeume_aus_anerkanntem_Entscheid()
    {
        var geb = D(2010, 4, 2);
        var e = KindQstVerlauf.EntscheideAusZeitraum(geb, D(2026, 5, 1), D(2026, 12, 31), false);
        var z = KindQstVerlauf.ElmZeitraeume(Kind(geb, ents: e.ToArray()));
        Assert.Single(z);
        Assert.Equal(D(2026, 5, 1), z[0].Von);
        Assert.Equal(D(2026, 12, 31), z[0].Bis);
    }

    [Fact]
    public void Tarifvorschlag_nutzt_den_Verlauf()
    {
        var verlauf = Kind(D(2006, 10, 14), new[] { new KindAusbildung(D(2022, 8, 1), D(2026, 7, 31), true, Art: "LEHRE") });
        var k = new QstKindInput(null, null, D(2006, 10, 14), null, false, true, null, false, Verlauf: verlauf);
        Assert.True(QstTarifVorschlagLogic.IstQstBerechtigt(k, D(2026, 7, 15)));
        Assert.False(QstTarifVorschlagLogic.IstQstBerechtigt(k, D(2026, 8, 1)));
    }
}

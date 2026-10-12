using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Soll «Familienbetreuung» aus Kindern im Haushalt (Walter 12.10.2026):
/// ab Eintritt bzw. Geburt bis Tag vor dem 15. Geburtstag des jüngsten Kindes.
/// </summary>
public class FamilienbetreuungSollTests
{
    static readonly DateOnly Heute = new(2026, 10, 12);
    static FamilienbetreuungSoll.Kind K(int j, int m, int t) => new(new DateOnly(j, m, t));

    [Fact]
    public void OhneKinder_Nein()
    {
        var s = FamilienbetreuungSoll.Berechne([], new DateOnly(2020, 1, 1), Heute);
        Assert.False(s.Ja);
        Assert.Null(s.Von);
    }

    [Fact]
    public void KindVorEintritt_BeginntAmEintritt_EndetVorDem15Geburtstag()
    {
        var s = FamilienbetreuungSoll.Berechne([K(2015, 5, 15)], new DateOnly(2022, 3, 1), Heute);
        Assert.True(s.Ja);
        Assert.Equal(new DateOnly(2022, 3, 1), s.Von);
        Assert.Equal(new DateOnly(2030, 5, 14), s.Bis);
        Assert.Equal(new DateOnly(2030, 5, 15), s.JuengstesKind15Am);
    }

    [Fact]
    public void ErstesKindNachEintritt_BeginntAnDerGeburt()
    {
        var s = FamilienbetreuungSoll.Berechne([K(2024, 2, 10)], new DateOnly(2021, 6, 1), Heute);
        Assert.Equal(new DateOnly(2024, 2, 10), s.Von);
        Assert.Equal(new DateOnly(2039, 2, 9), s.Bis);
    }

    [Fact]
    public void MehrereKinder_EndeNachJuengstem()
    {
        var s = FamilienbetreuungSoll.Berechne([K(2012, 1, 1), K(2018, 7, 20)], new DateOnly(2019, 1, 1), Heute);
        Assert.Equal(new DateOnly(2019, 1, 1), s.Von);
        Assert.Equal(new DateOnly(2033, 7, 19), s.Bis);
    }

    [Fact]
    public void KindSchon15_Nein()
    {
        var s = FamilienbetreuungSoll.Berechne([K(2010, 3, 1)], new DateOnly(2015, 1, 1), Heute);
        Assert.False(s.Ja);
    }

    [Fact]
    public void Luecke_ZeigtDenHeuteGueltigenZeitraum()
    {
        // Älteres Kind 15 im Jahr 2020, jüngeres erst 2022 geboren → zweiter Zeitraum
        var s = FamilienbetreuungSoll.Berechne([K(2005, 1, 1), K(2022, 4, 1)], new DateOnly(2010, 1, 1), Heute);
        Assert.Equal(new DateOnly(2022, 4, 1), s.Von);
        Assert.Equal(new DateOnly(2037, 3, 31), s.Bis);
    }

    [Fact]
    public void KuenftigerEintritt_ZeigtKommendenZeitraum()
    {
        var s = FamilienbetreuungSoll.Berechne([K(2020, 1, 1)], new DateOnly(2026, 11, 1), Heute);
        Assert.True(s.Ja);
        Assert.Equal(new DateOnly(2026, 11, 1), s.Von);
    }

    [Fact]
    public void GeburtsdatumFehlt_AlleinOhneSoll_SonstMitHinweis()
    {
        var nurOhne = FamilienbetreuungSoll.Berechne([new(null)], new DateOnly(2020, 1, 1), Heute);
        Assert.Null(nurOhne.Ja);
        Assert.True(nurOhne.GeburtsdatumFehlt);

        var gemischt = FamilienbetreuungSoll.Berechne([new(null), K(2019, 1, 1)], new DateOnly(2020, 1, 1), Heute);
        Assert.True(gemischt.Ja);
        Assert.True(gemischt.GeburtsdatumFehlt);
    }

    [Fact]
    public void Pdf_LaesstSichErzeugen()
    {
        var zeilen = new List<FamilienbetreuungListePdfService.Zeile>
        {
            new("1220001", "Anna", "Muster", true, new(2026, 8, 1), new(2026, 8, 31), true, new(2022, 3, 1), new(2030, 5, 14),
                false, true, true, "jüngstes Kind wird 15 am 15.05.2030"),
            new("1220002", "Bruno", "Beispiel", false, null, null, false, null, null, false, false, false, null),
            new("1220003", "Carla", "Test", false, null, null, null, null, null, false, false, false, "Geburtsdatum Kind fehlt"),
        };
        var bytes = new FamilienbetreuungListePdfService().Generate(
            new("122 Teststadt", false, 3, 1, zeilen));
        Assert.True(bytes.Length > 1000);
    }

    [Fact]
    public void Abweichung_JeFeld()
    {
        var soll = FamilienbetreuungSoll.Berechne([K(2015, 5, 15)], new DateOnly(2022, 3, 1), Heute);

        Assert.Equal((false, false, false), FamilienbetreuungSoll.Abweichung(true, new(2022, 3, 1), new(2030, 5, 14), soll));
        Assert.Equal((false, true, true), FamilienbetreuungSoll.Abweichung(true, new(2026, 8, 1), new(2026, 8, 31), soll));
        Assert.Equal((true, true, true), FamilienbetreuungSoll.Abweichung(false, null, null, soll));

        var nein = FamilienbetreuungSoll.Berechne([], null, Heute);
        Assert.Equal((false, false, false), FamilienbetreuungSoll.Abweichung(false, null, null, nein));
        Assert.Equal((true, true, true), FamilienbetreuungSoll.Abweichung(true, new(2026, 8, 1), new(2026, 8, 31), nein));
    }
}

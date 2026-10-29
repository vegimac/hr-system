using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Referenzalter im Juli → Freibetrag ab August, 5 × 1'400.
/// Verzicht ab August, gesperrt nach der ersten Lohnabrechnung ab August,
/// Änderung wieder ab 1.1. des Folgejahres.
/// </summary>
public class AhvFreibetragEntscheidTests
{
    static readonly DateTime Geburt = new(1961, 7, 15);
    static readonly AhvBeitragspflichtStand Stand = AhvBeitragspflichtStand.Gesetzlich;

    [Fact]
    public void Juli_Referenzalter_Freibetrag_ab_August_fuenf_Monate()
    {
        var folge = AhvFreibetragEntscheid.Folgemonat("m", Geburt, Stand);
        Assert.Equal(new DateOnly(2026, 8, 1), folge);
        Assert.Equal(new DateOnly(2026, 8, 1), AhvFreibetragEntscheid.ErsterTag(2026, null, folge));
        Assert.Equal(5, AhvFreibetragEntscheid.MonateImJahr(folge, 2026));
        Assert.False(PayrollCalculations.HatReferenzalterErreicht("m", Geburt, 2026, 7, Stand));
        Assert.True(PayrollCalculations.HatReferenzalterErreicht("m", Geburt, 2026, 8, Stand));
    }

    [Fact]
    public void Lohnabrechnung_im_Juli_sperrt_den_Verzicht_noch_nicht()
    {
        bool gilt(int j, int m) => PayrollCalculations.HatReferenzalterErreicht("m", Geburt, j, m, Stand);
        var nurJuli = AhvFreibetragEntscheid.GesperrteJahre(new[] { (2026, 7) }, gilt);
        var abAugust = AhvFreibetragEntscheid.GesperrteJahre(new[] { (2026, 7), (2026, 8) }, gilt);
        Assert.Empty(nurJuli);
        Assert.Contains(2026, abAugust);
    }

    [Fact]
    public void Verzicht_ab_August_ist_erlaubt_solange_keine_Lohnabrechnung()
    {
        var folge = new DateOnly(2026, 8, 1);
        var neu = new[] { new AhvFreibetragEntscheid.Spanne(folge, null) };
        var fehler = AhvFreibetragEntscheid.Pruefe(
            Array.Empty<AhvFreibetragEntscheid.Spanne>(), neu,
            new HashSet<int>(), null, folge);
        Assert.Null(fehler);
    }

    [Fact]
    public void Verzicht_erst_im_September_ist_kein_ganzes_Jahr()
    {
        var folge = new DateOnly(2026, 8, 1);
        var neu = new[] { new AhvFreibetragEntscheid.Spanne(new DateOnly(2026, 9, 1), null) };
        var fehler = AhvFreibetragEntscheid.Pruefe(
            Array.Empty<AhvFreibetragEntscheid.Spanne>(), neu,
            new HashSet<int>(), null, folge);
        Assert.Contains("01.08.2026", fehler);
    }

    [Fact]
    public void Nach_der_Lohnabrechnung_nur_noch_ab_dem_Folgejahr()
    {
        var folge = new DateOnly(2026, 8, 1);
        var vorher = new[] { new AhvFreibetragEntscheid.Spanne(folge, null) };
        var gesperrt = new HashSet<int> { 2026 };

        var aus = AhvFreibetragEntscheid.Pruefe(vorher, Array.Empty<AhvFreibetragEntscheid.Spanne>(), gesperrt, null, folge);
        Assert.Contains("1.1.2027", aus);

        var endeJahr = new[] { new AhvFreibetragEntscheid.Spanne(folge, new DateOnly(2026, 12, 31)) };
        Assert.Null(AhvFreibetragEntscheid.Pruefe(vorher, endeJahr, gesperrt, null, folge));

        var ab2027 = new[] { new AhvFreibetragEntscheid.Spanne(new DateOnly(2027, 1, 1), null) };
        Assert.Null(AhvFreibetragEntscheid.Pruefe(
            Array.Empty<AhvFreibetragEntscheid.Spanne>(), ab2027, gesperrt, null, folge));
    }

    [Fact]
    public void Hinweis_nennt_fuenf_Monate_und_August()
    {
        var folge = new DateOnly(2026, 8, 1);
        var text = AhvFreibetragEntscheid.Hinweis(new DateOnly(2026, 7, 20), folge, folge, false);
        Assert.Contains("Juli 2026", text);
        Assert.Contains("August", text);
        Assert.Contains("5 × CHF 1'400 = CHF 7'000", text);
        Assert.Contains("1.1.2027", text);
    }
}

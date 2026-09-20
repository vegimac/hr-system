using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Ferien-/Feiertag-Tage-Gutschrift im Ein-/Austrittsmonat (Walter 20.09.2026, Swissdec
/// TF21 Meier Christian Austritt 15.03., TF26 Jenzer Eintritt 10.02.): anteilig nach der
/// Teilmonat-Methode der Filiale — derselbe Faktor wie beim anteiligen Monatslohn.
/// Vorher lief im Teilmonat ein voller Monat (+2.92) auf.
/// </summary>
public class FerienTageTeilmonatTests
{
    private const decimal MonatsGutschrift5Wochen = 35m / 12m;   // 2.9167

    private static decimal Faktor(string methode, DateOnly von, DateOnly bis)
        => PayrollCalculationEngine.TeilmonatAnteil(methode, 1m, von, bis,
            bis.DayNumber - von.DayNumber + 1, DateTime.DaysInMonth(bis.Year, bis.Month));

    [Fact]
    public void Tage30_Austritt15Maerz_HalbeGutschrift()
    {
        var f = Faktor("TAGE30", new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 15));
        Assert.Equal(0.5m, f);
        Assert.Equal(1.4583m, Math.Round(MonatsGutschrift5Wochen * f, 4));
    }

    [Fact]
    public void Tage30_Eintritt10Februar_21von30()
    {
        var f = Faktor("TAGE30", new DateOnly(2025, 2, 10), new DateOnly(2025, 2, 28));
        Assert.Equal(0.7m, f);
        Assert.Equal(2.0417m, Math.Round(MonatsGutschrift5Wochen * f, 4));
    }

    [Fact]
    public void Tagessatz365_Schaub_15Kalendertage()
    {
        var f = Faktor("TAGESSATZ365", new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 15));
        Assert.Equal(Math.Round(12m / 365m * 15, 6), Math.Round(f, 6));
        Assert.Equal(1.4384m, Math.Round(MonatsGutschrift5Wochen * f, 4));
    }

    [Fact]
    public void Kalendertage_15von31()
    {
        var f = Faktor("KALENDERTAGE", new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 15));
        Assert.Equal(Math.Round(15m / 31m, 6), Math.Round(f, 6));
    }

    [Fact]
    public void VollerMonat_FaktorEins()
    {
        Assert.Equal(1m, Faktor("TAGE30", new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31)));
        Assert.Equal(1m, Math.Round(Faktor("KALENDERTAGE", new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31)), 6));
    }
}

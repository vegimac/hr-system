using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Ferien-/Feiertag-Tage-Gutschrift im Ein-/Austrittsmonat (Walter 20.09.2026, L-GAV Art. 17):
/// Kalendermonat pauschal 30 Tage, jeder Anstellungstag = 1/30 des Monatsanspruchs — immer
/// 30-Tage-Methode, auch wenn die Filiale den Lohn TAGESSATZ365 rechnet (Schaub).
/// Swissdec TF21 Meier Christian Austritt 15.03. → 15/30, TF26 Jenzer Eintritt 10.02. → 21/30.
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
    public void Tage30_Austritt31Maerz_VollerMonat()
    {
        Assert.Equal(1m, Faktor("TAGE30", new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31)));
    }

    [Fact]
    public void Tage30_Eintritt27Februar_4von30()
    {
        // TF12 Casanova: 27.–28.2. = Tage 27–30 → 4/30 (30-Tage-Methode, Monatsende = 30)
        var f = Faktor("TAGE30", new DateOnly(2025, 2, 27), new DateOnly(2025, 2, 28));
        Assert.Equal(Math.Round(4m / 30m, 6), Math.Round(f, 6));
        Assert.Equal(0.3889m, Math.Round(MonatsGutschrift5Wochen * f, 4));
    }

    [Fact]
    public void Tage30_Austritt15Maerz_Feiertage_Halb()
    {
        var f = Faktor("TAGE30", new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 15));
        Assert.Equal(0.25m, Math.Round(0.5m * f, 4));
    }
}

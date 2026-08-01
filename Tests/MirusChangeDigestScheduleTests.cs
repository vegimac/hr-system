using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Mo–Fr-Fenster für den Mirus-Änderungsdigest (Walter 30.07.2026).
/// </summary>
public class MirusChangeDigestScheduleTests
{
    [Fact]
    public void PreviousWeekday0600_Montag_deckt_Freitag_ab()
    {
        // Montag 06:05 → seit Freitag 06:00
        var mon = new DateTime(2026, 7, 27, 6, 5, 0); // 27.07.2026 = Mo
        var since = MirusChangeDigestService.PreviousWeekday0600(mon);
        Assert.Equal(new DateTime(2026, 7, 24, 6, 0, 0), since); // Fr
        Assert.Equal(DayOfWeek.Friday, since.DayOfWeek);
    }

    [Fact]
    public void PreviousWeekday0600_Dienstag_deckt_Montag_ab()
    {
        var tue = new DateTime(2026, 7, 28, 6, 0, 0); // Di
        var since = MirusChangeDigestService.PreviousWeekday0600(tue);
        Assert.Equal(new DateTime(2026, 7, 27, 6, 0, 0), since); // Mo
    }

    [Fact]
    public void PreviousWeekday0600_Freitag_deckt_Donnerstag_ab()
    {
        var fri = new DateTime(2026, 7, 24, 6, 0, 0); // Fr
        var since = MirusChangeDigestService.PreviousWeekday0600(fri);
        Assert.Equal(new DateTime(2026, 7, 23, 6, 0, 0), since); // Do
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, true)]
    [InlineData(DayOfWeek.Tuesday, true)]
    [InlineData(DayOfWeek.Wednesday, true)]
    [InlineData(DayOfWeek.Thursday, true)]
    [InlineData(DayOfWeek.Friday, true)]
    [InlineData(DayOfWeek.Saturday, false)]
    [InlineData(DayOfWeek.Sunday, false)]
    public void IsSwissWeekday_nur_Mo_Fr(DayOfWeek dow, bool expected)
    {
        // 20.07.2026 = Montag; offset bis gewünschtem Wochentag
        var baseMon = new DateTime(2026, 7, 20, 12, 0, 0);
        var d = baseMon.AddDays((int)dow - (int)DayOfWeek.Monday);
        Assert.Equal(dow, d.DayOfWeek);
        Assert.Equal(expected, MirusChangeDigestBackgroundService.IsSwissWeekday(d));
    }
}

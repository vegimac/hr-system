using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class TimeEntryHoursTests
{
    [Fact]
    public void From_TimeIn_Out_Is_Wall_Clock()
    {
        var e = new EmployeeTimeEntry
        {
            TimeIn = new DateTime(2026, 7, 20, 21, 0, 0),
            TimeOut = new DateTime(2026, 7, 21, 1, 0, 0),
            TotalHours = 2m,      // falsch nur Tag
            DurationHours = 2m,
            NightHours = 2m,
        };
        Assert.Equal(4m, TimeEntryHours.AbsoluteHours(e));
    }

    [Fact]
    public void Alt_Data_Total_Equals_Tag_Uses_Tag_Plus_Nacht()
    {
        // Wie im Juli-Fall: TotalHours fälschlich = Tag, Nacht separat
        var h = TimeEntryHours.AbsoluteHours(totalHours: 155.98m, durationHours: 155.98m, nightHours: 18m);
        Assert.Equal(173.98m, h);
    }

    [Fact]
    public void Sync_Correct_Total_Includes_Night()
    {
        var h = TimeEntryHours.AbsoluteHours(totalHours: 173.98m, durationHours: 155.98m, nightHours: 18m);
        Assert.Equal(173.98m, h);
    }

    [Fact]
    public void Only_Total_Falls_Back()
    {
        Assert.Equal(8m, TimeEntryHours.AbsoluteHours(8m, null, null));
    }

    [Fact]
    public void SumAbsolute_Adds_Entries()
    {
        var list = new[]
        {
            new EmployeeTimeEntry
            {
                TimeIn = new DateTime(2026, 7, 1, 8, 0, 0),
                TimeOut = new DateTime(2026, 7, 1, 12, 0, 0),
                TotalHours = 4, DurationHours = 4, NightHours = 0,
            },
            new EmployeeTimeEntry
            {
                TimeIn = new DateTime(2026, 7, 2, 22, 0, 0),
                TimeOut = new DateTime(2026, 7, 3, 2, 0, 0),
                TotalHours = 2, DurationHours = 2, NightHours = 2,
            },
        };
        Assert.Equal(8m, TimeEntryHours.SumAbsolute(list));
    }
}

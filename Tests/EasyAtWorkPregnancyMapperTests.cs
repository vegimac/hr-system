using HrSystem.Services.EasyAtWork;
using Xunit;

namespace hr_system.Tests;

/// <summary>
/// easy@work «Schwanger» → Meldedatum (from) + ET (to).
/// Schwangerschaftsbeginn = ET − 280 (live, nicht hier gespeichert).
/// </summary>
public class EasyAtWorkPregnancyMapperTests
{
    private static EawProperty Prop(string key, string value, string? from, string? to) => new()
    {
        Key = key,
        Value = value,
        FromRaw = from,
        ToRaw = to,
    };

    [Fact]
    public void PickDates_Ja_WithFromAndTo_ReturnsMeldedatumAndEt()
    {
        var props = new[]
        {
            Prop("cf_pregnant", "Ja", "2025-12-31", "2026-09-27"),
        };
        var (melde, et) = EasyAtWorkPregnancyMapper.PickDates(props);
        Assert.Equal(new DateOnly(2025, 12, 31), melde);
        Assert.Equal(new DateOnly(2026, 9, 27), et);
        // Beginn Schwangerschaft = ET − 280 Tage (PregnancyFristCalculator)
        Assert.Equal(new DateOnly(2025, 12, 21), et!.Value.AddDays(-280));
    }

    [Fact]
    public void PickDates_Nein_ReturnsNull()
    {
        var props = new[]
        {
            Prop("cf_schwanger", "Nein", "2025-12-31", "2026-09-27"),
        };
        var (melde, et) = EasyAtWorkPregnancyMapper.PickDates(props);
        Assert.Null(melde);
        Assert.Null(et);
    }

    [Fact]
    public void IsSyncedFromEasy_RecognizesMarker()
    {
        Assert.True(EasyAtWorkPregnancyMapper.IsSyncedFromEasy(
            EasyAtWorkPregnancyMapper.SyncBemerkungMarker));
        Assert.True(EasyAtWorkPregnancyMapper.IsSyncedFromEasy(
            "Hinweis — aus easy@work synchronisiert"));
        Assert.False(EasyAtWorkPregnancyMapper.IsSyncedFromEasy(null));
        Assert.False(EasyAtWorkPregnancyMapper.IsSyncedFromEasy("manuell erfasst"));
    }

    [Fact]
    public void PickDates_MissingEt_ReturnsNull()
    {
        var props = new[]
        {
            Prop("cf_pregnant", "1", "2025-12-31", null),
        };
        var (melde, et) = EasyAtWorkPregnancyMapper.PickDates(props);
        Assert.Null(melde);
        Assert.Null(et);
    }

    [Fact]
    public void PickDates_IgnoresNightWorkKey()
    {
        var props = new[]
        {
            Prop("cf_night_work_doctors_note", "Ja", "2025-01-01", "2027-01-01"),
            Prop("cf_pregnant", "Ja", "2025-12-31", "2026-09-27"),
        };
        var (melde, et) = EasyAtWorkPregnancyMapper.PickDates(props);
        Assert.Equal(new DateOnly(2025, 12, 31), melde);
        Assert.Equal(new DateOnly(2026, 9, 27), et);
    }

    [Fact]
    public void PickDates_PrefersCurrentlyActiveVersion()
    {
        var props = new[]
        {
            // alte, abgelaufene Version
            Prop("cf_pregnant", "Ja", "2020-01-01", "2020-10-01"),
            // aktuelle
            Prop("cf_pregnant", "Ja", "2025-12-31", "2026-09-27"),
        };
        var (melde, et) = EasyAtWorkPregnancyMapper.PickDates(props);
        Assert.Equal(new DateOnly(2025, 12, 31), melde);
        Assert.Equal(new DateOnly(2026, 9, 27), et);
    }
}

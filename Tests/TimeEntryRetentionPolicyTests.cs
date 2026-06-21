using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Tests für die Stempelzeiten-Aufbewahrung (Walter-Vorgabe 21.06.2026):
/// Cutoff-Berechnung, „nur am 1. des Monats laufen" und der 5-Jahre-Riegel.
/// </summary>
public class TimeEntryRetentionPolicyTests
{
    // ── Cutoff = heute − X Jahre ──────────────────────────────────────────
    [Fact]
    public void ComputeCutoff_FiveYears()
    {
        var today = new DateOnly(2026, 6, 21);
        Assert.Equal(new DateOnly(2021, 6, 21), TimeEntryRetentionPolicy.ComputeCutoff(today, 5));
    }

    [Fact]
    public void ComputeCutoff_TenYears()
    {
        var today = new DateOnly(2026, 1, 1);
        Assert.Equal(new DateOnly(2016, 1, 1), TimeEntryRetentionPolicy.ComputeCutoff(today, 10));
    }

    [Fact]
    public void ComputeCutoff_LeapDay_RollsToValidDate()
    {
        // 29.02.2024 − 5 Jahre → 28.02.2019 (2019 kein Schaltjahr) — kein Crash.
        var today = new DateOnly(2024, 2, 29);
        Assert.Equal(new DateOnly(2019, 2, 28), TimeEntryRetentionPolicy.ComputeCutoff(today, 5));
    }

    // ── Nur am 1. des Monats ──────────────────────────────────────────────
    [Fact]
    public void IsRunDay_FirstOfMonth_True()
    {
        Assert.True(TimeEntryRetentionPolicy.IsRunDay(new DateOnly(2026, 7, 1)));
        Assert.True(TimeEntryRetentionPolicy.IsRunDay(new DateOnly(2026, 1, 1)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(15)]
    [InlineData(28)]
    [InlineData(30)]
    [InlineData(31)]
    public void IsRunDay_OtherDays_False(int day)
    {
        Assert.False(TimeEntryRetentionPolicy.IsRunDay(new DateOnly(2026, 7, day)));
    }

    // ── Effektive Jahre: gespeichert > Config-Default ─────────────────────
    [Fact]
    public void EffectiveYears_UsesStoredThenConfig()
    {
        Assert.Equal(7, TimeEntryRetentionPolicy.EffectiveYears(7, 5));
        Assert.Equal(5, TimeEntryRetentionPolicy.EffectiveYears(null, 5));
    }

    // ── 5-Jahre-Riegel ────────────────────────────────────────────────────
    [Fact]
    public void IsRetentionAllowed_FiveOrMore_True()
    {
        Assert.True(TimeEntryRetentionPolicy.IsRetentionAllowed(5, allowShort: false));
        Assert.True(TimeEntryRetentionPolicy.IsRetentionAllowed(10, allowShort: false));
    }

    [Fact]
    public void IsRetentionAllowed_BelowFive_BlockedUnlessExplicit()
    {
        Assert.False(TimeEntryRetentionPolicy.IsRetentionAllowed(4, allowShort: false));
        Assert.False(TimeEntryRetentionPolicy.IsRetentionAllowed(1, allowShort: false));
        // Nur mit ausdrücklicher Freigabe:
        Assert.True(TimeEntryRetentionPolicy.IsRetentionAllowed(4, allowShort: true));
    }
}

using System;
using System.Collections.Generic;
using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Parser für Original-Zeiten aus easy@work-Audit-Text
/// («Ein/Aus vom … bis … geändert»). Format aus UI/Changelog (Walter 18.07.2026).
/// </summary>
public class EasyAtWorkOriginalTimesParseTests
{
    private static readonly DateOnly Day = new(2026, 7, 2);

    [Fact]
    public void Aus_MitNumerischemDatum_LiefertOriginalOut()
    {
        var (_, oout) = EasyAtWorkTimepunchSyncService.ParseEditedTimesFromTexts(Day, new[]
        {
            "Aus vom 2.7.2026, 13:37 bis zum 2.7.2026, 15:15 geändert"
        });
        Assert.Equal(new DateTime(2026, 7, 2, 13, 37, 0), oout);
    }

    [Fact]
    public void Ein_MitMonatsname_LiefertOriginalIn()
    {
        var (oin, _) = EasyAtWorkTimepunchSyncService.ParseEditedTimesFromTexts(Day, new[]
        {
            "Ein vom 17 Januar 07:38 bis zum 17 Jan 07:15 geändert"
        });
        Assert.Equal(new DateTime(2026, 7, 2, 7, 38, 0), oin);
    }

    [Fact]
    public void Freitext_Und_Audit_Zusammen_ParstAus()
    {
        var (oin, oout) = EasyAtWorkTimepunchSyncService.ParseEditedTimesFromTexts(Day, new[]
        {
            "Falsch gestempelt",
            "🕐 Aus vom 2.7.2026, 13:37 bis zum 2.7.2026, 15:15 geändert"
        });
        Assert.Null(oin);
        Assert.Equal(new DateTime(2026, 7, 2, 13, 37, 0), oout);
    }

    [Fact]
    public void Comments_Und_Changelog_Zusammen()
    {
        var comments = new List<EawTimepunchComment>
        {
            new() { Body = "Falsch gestempelt" }
        };
        var changelog = new List<EawTimepunchChangelogEntry>
        {
            new() { Message = "Aus vom 2.7.2026, 13:37 bis zum 2.7.2026, 15:15 geändert" },
            new() { Body = "Ein vom 2.7.2026, 11:00 bis zum 2.7.2026, 11:30 geändert" },
        };
        var (oin, oout) = EasyAtWorkTimepunchSyncService.ParseEditedTimesFromComments(Day, comments, changelog);
        Assert.Equal(new DateTime(2026, 7, 2, 11, 0, 0), oin);
        Assert.Equal(new DateTime(2026, 7, 2, 13, 37, 0), oout);
    }

    [Fact]
    public void CreatedAt_nahe_am_Ein_ist_Originalzeit()
        => Assert.True(EasyAtWorkTimepunchSyncService.CreatedAtIstOriginal(
            new DateTime(2026, 7, 2, 9, 2, 0, DateTimeKind.Utc), new DateTime(2026, 7, 2, 9, 30, 0, DateTimeKind.Utc)));

    [Fact]
    public void Nachtraeglich_erfasster_Stempel_hat_keine_Originalzeit()
        // Stempel 02.07. 11:12 UTC, von Hand erfasst am 03.07. 12:02 UTC
        => Assert.False(EasyAtWorkTimepunchSyncService.CreatedAtIstOriginal(
            new DateTime(2026, 7, 3, 12, 2, 0, DateTimeKind.Utc), new DateTime(2026, 7, 2, 11, 12, 0, DateTimeKind.Utc)));

    [Fact]
    public void Unveraendert_gestempelt_ist_keine_Korrektur()
        => Assert.False(EasyAtWorkTimepunchSyncService.CreatedAtIstOriginal(
            new DateTime(2026, 7, 2, 9, 30, 20, DateTimeKind.Utc), new DateTime(2026, 7, 2, 9, 30, 0, DateTimeKind.Utc)));
}

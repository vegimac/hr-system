using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Familienbetreuung aus easy@work «cf_family_charges» (Walter 12.10.2026).
/// </summary>
public class EasyAtWorkFamilienbetreuungTests
{
    static EawProperty P(string value, string? from, string? to, string key = "cf_family_charges")
        => new() { Key = key, Value = value, FromRaw = from, ToRaw = to };

    static readonly DateOnly Heute = new(2026, 10, 12);

    [Fact]
    public void Beleg580107_UtcWirdZuZuercherKalendertag()
    {
        var r = EasyAtWorkEmployeeSyncService.FamilienbetreuungAusEasy(
            new[] { P("1", "2026-07-31 22:00:00", "2026-08-30 22:00:00") }, Heute);
        Assert.True(r.Ja);
        Assert.Equal(new DateOnly(2026, 8, 1), r.Von);
        Assert.Equal(new DateOnly(2026, 8, 31), r.Bis);
    }

    [Fact]
    public void KeinFeldOderNein_IstNein()
    {
        Assert.False(EasyAtWorkEmployeeSyncService.FamilienbetreuungAusEasy(Array.Empty<EawProperty>(), Heute).Ja);
        var r = EasyAtWorkEmployeeSyncService.FamilienbetreuungAusEasy(
            new[] { P("0", "2026-07-31 22:00:00", null) }, Heute);
        Assert.False(r.Ja);
        Assert.Null(r.Von);
    }

    [Fact]
    public void HeuteGueltigeVersionGewinnt_VorJuengererAbgelaufener()
    {
        var r = EasyAtWorkEmployeeSyncService.FamilienbetreuungAusEasy(new[]
        {
            P("1", "2025-12-31 23:00:00", null),
            P("1", "2026-07-31 22:00:00", "2026-08-30 22:00:00"),
        }, Heute);
        Assert.Equal(new DateOnly(2026, 1, 1), r.Von);
        Assert.Null(r.Bis);
    }

    [Fact]
    public void OhneGueltigeVersion_JuengsteJaVersion()
    {
        var r = EasyAtWorkEmployeeSyncService.FamilienbetreuungAusEasy(new[]
        {
            P("1", "2025-02-28 23:00:00", "2025-05-30 22:00:00"),
            P("1", "2026-07-31 22:00:00", "2026-08-30 22:00:00"),
            P("0", "2026-08-31 22:00:00", null),
        }, Heute);
        Assert.True(r.Ja);
        Assert.Equal(new DateOnly(2026, 8, 1), r.Von);
    }

    [Fact]
    public void AndereFelderZaehlenNicht()
    {
        var r = EasyAtWorkEmployeeSyncService.FamilienbetreuungAusEasy(
            new[] { P("1", "2026-07-31 22:00:00", null, "cf_night_work_doctors_note") }, Heute);
        Assert.False(r.Ja);
    }
}

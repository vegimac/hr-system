using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Tests für die reine Fenster-Berechnung + das Lock-Gating-Prädikat des
/// automatischen easy@work-Stempelzeiten-Sync (Walter-Vorgabe 19.06.2026):
///   from = max(Start der ältesten offenen Periode, today − 40), to = today;
///   keine offene Periode → kein Fenster (Sync überspringen).
///   IsEditable: nur Daten ab FirstAllowedDate dürfen geschrieben werden.
/// </summary>
public class EasyAtWorkAutoSyncWindowTests
{
    private static readonly DateOnly Today = new(2026, 6, 19);

    // ───────────────────────── ComputeSyncWindow ────────────────────────

    [Fact]
    public void KeineOffenePeriode_SyncWirdUebersprungen()
    {
        var w = EasyAtWorkTimepunchSyncService.ComputeSyncWindow(null, Today);
        Assert.Null(w);
    }

    [Fact]
    public void OffenePeriodeAelterAls40Tage_FromAuf40TageBegrenzt()
    {
        // Älteste offene Periode 1.1.2026 → viel weiter als 40 Tage zurück
        // → from wird auf today−40 begrenzt.
        var w = EasyAtWorkTimepunchSyncService.ComputeSyncWindow(new DateOnly(2026, 1, 1), Today);
        Assert.NotNull(w);
        Assert.Equal(Today.AddDays(-40), w!.Value.From);
        Assert.Equal(Today, w.Value.To);
    }

    [Fact]
    public void OffenePeriodeInnerhalb40Tage_FromIstPeriodenstart()
    {
        // Periode beginnt 1.6.2026 (< 40 Tage vor 19.6.) → from = Periodenstart.
        var start = new DateOnly(2026, 6, 1);
        var w = EasyAtWorkTimepunchSyncService.ComputeSyncWindow(start, Today);
        Assert.NotNull(w);
        Assert.Equal(start, w!.Value.From);
        Assert.Equal(Today, w.Value.To);
    }

    [Fact]
    public void ToIstImmerToday()
    {
        var w = EasyAtWorkTimepunchSyncService.ComputeSyncWindow(new DateOnly(2026, 5, 1), Today);
        Assert.NotNull(w);
        Assert.Equal(Today, w!.Value.To);
    }

    // ────────────────────────────── IsEditable ──────────────────────────

    [Fact]
    public void KeineSperre_AllesEditierbar()
    {
        Assert.True(EasyAtWorkTimepunchSyncService.IsEditable(new DateOnly(2020, 1, 1), null));
    }

    [Fact]
    public void DatumAbFirstAllowed_Editierbar()
    {
        // Genau am FirstAllowedDate ist erlaubt (>=).
        Assert.True(EasyAtWorkTimepunchSyncService.IsEditable(
            new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1)));
        Assert.True(EasyAtWorkTimepunchSyncService.IsEditable(
            new DateOnly(2026, 2, 15), new DateOnly(2026, 2, 1)));
    }

    [Fact]
    public void DatumVorFirstAllowed_Gesperrt()
    {
        // Januar-Stempel bei FirstAllowed 1.2. → gesperrt, darf nicht geschrieben werden.
        Assert.False(EasyAtWorkTimepunchSyncService.IsEditable(
            new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 1)));
    }
}

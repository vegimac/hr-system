namespace HrSystem.Services.Six;

/// <summary>
/// Täglicher SIX-Bankenstamm-Sync um 05:30 Europe/Zurich
/// (nach dem easy@work-Nachtlauf 05:00 — Walter 16.10.2026).
/// </summary>
public sealed class SixBankMasterBackgroundService : BackgroundService
{
    private readonly SixBankMasterSyncService _sync;
    private readonly IConfiguration _config;
    private readonly ILogger<SixBankMasterBackgroundService> _log;

    public SixBankMasterBackgroundService(
        SixBankMasterSyncService sync,
        IConfiguration config,
        ILogger<SixBankMasterBackgroundService> log)
    {
        _sync = sync;
        _config = config;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!(_config.GetValue<bool?>("SixBankMaster:Enabled") ?? true))
        {
            _log.LogInformation("SIX Bankenstamm-Sync deaktiviert (SixBankMaster:Enabled=false).");
            return;
        }

        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (TaskCanceledException) { return; }

        // Catch-up: nach Deploy/Neustart nach 05:30, wenn heute noch kein erfolgreicher Lauf.
        try
        {
            if (NeedsCatchUp())
            {
                _log.LogInformation("SIX Bankenstamm: Nachhol-Lauf nach Neustart.");
                await _sync.SyncAsync("automatisch (catch-up)", stoppingToken);
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _log.LogError(ex, "SIX Bankenstamm Catch-up fehlgeschlagen.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNext0530Zurich();
            _log.LogInformation("SIX Bankenstamm: nächster Lauf in {Hours:F1} h (05:30 Europe/Zurich).", delay.TotalHours);
            try { await Task.Delay(delay, stoppingToken); }
            catch (TaskCanceledException) { return; }
            if (stoppingToken.IsCancellationRequested) return;

            try
            {
                await _sync.SyncAsync("automatisch", stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.LogError(ex, "SIX Bankenstamm-Nachtlauf fehlgeschlagen — nächster Versuch morgen 05:30.");
            }
        }
    }

    bool NeedsCatchUp()
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
        if (nowLocal.TimeOfDay < new TimeSpan(5, 30, 0)) return false;
        var last = _sync.LetztesErgebnis;
        if (last is null || !last.Ok) return true;
        return last.FertigAm.Date < nowLocal.Date;
    }

    static readonly TimeZoneInfo SwissTz = FindSwissTz();
    static TimeZoneInfo FindSwissTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich"); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
            catch { return TimeZoneInfo.Utc; }
        }
    }

    static TimeSpan TimeUntilNext0530Zurich()
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
        var today = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, 5, 30, 0, DateTimeKind.Unspecified);
        var nextLocal = nowLocal < today ? today : today.AddDays(1);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocal, SwissTz);
        var delay = nextUtc - DateTime.UtcNow;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }
}

namespace HrSystem.Services;

/// <summary>
/// Täglich um 05:45 Europe/Zurich die italienischen Grenzgemeinden bei der ESTV holen.
/// Nach einem Neustart am selben Tag nach 05:45 einmal nachholen.
/// </summary>
public sealed class GrenzregionEstvBackgroundService : BackgroundService
{
    private readonly GrenzregionEstvImport _import;
    private readonly ILogger<GrenzregionEstvBackgroundService> _log;

    public GrenzregionEstvBackgroundService(
        GrenzregionEstvImport import,
        ILogger<GrenzregionEstvBackgroundService> log)
    {
        _import = import;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (TaskCanceledException) { return; }

        try
        {
            if (NeedsCatchUp())
            {
                _log.LogInformation("ESTV Grenzregion: Nachhol-Lauf nach Neustart.");
                await _import.HolenAsync("automatisch (catch-up)", stoppingToken);
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _log.LogError(ex, "ESTV Grenzregion Catch-up fehlgeschlagen. Die bestehende Liste bleibt.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNext0545Zurich();
            _log.LogInformation("ESTV Grenzregion: nächster Lauf in {Hours:F1} h (05:45 Europe/Zurich).", delay.TotalHours);
            try { await Task.Delay(delay, stoppingToken); }
            catch (TaskCanceledException) { return; }
            if (stoppingToken.IsCancellationRequested) return;

            try
            {
                await _import.HolenAsync("automatisch", stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.LogError(ex, "ESTV Grenzregion-Nachtlauf fehlgeschlagen. Nächster Versuch morgen 05:45.");
            }
        }
    }

    bool NeedsCatchUp()
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
        if (nowLocal.TimeOfDay < new TimeSpan(5, 45, 0)) return false;
        var last = _import.LetztesErgebnis;
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

    static TimeSpan TimeUntilNext0545Zurich()
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
        var today = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, 5, 45, 0, DateTimeKind.Unspecified);
        var nextLocal = nowLocal < today ? today : today.AddDays(1);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocal, SwissTz);
        var delay = nextUtc - DateTime.UtcNow;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }
}

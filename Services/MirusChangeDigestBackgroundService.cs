namespace HrSystem.Services;

/// <summary>
/// Täglicher Lauf 06:00 Europe/Zurich für den Mirus-Änderungsdigest
/// (Walter 23.07.2026). Catch-up nach Neustart, wenn der heutige Lauf
/// noch fehlt (analog easy@work Auto-Sync).
/// </summary>
public class MirusChangeDigestBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly ILogger<MirusChangeDigestBackgroundService> _log;

    private static readonly TimeZoneInfo SwissTz = FindSwissTz();

    public MirusChangeDigestBackgroundService(
        IServiceProvider services,
        IConfiguration config,
        ILogger<MirusChangeDigestBackgroundService> log)
    {
        _services = services;
        _config = config;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!(_config.GetValue<bool?>("MirusDigest:Enabled") ?? true))
        {
            _log.LogInformation("Mirus-Änderungsdigest ist deaktiviert (MirusDigest:Enabled=false).");
            return;
        }

        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (TaskCanceledException) { return; }

        // Catch-up: nach Deploy am Nachmittag den heutigen 06:00-Lauf nachholen.
        try
        {
            if (NeedsCatchUpToday())
            {
                _log.LogInformation("Mirus-Digest: Nachhol-Lauf nach Neustart (heute 06:00 noch nicht gelaufen).");
                await RunOnceAsync(stoppingToken);
                MarkRanToday();
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _log.LogError(ex, "Mirus-Digest: Catch-up fehlgeschlagen.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNext0600Zurich();
            _log.LogInformation("Mirus-Digest: nächster Lauf in {Hours:F1} h (06:00 Europe/Zurich).", delay.TotalHours);
            try { await Task.Delay(delay, stoppingToken); }
            catch (TaskCanceledException) { return; }
            if (stoppingToken.IsCancellationRequested) return;

            try
            {
                await RunOnceAsync(stoppingToken);
                MarkRanToday();
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Mirus-Digest-Lauf fehlgeschlagen — nächster Versuch morgen 06:00.");
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<MirusChangeDigestService>();
        var result = await svc.RunAsync(ct);
        _log.LogInformation("Mirus-Digest Ergebnis: {Msg}", result.Message);
    }

    // In-Memory-Marker reicht (Catch-up nur innerhalb eines Prozesslebens).
    private static string? _lastRunLocalDate;

    private static bool NeedsCatchUpToday()
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
        if (nowLocal.Hour < 6) return false;
        var today = nowLocal.ToString("yyyy-MM-dd");
        return _lastRunLocalDate != today;
    }

    private static void MarkRanToday()
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
        _lastRunLocalDate = nowLocal.ToString("yyyy-MM-dd");
    }

    private static TimeSpan TimeUntilNext0600Zurich()
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
        var todayAt6 = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, 6, 0, 0, DateTimeKind.Unspecified);
        var nextLocal = nowLocal < todayAt6 ? todayAt6 : todayAt6.AddDays(1);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocal, SwissTz);
        var delay = nextUtc - DateTime.UtcNow;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static TimeZoneInfo FindSwissTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich"); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
            catch { return TimeZoneInfo.Utc; }
        }
    }
}

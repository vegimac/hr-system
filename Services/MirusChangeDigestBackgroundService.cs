using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Täglicher Lauf 06:00 Europe/Zurich für den Mirus-Änderungsdigest
/// (Walter 23.07.2026).
///
/// Catch-up nur am Vormittag (06:00–12:00), und «heute schon gelaufen»
/// wird in app_setting persistiert — sonst sendet jeder Deploy/Neustart
/// am Abend erneut die gleichen Mails (Walter-Bug 27.07.2026).
/// </summary>
public class MirusChangeDigestBackgroundService : BackgroundService
{
    public const string LastRunSettingKey = "MirusDigest.LastRunLocalDate";

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

        // Catch-up: nur wenn der heutige 06:00-Lauf fehlt UND wir noch am
        // Vormittag sind. Abends nach Deploy = warten bis morgen 06:00.
        try
        {
            if (await NeedsCatchUpTodayAsync(stoppingToken))
            {
                _log.LogInformation("Mirus-Digest: Nachhol-Lauf (heute 06:00 noch nicht gelaufen, vor 12:00).");
                await RunOnceAsync(stoppingToken);
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
                // Doppel-Schutz: wenn z.B. Catch-up + Timer knapp hintereinander
                // (oder manuelles run-now), nicht nochmals senden.
                if (await HasRanTodayAsync(stoppingToken))
                {
                    _log.LogInformation("Mirus-Digest: heutiger Lauf bereits erledigt — übersprungen.");
                    continue;
                }
                await RunOnceAsync(stoppingToken);
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
        await MarkRanTodayAsync(ct);
        _log.LogInformation("Mirus-Digest Ergebnis: {Msg}", result.Message);
    }

    /// <summary>
    /// Catch-up nur 06:00–12:00 Europe/Zurich und nur wenn heute noch kein
    /// Lauf in app_setting steht.
    /// </summary>
    private async Task<bool> NeedsCatchUpTodayAsync(CancellationToken ct)
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
        if (nowLocal.Hour < 6 || nowLocal.Hour >= 12) return false;
        return !await HasRanTodayAsync(ct);
    }

    private async Task<bool> HasRanTodayAsync(CancellationToken ct)
    {
        var today = TodayLocalDateString();
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var last = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == LastRunSettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return last == today;
    }

    /// <summary>
    /// Persistiert «heute gelaufen» (auch bei 0 Mails — sonst Catch-up-Spam).
    /// Öffentlich nutzbar vom manuellen run-now-Endpoint.
    /// </summary>
    public static async Task MarkRanTodayAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var today = TodayLocalDateString();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == LastRunSettingKey, ct);
        if (row == null)
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = LastRunSettingKey,
                Value = today,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            row.Value = today;
            row.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkRanTodayAsync(CancellationToken ct)
        => await MarkRanTodayAsync(_services, ct);

    private static string TodayLocalDateString()
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
        return nowLocal.ToString("yyyy-MM-dd");
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

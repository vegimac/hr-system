using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Mo–Fr 06:00 Europe/Zurich für den Mirus-Änderungsdigest
/// (Walter 23.07.2026, Mo–Fr Walter 30.07.2026).
///
/// Wochenende: kein Versand (Sa/So). Montag deckt Fr 06:00–Mo 06:00 ab
/// (Fenster-Logik in MirusChangeDigestService).
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

        // Catch-up: nur Mo–Fr, nur wenn der heutige 06:00-Lauf fehlt UND wir
        // noch am Vormittag sind. Abends nach Deploy = warten bis nächster Werktag 06:00.
        try
        {
            if (await NeedsCatchUpTodayAsync(stoppingToken))
            {
                _log.LogInformation("Mirus-Digest: Nachhol-Lauf (heute 06:00 noch nicht gelaufen, vor 12:00, Werktag).");
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
            var delay = TimeUntilNextWeekday0600Zurich();
            _log.LogInformation("Mirus-Digest: nächster Lauf in {Hours:F1} h (nächster Werktag 06:00 Europe/Zurich).", delay.TotalHours);
            try { await Task.Delay(delay, stoppingToken); }
            catch (TaskCanceledException) { return; }
            if (stoppingToken.IsCancellationRequested) return;

            try
            {
                var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
                if (!IsSwissWeekday(nowLocal))
                {
                    _log.LogInformation("Mirus-Digest: Wochenende — kein Versand (Mo–Fr 06:00).");
                    continue;
                }
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
                _log.LogError(ex, "Mirus-Digest-Lauf fehlgeschlagen — nächster Versuch am nächsten Werktag 06:00.");
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
    /// Catch-up nur Mo–Fr, 06:00–12:00 Europe/Zurich und nur wenn heute noch kein
    /// Lauf in app_setting steht.
    /// </summary>
    private async Task<bool> NeedsCatchUpTodayAsync(CancellationToken ct)
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
        if (!IsSwissWeekday(nowLocal)) return false;
        if (nowLocal.Hour < 6 || nowLocal.Hour >= 12) return false;
        return !await HasRanTodayAsync(ct);
    }

    /// <summary>Mo–Fr = Werktag für den Digest (Walter 30.07.2026).</summary>
    public static bool IsSwissWeekday(DateTime local) =>
        local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

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

    /// <summary>Nächster Mo–Fr 06:00 Europe/Zurich (überspringt Sa/So).</summary>
    public static TimeSpan TimeUntilNextWeekday0600Zurich()
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
        var nextLocal = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, 6, 0, 0, DateTimeKind.Unspecified);
        if (nowLocal >= nextLocal)
            nextLocal = nextLocal.AddDays(1);
        while (!IsSwissWeekday(nextLocal))
            nextLocal = nextLocal.AddDays(1);
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

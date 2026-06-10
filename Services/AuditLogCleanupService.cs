using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Walter-Vorgabe 27.05.2026: Audit-Log-Eintraege aelter als
/// AUDIT_RETENTION_DAYS (Default 180 Tage = ca. 6 Monate) werden
/// automatisch geloescht. Laeuft als Hintergrunddienst:
///   - Beim App-Start einmal (30 Sek. Warmup-Verzoegerung)
///   - Danach alle 24 Stunden
///
/// Der DELETE laeuft via ExecuteSqlRawAsync und umgeht damit den
/// AuditSaveChangesInterceptor — sonst wuerde das Cleanup selbst
/// 1000-fach im Audit-Log landen.
/// </summary>
public class AuditLogCleanupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AuditLogCleanupService> _log;

    // Retention: 180 Tage. Bei Bedarf in appsettings.json überschreibbar
    // („Audit:RetentionDays").
    private readonly int _retentionDays;

    public AuditLogCleanupService(
        IServiceProvider services,
        IConfiguration config,
        ILogger<AuditLogCleanupService> log)
    {
        _services = services;
        _log = log;
        _retentionDays = config.GetValue<int?>("Audit:RetentionDays") ?? 180;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Warmup: dem Webserver Zeit geben zu starten, dann erste Runde
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Cleanup-Fehler darf den Webserver nicht stoppen — nur loggen.
                _log.LogWarning(ex, "Audit-Log-Cleanup fehlgeschlagen — wird in 24h erneut versucht.");
            }
            try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Hinweis: ExecuteSqlRawAsync geht NICHT durch den
        // AuditSaveChangesInterceptor — das ist genau gewollt, damit
        // das Cleanup nicht sich selbst loggt.
        var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
        var sql = "DELETE FROM audit_log WHERE created_at < {0}";
        var rows = await db.Database.ExecuteSqlRawAsync(sql, new object[] { cutoff }, ct);
        if (rows > 0)
        {
            _log.LogInformation(
                "Audit-Log-Cleanup: {Rows} Eintraege aelter als {Days} Tage geloescht (vor {Cutoff:yyyy-MM-dd}).",
                rows, _retentionDays, cutoff);
        }
    }
}

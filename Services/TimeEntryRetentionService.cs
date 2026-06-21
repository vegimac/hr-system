using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Monatliche Aufbewahrungs-Routine für Stempelzeiten (Walter-Vorgabe
/// 21.06.2026) — BEWUSST getrennt vom täglichen easy@work-Auto-Sync.
///
///   • Läuft jeden 1. des Monats um 04:30 Europe/Zurich.
///   • Löscht employee_time_entry mit entry_date &lt; current_date − X Jahre.
///   • Danach VACUUM ANALYZE employee_time_entry.
///   • Loggt Anzahl gelöschter Zeilen, Cutoff-Datum und Fehler.
///
/// Konfiguration:
///   • Jahre: app_setting["TimeEntries.RetentionYears"] (UI-editierbar) →
///     Fallback Config "TimeEntries:RetentionYears" → Default 5.
///   • An/Aus: Config "TimeEntries:RetentionEnabled" (Default true).
///   • Kurz-Aufbewahrung (&lt; 5 Jahre) nur mit
///     Config "TimeEntries:AllowShortRetention" (Default false).
/// </summary>
public class TimeEntryRetentionService : BackgroundService
{
    public const string SettingKey = "TimeEntries.RetentionYears";

    private readonly IServiceProvider _services;
    private readonly ILogger<TimeEntryRetentionService> _log;

    private readonly bool _enabled;
    private readonly int  _configDefaultYears;
    private readonly bool _allowShort;

    public TimeEntryRetentionService(
        IServiceProvider services,
        IConfiguration config,
        ILogger<TimeEntryRetentionService> log)
    {
        _services = services;
        _log = log;
        _enabled            = config.GetValue<bool?>("TimeEntries:RetentionEnabled") ?? true;
        _configDefaultYears = config.GetValue<int?>("TimeEntries:RetentionYears") ?? 5;
        _allowShort         = config.GetValue<bool?>("TimeEntries:AllowShortRetention") ?? false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _log.LogInformation("Stempelzeiten-Retention deaktiviert (TimeEntries:RetentionEnabled=false).");
            return;
        }

        // Warmup
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun();
            _log.LogInformation("Stempelzeiten-Retention: nächster Lauf in {Days:F1} Tagen (1. des Monats, 04:30 Europe/Zurich).",
                delay.TotalDays);
            try { await Task.Delay(delay, stoppingToken); }
            catch (TaskCanceledException) { return; }

            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Stempelzeiten-Retention-Lauf fehlgeschlagen — nächster Versuch im Folgemonat.");
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(SwissNow().Date);

        // Defensiv: nur am 1. des Monats wirklich löschen (verhindert ein
        // versehentliches Löschen bei Scheduler-Drift / Catch-up).
        if (!TimeEntryRetentionPolicy.IsRunDay(today))
        {
            _log.LogInformation("Stempelzeiten-Retention: heute ({Today}) ist nicht der 1. — übersprungen.", today);
            return;
        }

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Editierbaren Wert aus dem Key/Value-Store lesen (Fallback Config).
        int? stored = null;
        var raw = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == SettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        if (int.TryParse(raw, out var parsed)) stored = parsed;

        var years = TimeEntryRetentionPolicy.EffectiveYears(stored, _configDefaultYears);

        if (!TimeEntryRetentionPolicy.IsRetentionAllowed(years, _allowShort))
        {
            _log.LogWarning(
                "Stempelzeiten-Retention: Aufbewahrung {Years} Jahre liegt unter {Min} — Löschen blockiert " +
                "(TimeEntries:AllowShortRetention=false). Kein Eintrag gelöscht.",
                years, TimeEntryRetentionPolicy.MinRetentionYears);
            return;
        }

        var cutoff = TimeEntryRetentionPolicy.ComputeCutoff(today, years);

        // DELETE — Stempelzeiten innerhalb der Aufbewahrung bleiben unangetastet
        // (entry_date >= cutoff).
        var rows = await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM employee_time_entry WHERE entry_date < {0}",
            new object[] { cutoff }, ct);

        _log.LogInformation(
            "Stempelzeiten-Retention: {Rows} Zeile(n) älter als {Years} Jahre gelöscht (entry_date < {Cutoff:yyyy-MM-dd}).",
            rows, years, cutoff);

        // VACUUM ANALYZE — Platz freigeben + Statistiken aktualisieren.
        // Läuft NICHT in einer Transaktion (Postgres verbietet das) — daher
        // eigener Try/Catch, damit ein VACUUM-Fehler den Lauf nicht versenkt.
        try
        {
            await db.Database.ExecuteSqlRawAsync("VACUUM ANALYZE employee_time_entry", ct);
            _log.LogInformation("Stempelzeiten-Retention: VACUUM ANALYZE employee_time_entry abgeschlossen.");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Stempelzeiten-Retention: VACUUM ANALYZE fehlgeschlagen (DELETE war erfolgreich).");
        }
    }

    // ── Zeitplanung: nächster 1. des Monats, 04:30 Europe/Zurich ──────────
    private static TimeSpan TimeUntilNextRun()
    {
        var nowLocal = SwissNow();
        var firstThisMonth = new DateTime(nowLocal.Year, nowLocal.Month, 1, 4, 30, 0, DateTimeKind.Unspecified);
        var nextLocal = nowLocal < firstThisMonth
            ? firstThisMonth
            : firstThisMonth.AddMonths(1);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocal, SwissTz);
        var delay = nextUtc - DateTime.UtcNow;
        return delay < TimeSpan.Zero ? TimeSpan.FromMinutes(1) : delay;
    }

    private static readonly TimeZoneInfo SwissTz = FindSwissTz();
    private static DateTime SwissNow() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
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

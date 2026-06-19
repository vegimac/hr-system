using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services.EasyAtWork;

// ════════════════════════════════════════════════════════════════════════
// Automatischer easy@work-Stempelzeiten-Sync (Walter-Vorgabe 19.06.2026)
//
// - Läuft täglich um 05:00 Europe/Zurich (BackgroundService unten).
// - Synct ALLE easy@work-gemappten Filialen SEQUENZIELL (keine parallelen
//   API-Spikes).
// - Pro Filiale Fenster:
//     from = max(Start der ältesten NICHT definitiv abgeschlossenen Periode,
//                today − 40 Tage),  to = today.
//   Keine offene Periode → Filiale überspringen.
// - Quelle: timepunch_updates (Cursor = last_seen_updated_at), sonst voller
//   timepunches-Abzug. Lokaler [from,to]-Filter gilt für beide.
// - Schreibt/ändert/löscht nur Stempel, deren Periode NICHT gesperrt ist
//   (LohnEditLockService).
// - Fehlende MA (Preflight) blockieren den Sync der Filiale → last_error.
// - Erfolg: last_sync_at / last_seen_updated_at / last_row_count / last_error=null.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Orchestriert den Auto-Sync über alle Filialen. Pro Filiale ein EIGENER
/// DI-Scope (frischer DbContext) → ein Fehler in einer Filiale verschmutzt den
/// Lauf der nächsten nicht.
/// </summary>
public class EasyAtWorkAutoSyncRunner
{
    private const string Resource = "TIMEPUNCH";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EasyAtWorkClient _client;          // Singleton
    private readonly ILogger<EasyAtWorkAutoSyncRunner> _log;

    public EasyAtWorkAutoSyncRunner(
        IServiceScopeFactory scopeFactory,
        EasyAtWorkClient client,
        ILogger<EasyAtWorkAutoSyncRunner> log)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _log = log;
    }

    public async Task RunAllBranchesAsync(CancellationToken ct)
    {
        if (!_client.IsConfigured)
        {
            _log.LogInformation("easy@work nicht konfiguriert — Auto-Sync übersprungen.");
            return;
        }

        var today = DateOnly.FromDateTime(SwissNow());

        // Mappings einmal laden (eigener Scope).
        List<EasyAtWorkBranchMapping> mappings;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            mappings = await db.EasyAtWorkBranchMappings.AsNoTracking().ToListAsync(ct);
        }

        _log.LogInformation("easy@work Auto-Sync gestartet für {N} Filiale(n), Stichtag {Today}.", mappings.Count, today);

        foreach (var mapping in mappings)   // SEQUENZIELL
        {
            ct.ThrowIfCancellationRequested();

            // Pro-Filiale-Schalter (Filial-Einstellungen-Tab). Aus = überspringen.
            if (!mapping.AutoSyncEnabled)
            {
                _log.LogInformation("easy@work Auto-Sync: Filiale {Cp} ist deaktiviert — übersprungen.", mapping.CompanyProfileId);
                continue;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tpSync = scope.ServiceProvider.GetRequiredService<EasyAtWorkTimepunchSyncService>();
                await RunBranchAsync(db, tpSync, mapping, today, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "easy@work Auto-Sync Filiale {Cp} fehlgeschlagen.", mapping.CompanyProfileId);
                await SaveErrorAsync(mapping.CompanyProfileId, ex.Message, ct);
            }
        }

        _log.LogInformation("easy@work Auto-Sync beendet.");
    }

    private async Task RunBranchAsync(
        AppDbContext db, EasyAtWorkTimepunchSyncService tpSync,
        EasyAtWorkBranchMapping mapping, DateOnly today, CancellationToken ct)
    {
        // Älteste NICHT definitiv abgeschlossene Periode dieser Filiale.
        var oldestStart = await db.PayrollPerioden
            .Where(p => p.CompanyProfileId == mapping.CompanyProfileId && p.Status != "abgeschlossen")
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .Select(p => (DateOnly?)p.PeriodFrom)
            .FirstOrDefaultAsync(ct);

        var window = EasyAtWorkTimepunchSyncService.ComputeSyncWindow(oldestStart, today);
        if (window is null)
        {
            _log.LogInformation("easy@work Auto-Sync: Filiale {Cp} hat keine offene Periode — übersprungen.", mapping.CompanyProfileId);
            return;   // kein State-Update
        }

        var state = await db.EasyAtWorkSyncStates
            .FirstOrDefaultAsync(s => s.CompanyProfileId == mapping.CompanyProfileId && s.Resource == Resource, ct);

        var result = await tpSync.AutoSyncAsync(mapping, window.Value.From, window.Value.To, state, ct);

        // State-Zeile sicherstellen.
        if (state == null)
        {
            state = new EasyAtWorkSyncState { CompanyProfileId = mapping.CompanyProfileId, Resource = Resource };
            db.EasyAtWorkSyncStates.Add(state);
        }

        if (result.IsBlocked)
        {
            var names = string.Join(", ", result.MissingEmployees.Take(10)
                .Select(m => (m.EawEmployeeName ?? ("easy@work-MA")) + " (#" + m.EawEmployeeId + ")"));
            state.LastError  = Truncate($"Sync blockiert: {result.MissingEmployees.Count} MA ohne Cowork-Zuordnung: {names}", 1000);
            state.LastSyncAt = DateTime.UtcNow;
            // Cursor (last_seen_updated_at) bewusst NICHT vorrücken → nächster
            // Lauf versucht es erneut, bis die MA zugeordnet sind.
            _log.LogWarning("easy@work Auto-Sync Filiale {Cp} blockiert: {N} MA ohne Zuordnung.",
                mapping.CompanyProfileId, result.MissingEmployees.Count);
        }
        else
        {
            state.LastSyncAt = DateTime.UtcNow;
            if (result.MaxUpdatedAt.HasValue) state.LastSeenUpdatedAt = result.MaxUpdatedAt;
            state.LastRowCount = result.RowCount;
            state.LastError = null;
            _log.LogInformation(
                "easy@work Auto-Sync Filiale {Cp} [{From}..{To}] ({Feed}): +{Ins} / ~{Upd} / -{Del}, {Lock} gesperrt übersprungen, {Skip} übersprungen.",
                mapping.CompanyProfileId, window.Value.From, window.Value.To,
                result.UsedUpdatesFeed ? "updates" : "voll",
                result.Inserted, result.Updated, result.Deleted, result.LockedSkipped, result.Skipped);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Fehler in last_error schreiben — eigener Scope (frischer DbContext).</summary>
    private async Task SaveErrorAsync(int companyProfileId, string error, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var state = await db.EasyAtWorkSyncStates
                .FirstOrDefaultAsync(s => s.CompanyProfileId == companyProfileId && s.Resource == Resource, ct);
            if (state == null)
            {
                state = new EasyAtWorkSyncState { CompanyProfileId = companyProfileId, Resource = Resource };
                db.EasyAtWorkSyncStates.Add(state);
            }
            state.LastError  = Truncate(error, 1000);
            state.LastSyncAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "easy@work Auto-Sync: Fehler-State für Filiale {Cp} konnte nicht gespeichert werden.", companyProfileId);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

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

/// <summary>
/// Hintergrunddienst: triggert den Auto-Sync täglich um 05:00 Europe/Zurich.
/// Abschaltbar über appsettings „EasyAtWork:AutoSyncEnabled" = false.
/// </summary>
public class EasyAtWorkAutoSyncBackgroundService : BackgroundService
{
    private readonly EasyAtWorkAutoSyncRunner _runner;
    private readonly IConfiguration _config;
    private readonly ILogger<EasyAtWorkAutoSyncBackgroundService> _log;

    public EasyAtWorkAutoSyncBackgroundService(
        EasyAtWorkAutoSyncRunner runner,
        IConfiguration config,
        ILogger<EasyAtWorkAutoSyncBackgroundService> log)
    {
        _runner = runner;
        _config = config;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!(_config.GetValue<bool?>("EasyAtWork:AutoSyncEnabled") ?? true))
        {
            _log.LogInformation("easy@work Auto-Sync ist deaktiviert (EasyAtWork:AutoSyncEnabled=false).");
            return;
        }

        // Warmup, damit der Webserver erst hochfährt.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNext0500Zurich();
            _log.LogInformation("easy@work Auto-Sync: nächster Lauf in {Hours:F1} h (05:00 Europe/Zurich).", delay.TotalHours);
            try { await Task.Delay(delay, stoppingToken); }
            catch (TaskCanceledException) { return; }
            if (stoppingToken.IsCancellationRequested) return;

            try
            {
                await _runner.RunAllBranchesAsync(stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.LogError(ex, "easy@work Auto-Sync-Lauf fehlgeschlagen — nächster Versuch morgen 05:00.");
            }
        }
    }

    private static readonly TimeZoneInfo SwissTz = FindSwissTz();
    private static TimeZoneInfo FindSwissTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich"); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
            catch { return TimeZoneInfo.Utc; }
        }
    }

    /// <summary>Zeitspanne bis zum nächsten 05:00 Europe/Zurich.</summary>
    private static TimeSpan TimeUntilNext0500Zurich()
    {
        var nowLocal  = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
        var todayAt5  = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, 5, 0, 0, DateTimeKind.Unspecified);
        var nextLocal = nowLocal < todayAt5 ? todayAt5 : todayAt5.AddDays(1);
        var nextUtc   = TimeZoneInfo.ConvertTimeToUtc(nextLocal, SwissTz);
        var delay     = nextUtc - DateTime.UtcNow;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }
}

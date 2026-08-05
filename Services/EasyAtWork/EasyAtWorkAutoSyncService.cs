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
                // STUFE 1 (Walter-Vorgabe 05.07.2026): MA-Stammdaten-Sync VOR den
                // Stempelzeiten — damit neue/geänderte MA existieren, bevor ihre
                // Stempel importiert werden (sonst laufen die Stempel ins Leere).
                // Eigener Scope, best-effort: schlägt der MA-Sync fehl, läuft der
                // Stempel-Sync trotzdem (er hat seine eigene Fehlende-MA-Sperre).
                await RunEmployeeSyncAsync(mapping, ct);

                // STUFE 2: Stempelzeiten-Sync.
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

        // STUFE 3 (Walter 05.08.2026): Verschollen-Wächter — aktive MA mit
        // easy@work-Verknüpfung, die in KEINER Aktiv-Liste mehr vorkommen
        // (Wechsel zu fremdem Franchise / vergessener Austritt), markieren →
        // kritische Dashboard-Warnung «Austritt prüfen». Best-effort.
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var empSync = scope.ServiceProvider.GetRequiredService<EasyAtWorkEmployeeSyncService>();
            var notes = await empSync.CheckVerscholleneAsync(ct);
            foreach (var n in notes)
                _log.LogInformation("[Verschollen-Check] {Note}", n);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Verschollen-Check fehlgeschlagen.");
        }

        await CleanupLogAsync(ct);
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
            AddLog(db, mapping.CompanyProfileId, "SKIPPED", null, null, "Keine offene Lohnperiode — Sync übersprungen.");
            await db.SaveChangesAsync(ct);
            return;
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
            // Block kann zwei Ursachen haben: fehlende Zuordnung (MissingEmployees)
            // ODER mehrdeutiger Lohn-MA (AmbiguousEmployees, Datenfehler). Beide in
            // die Meldung aufnehmen.
            var blockers = result.MissingEmployees.Concat(result.AmbiguousEmployees).ToList();
            var names = string.Join(", ", blockers.Take(10)
                .Select(m => (m.EawEmployeeName ?? ("easy@work-MA")) + " (#" + m.EawEmployeeId + ")"));
            var ursache = result.AmbiguousEmployees.Count > 0 && result.MissingEmployees.Count == 0
                ? "mehrere Lohn-MA für eine Person"
                : "MA ohne eindeutige Cowork-Zuordnung";
            var blockMsg = $"Sync blockiert: {blockers.Count} {ursache}: {names}";
            state.LastError  = Truncate(blockMsg, 1000);
            state.LastSyncAt = DateTime.UtcNow;
            // Cursor (last_seen_updated_at) bewusst NICHT vorrücken → nächster
            // Lauf versucht es erneut, bis die MA zugeordnet sind.
            AddLog(db, mapping.CompanyProfileId, "BLOCKED", window, result, blockMsg);
            _log.LogWarning("easy@work Auto-Sync Filiale {Cp} blockiert: {N} MA.",
                mapping.CompanyProfileId, blockers.Count);
        }
        else
        {
            state.LastSyncAt = DateTime.UtcNow;
            if (result.MaxUpdatedAt.HasValue) state.LastSeenUpdatedAt = result.MaxUpdatedAt;
            state.LastRowCount = result.RowCount;
            state.LastError = null;
            var okMsg = $"+{result.Inserted} neu / ~{result.Updated} geändert / -{result.Deleted} gelöscht"
                      + (result.LockedSkipped > 0 ? $", {result.LockedSkipped} in gesperrter Periode übersprungen" : "")
                      + (result.Skipped > 0 ? $", {result.Skipped} übersprungen" : "");
            AddLog(db, mapping.CompanyProfileId, "OK", window, result, okMsg);
            _log.LogInformation(
                "easy@work Auto-Sync Filiale {Cp} [{From}..{To}] ({Feed}): +{Ins} / ~{Upd} / -{Del}, {Lock} gesperrt übersprungen, {Skip} übersprungen.",
                mapping.CompanyProfileId, window.Value.From, window.Value.To,
                result.UsedUpdatesFeed ? "updates" : "voll",
                result.Inserted, result.Updated, result.Deleted, result.LockedSkipped, result.Skipped);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// STUFE 1 des Auto-Syncs: MA-Stammdaten-Sync einer Filiale (eigener Scope,
    /// best-effort). Läuft VOR dem Stempel-Sync, damit neue/geänderte MA existieren.
    /// Nutzt den normalen Massenimport-Commit (SelectedNumbers=null → alle NEW+UPDATE
    /// automatisch; Konflikte wie 2 Funktionen werden übersprungen). Walter 05.07.2026.
    /// </summary>
    private async Task RunEmployeeSyncAsync(EasyAtWorkBranchMapping mapping, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var empSync = scope.ServiceProvider.GetRequiredService<EasyAtWorkEmployeeSyncService>();
            var req = new EasyAtWorkEmployeeSyncService.SyncRequest
            {
                CompanyProfileId = mapping.CompanyProfileId,
                OnlyActive       = false,   // alle relevanten MA (inkl. ausgetretene, ohne Pre-Mirus) — wie der Stempel-Filter
                SelectedNumbers  = null     // alle NEW+UPDATE automatisch übernehmen
            };
            var res = await empSync.CommitAsync(req, null, ct);
            _log.LogInformation(
                "easy@work Auto-Sync Filiale {Cp}: MA-Vorstufe +{Ins} neu / ~{Upd} geändert / {Conf} Konflikt(e) / {Exist} Wiedereintritt(e).",
                mapping.CompanyProfileId, res.CountInserted, res.CountUpdated, res.CountConflict, res.CountExisting);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "easy@work Auto-Sync Filiale {Cp}: MA-Vorstufe fehlgeschlagen — Stempel-Sync läuft trotzdem.",
                mapping.CompanyProfileId);
        }
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
            AddLog(db, companyProfileId, "ERROR", null, null, error);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "easy@work Auto-Sync: Fehler-State für Filiale {Cp} konnte nicht gespeichert werden.", companyProfileId);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>
    /// Catch-up-Entscheidung (Walter-Vorgabe 19.06.2026, seiteneffektfrei →
    /// testbar): nach einem Neustart soll der heutige Lauf SOFORT nachgeholt
    /// werden, wenn es lokal bereits NACH 05:00 ist UND mindestens eine aktiv
    /// gemappte Filiale heute (lokal) noch keinen erfolgreichen Sync hatte.
    /// Vor 05:00 nicht — dann greift der normale 05:00-Lauf ohnehin.
    /// </summary>
    public static bool ShouldCatchUp(
        DateTime nowLocal, IReadOnlyCollection<DateOnly?> lastSuccessLocalDatePerActiveBranch)
    {
        if (lastSuccessLocalDatePerActiveBranch.Count == 0) return false;       // keine aktive Filiale
        if (nowLocal.TimeOfDay < new TimeSpan(5, 0, 0)) return false;           // vor 05:00 → normaler Lauf
        var today = DateOnly.FromDateTime(nowLocal);
        return lastSuccessLocalDatePerActiveBranch.Any(d => d != today);        // mind. eine heute noch nicht
    }

    /// <summary>Lädt den Sync-Stand und entscheidet, ob ein Nachhol-Lauf nötig ist.</summary>
    public async Task<bool> NeedsCatchUpAsync(CancellationToken ct)
    {
        if (!_client.IsConfigured) return false;
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var activeCpIds = await db.EasyAtWorkBranchMappings
            .Where(m => m.AutoSyncEnabled)
            .Select(m => m.CompanyProfileId)
            .ToListAsync(ct);
        if (activeCpIds.Count == 0) return false;

        var states = await db.EasyAtWorkSyncStates
            .Where(s => s.Resource == "TIMEPUNCH" && activeCpIds.Contains(s.CompanyProfileId))
            .Select(s => new { s.CompanyProfileId, s.LastSyncAt })
            .ToListAsync(ct);
        var lastByCp = states.ToDictionary(s => s.CompanyProfileId, s => s.LastSyncAt);

        var dates = activeCpIds.Select(cp =>
        {
            if (lastByCp.TryGetValue(cp, out var last) && last.HasValue)
            {
                var utc = DateTime.SpecifyKind(last.Value, DateTimeKind.Utc);
                return (DateOnly?)DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, SwissTz));
            }
            return (DateOnly?)null;   // noch nie gelaufen → zählt als „heute nicht"
        }).ToList();

        return ShouldCatchUp(SwissNow(), dates);
    }

    /// <summary>Hängt eine Protokoll-Zeile an (wird mit dem nächsten SaveChanges persistiert).</summary>
    private static void AddLog(
        AppDbContext db, int companyProfileId, string status,
        (DateOnly From, DateOnly To)? window,
        EasyAtWorkTimepunchSyncService.AutoSyncResult? r, string? message)
    {
        db.EasyAtWorkSyncLogs.Add(new EasyAtWorkSyncLog
        {
            CompanyProfileId = companyProfileId,
            RunAt            = DateTime.UtcNow,
            Status           = status,
            PeriodFrom       = window?.From,
            PeriodTo         = window?.To,
            UsedUpdatesFeed  = r?.UsedUpdatesFeed ?? false,
            Inserted         = r?.Inserted ?? 0,
            Updated          = r?.Updated ?? 0,
            Deleted          = r?.Deleted ?? 0,
            LockedSkipped    = r?.LockedSkipped ?? 0,
            Skipped          = r?.Skipped ?? 0,
            MissingCount     = (r?.MissingEmployees.Count ?? 0) + (r?.AmbiguousEmployees.Count ?? 0),
            Message          = message == null ? null : Truncate(message, 1000),
            DetailJson       = BuildDetailJson(r),
        });
    }

    /// <summary>Detail der echten Änderungen als JSON (gedeckelt auf 1000 Zeilen,
    /// damit die Spalte nicht ausufert — der Rest wird über totalChanges gemeldet).</summary>
    private static string? BuildDetailJson(EasyAtWorkTimepunchSyncService.AutoSyncResult? r)
    {
        if (r == null || r.Changes.Count == 0) return null;
        const int cap = 1000;
        var payload = new
        {
            totalChanges = r.Changes.Count,
            capped       = r.Changes.Count > cap,
            changes      = r.Changes.Take(cap).Select(c => new {
                empId  = c.EmployeeId,
                date   = c.Date.ToString("yyyy-MM-dd"),
                action = c.Action,
                oldTotal = c.OldTotal, newTotal = c.NewTotal,
                oldNight = c.OldNight, newNight = c.NewNight
            })
        };
        return System.Text.Json.JsonSerializer.Serialize(payload);
    }

    /// <summary>Protokoll-Einträge älter als 90 Tage entfernen (eigener Scope).</summary>
    private async Task CleanupLogAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cutoff = DateTime.UtcNow.AddDays(-90);
            await db.EasyAtWorkSyncLogs.Where(l => l.RunAt < cutoff).ExecuteDeleteAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "easy@work Auto-Sync: Log-Cleanup fehlgeschlagen.");
        }
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

        // Catch-up nach Neustart: wenn der Server nach 05:00 hochkommt, wäre der
        // heutige Lauf sonst verpasst. Sofort nachholen, wenn heute (lokal) noch
        // nicht gelaufen.
        try
        {
            if (await _runner.NeedsCatchUpAsync(stoppingToken))
            {
                _log.LogInformation("easy@work Auto-Sync: Nachhol-Lauf nach Neustart (heute noch nicht gelaufen).");
                await _runner.RunAllBranchesAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _log.LogError(ex, "easy@work Auto-Sync: Catch-up-Lauf fehlgeschlagen.");
        }

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

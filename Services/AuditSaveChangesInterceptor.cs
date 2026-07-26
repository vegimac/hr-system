using System.Data;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace HrSystem.Services;

/// <summary>
/// Walter-Vorgabe 27.05.2026: zentrales Voll-Audit (Writes) ueber EF Core.
///
/// Architektur-Wichtig (Walter 27.05.2026, nach Crash beim MA-Speichern):
/// das Audit-Log darf den User-Write NIEMALS blockieren. Daher:
///  1) WAEHREND SavingChanges sammeln wir nur die Aenderungen in eine
///     Liste (kein ChangeTracker-Add, kein zusaetzlicher INSERT).
///  2) NACH erfolgreichem SaveChanges (SavedChanges) schreiben wir die
///     gesammelten Audit-Eintraege per RAW SQL in einer eigenen
///     try/catch-Klammer. Wenn etwas schief geht, wird der Fehler nur
///     geloggt — der User-Write ist schon committet.
///
/// Walter-Bug 26.07.2026: nach EINEM fehlgeschlagenen Batch-Insert wurde
/// <c>_auditDisabled</c> dauerhaft true → Import + manuelle Edits ab dann
/// unsichtbar im Aktivitäts-Log (Liste blieb bei 23.07. stehen). Neu:
/// kein permanentes Disable; Insert per ADO.NET Named-Params (kein
/// ExecuteSqlRaw/::jsonb); nur bei «Tabelle fehlt» kurz pausieren.
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditSaveChangesInterceptor> _log;

    /// <summary>
    /// Nur bei «audit_log fehlt»: Pause bis <see cref="_pauseUntilUtc"/>.
    /// Transiente Fehler (JSON, Param-Limit, Timeout) deaktivieren NICHT mehr.
    /// </summary>
    private static DateTime _pauseUntilUtc = DateTime.MinValue;
    private static readonly object _pauseLock = new();

    private static readonly HashSet<string> _skipTypes = new()
    {
        nameof(AuditLog),
        nameof(PayrollPeriodeAudit),
    };

    private static readonly System.Threading.AsyncLocal<List<PendingAudit>?> _pending = new();

    public AuditSaveChangesInterceptor(
        IHttpContextAccessor http,
        ILogger<AuditSaveChangesInterceptor> log)
    {
        _http = http;
        _log  = log;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is AppDbContext ctx) Collect(ctx);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext ctx) Collect(ctx);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        RefreshNewEntityIds(eventData.Context as AppDbContext);
        TryPersist(eventData.Context as AppDbContext);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        RefreshNewEntityIds(eventData.Context as AppDbContext);
        TryPersist(eventData.Context as AppDbContext);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        _pending.Value = null;
        base.SaveChangesFailed(eventData);
    }
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending.Value = null;
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Collect(AppDbContext ctx)
    {
        if (IsPaused()) return;
        try
        {
            var entries = ctx.ChangeTracker.Entries()
                .Where(e => e.Entity != null
                            && !_skipTypes.Contains(e.Entity.GetType().Name)
                            && (e.State == EntityState.Added
                             || e.State == EntityState.Modified
                             || e.State == EntityState.Deleted))
                .ToList();
            if (entries.Count == 0) { _pending.Value = null; return; }

            int?    userId   = null;
            string? userName = null;
            string? userRole = null;
            string? route    = null;
            string? ip       = null;
            var http = _http?.HttpContext;
            if (http != null)
            {
                var idClaim = http.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(idClaim, out var uid)) userId = uid;
                userName = http.User?.FindFirst(ClaimTypes.Name)?.Value
                        ?? http.User?.Identity?.Name;
                userRole = http.User?.FindFirst(ClaimTypes.Role)?.Value;
                try { route = http.Request.Method + " " + http.Request.Path.Value; } catch { }
                try { ip    = http.Connection?.RemoteIpAddress?.ToString(); } catch { }
            }
            else
            {
                // Background-Services (Auto-Sync 05:00, Cleanup …) — trotzdem loggen.
                userName = "System";
                userRole = "system";
                route = "(background)";
            }

            var pendings = new List<PendingAudit>();
            foreach (var entry in entries)
            {
                var entityType = entry.Entity.GetType().Name;
                string action = entry.State switch
                {
                    EntityState.Added    => "CREATE",
                    EntityState.Modified => "UPDATE",
                    EntityState.Deleted  => "DELETE",
                    _ => "OTHER"
                };
                if (action == "OTHER") continue;

                var changes = new Dictionary<string, object?>();
                if (entry.State == EntityState.Modified)
                {
                    bool anyChange = false;
                    foreach (var prop in entry.Properties)
                    {
                        if (prop.IsModified && !Equals(prop.OriginalValue, prop.CurrentValue))
                        {
                            changes[prop.Metadata.Name] = new
                            {
                                old = Sanitize(prop.OriginalValue),
                                @new = Sanitize(prop.CurrentValue),
                            };
                            anyChange = true;
                        }
                    }
                    if (!anyChange) continue;
                }
                else
                {
                    foreach (var prop in entry.Properties)
                    {
                        var val = entry.State == EntityState.Deleted
                            ? prop.OriginalValue
                            : prop.CurrentValue;
                        changes[prop.Metadata.Name] = Sanitize(val);
                    }
                }

                string? pk = null;
                try
                {
                    var pkValues = entry.Properties
                        .Where(p => p.Metadata.IsPrimaryKey())
                        .Select(p => entry.State == EntityState.Deleted ? p.OriginalValue : p.CurrentValue)
                        .Where(v => v != null)
                        .ToList();
                    if (pkValues.Count == 1) pk = pkValues[0]?.ToString();
                    else if (pkValues.Count > 1) pk = string.Join("|", pkValues);
                }
                catch { }

                if (entry.State == EntityState.Added && (pk == null || pk == "0"))
                    pk = null;

                string? changesJson;
                try { changesJson = JsonSerializer.Serialize(changes); }
                catch { changesJson = "{}"; }

                pendings.Add(new PendingAudit(
                    CreatedAt: SwissNowUnspecified(),
                    UserId:    userId,
                    UserName:  userName,
                    UserRole:  userRole,
                    EntityType: entityType,
                    EntityId:   pk,
                    Action:     action,
                    ChangesJson: changesJson,
                    Route:       route,
                    IpAddress:   ip,
                    EntityRef:   entry.State == EntityState.Added ? entry.Entity : null));
            }

            _pending.Value = pendings.Count > 0 ? pendings : null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Audit-Collect fehlgeschlagen — User-Write laeuft normal weiter.");
            _pending.Value = null;
        }
    }

    private void RefreshNewEntityIds(AppDbContext? ctx)
    {
        var list = _pending.Value;
        if (ctx == null || list == null || list.Count == 0) return;
        for (var i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (a.Action != "CREATE" || a.EntityRef == null) continue;
            if (!string.IsNullOrEmpty(a.EntityId) && a.EntityId != "0") continue;
            try
            {
                var entry = ctx.Entry(a.EntityRef);
                var pkValues = entry.Properties
                    .Where(p => p.Metadata.IsPrimaryKey())
                    .Select(p => p.CurrentValue)
                    .Where(v => v != null)
                    .ToList();
                string? pk = null;
                if (pkValues.Count == 1) pk = pkValues[0]?.ToString();
                else if (pkValues.Count > 1) pk = string.Join("|", pkValues);
                if (!string.IsNullOrEmpty(pk) && pk != "0")
                    list[i] = a with { EntityId = pk, EntityRef = null };
            }
            catch { /* best-effort */ }
        }
    }

    private void TryPersist(AppDbContext? ctx)
    {
        var list = _pending.Value;
        _pending.Value = null;
        if (ctx == null || list == null || list.Count == 0 || IsPaused()) return;

        // ADO.NET mit Named-Params — robuster als ExecuteSqlRaw({0}…)+DBNull
        // (Walter 26.07.2026: nach Deploy immer noch stumm → Insert-Pfad gehärtet).
        try
        {
            var conn = ctx.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                ctx.Database.OpenConnection(); // EF verwaltet Close/Dispose
            var tx = ctx.Database.CurrentTransaction?.GetDbTransaction();

            foreach (var row in list)
            {
                if (!TryInsertRow(conn, tx, row))
                {
                    _log.LogWarning(
                        "Audit-Zeile übersprungen: {Action} {Entity} #{Id} (Route {Route})",
                        row.Action, row.EntityType, row.EntityId, row.Route);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Audit-Persist fehlgeschlagen — User-Write bleibt erhalten.");
            MaybePauseIfTableMissing(ex);
        }
    }

    private bool TryInsertRow(DbConnection conn, DbTransaction? tx, PendingAudit a)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            // changes_json = TEXT (kein ::jsonb) — Cast-Fehler hat Audit früher killt.
            cmd.CommandText =
                "INSERT INTO audit_log (created_at,user_id,user_name,user_role,entity_type,entity_id,action,changes_json,route,ip_address) "
                + "VALUES (@created_at,@user_id,@user_name,@user_role,@entity_type,@entity_id,@action,@changes_json,@route,@ip_address)";

            AddParam(cmd, "@created_at", a.CreatedAt);
            AddParam(cmd, "@user_id", a.UserId);
            AddParam(cmd, "@user_name", a.UserName);
            AddParam(cmd, "@user_role", a.UserRole);
            AddParam(cmd, "@entity_type", a.EntityType);
            AddParam(cmd, "@entity_id", a.EntityId);
            AddParam(cmd, "@action", a.Action);
            AddParam(cmd, "@changes_json", a.ChangesJson);
            AddParam(cmd, "@route", a.Route);
            AddParam(cmd, "@ip_address", a.IpAddress);

            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Audit-Insert fehlgeschlagen: {Action} {Entity} #{Id} — {Msg}",
                a.Action, a.EntityType, a.EntityId, ex.GetBaseException().Message);
            MaybePauseIfTableMissing(ex);
            return false;
        }
    }

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private void MaybePauseIfTableMissing(Exception ex)
    {
        var msg = ex.GetBaseException().Message ?? "";
        if (msg.Contains("audit_log", StringComparison.OrdinalIgnoreCase)
            && (msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("existiert nicht", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("42P01", StringComparison.OrdinalIgnoreCase)))
        {
            PauseFor(TimeSpan.FromMinutes(30));
            _log.LogWarning(
                "Audit-Tabelle fehlt — Audit 30 Min pausiert. Migration add_audit_log.sql ausfuehren.");
        }
    }

    private static bool IsPaused()
    {
        lock (_pauseLock) return DateTime.UtcNow < _pauseUntilUtc;
    }

    private static void PauseFor(TimeSpan duration)
    {
        lock (_pauseLock) _pauseUntilUtc = DateTime.UtcNow.Add(duration);
    }

    private static object? Sanitize(object? v)
    {
        if (v == null) return null;
        if (v is string s) return s.Length > 4000 ? s.Substring(0, 4000) + "…[gekuerzt]" : s;
        if (v is byte[] b) return $"<binary {b.Length} bytes>";
        if (v is DateTime dt) return dt.ToString("o");
        if (v is DateOnly d)  return d.ToString("yyyy-MM-dd");
        if (v is TimeOnly t)  return t.ToString("HH:mm:ss");
        return v;
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

    private static DateTime SwissNowUnspecified()
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }

    private record PendingAudit(
        DateTime  CreatedAt,
        int?      UserId,
        string?   UserName,
        string?   UserRole,
        string    EntityType,
        string?   EntityId,
        string    Action,
        string?   ChangesJson,
        string?   Route,
        string?   IpAddress,
        object?   EntityRef = null);
}

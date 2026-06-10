using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Security.Claims;
using System.Text.Json;

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
///     try/catch-Klammer. Wenn audit_log fehlt oder etwas schief geht,
///     wird der Fehler nur geloggt — der User-Write ist schon committet.
///  3) Wenn AuditDisabled-Flag gesetzt ist (z.B. Tabelle existiert nicht),
///     ueberspringen wir das ganz still.
///
/// Spezialfaelle:
///  - AuditLog selbst wird NIE geloggt (sonst Endlosschleife).
///  - PayrollPeriodeAudit wird ebenfalls nicht geloggt (eigene Audit-Quelle).
///  - „kosmetische" Property-Updates ohne echte Wertaenderung werden
///    von EF eigentlich nicht als Modified markiert (Standard-Verhalten);
///    sicherheitshalber filtern wir Modified ohne tatsaechliche Aenderung raus.
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditSaveChangesInterceptor> _log;

    // Wird auf true gesetzt, sobald ein DB-Fehler beim Audit-Insert auftritt
    // (z.B. Tabelle audit_log existiert nicht). Verhindert Spam.
    private static bool _auditDisabled = false;

    // Entitaeten, die NICHT geloggt werden (sonst rekursiv oder reines Rauschen).
    private static readonly HashSet<string> _skipTypes = new()
    {
        nameof(AuditLog),
        nameof(PayrollPeriodeAudit),
    };

    // Pending-Audits werden zwischen SavingChanges und SavedChanges
    // pro DbContext-Instanz im AsyncLocal abgelegt — KEIN Singleton-State,
    // sonst klauen sich parallele Requests die Listen.
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
        TryPersist(eventData.Context as AppDbContext);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        TryPersist(eventData.Context as AppDbContext);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        // User-Write war fehlerhaft → pending Audits wegwerfen, NICHT loggen.
        _pending.Value = null;
        base.SaveChangesFailed(eventData);
    }
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending.Value = null;
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    // ── Phase 1: Aenderungen sammeln, NICHT in den ChangeTracker schreiben ──
    private void Collect(AppDbContext ctx)
    {
        if (_auditDisabled) return;
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

            // User-Kontext
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

                string? changesJson;
                try { changesJson = JsonSerializer.Serialize(changes); }
                catch { changesJson = "{}"; }

                pendings.Add(new PendingAudit(
                    CreatedAt: DateTime.UtcNow,
                    UserId:    userId,
                    UserName:  userName,
                    UserRole:  userRole,
                    EntityType: entityType,
                    EntityId:   pk,
                    Action:     action,
                    ChangesJson: changesJson,
                    Route:       route,
                    IpAddress:   ip));
            }

            _pending.Value = pendings.Count > 0 ? pendings : null;
        }
        catch (Exception ex)
        {
            // NIE den User-Write killen — Audit-Collection-Fehler still ignorieren.
            _log.LogWarning(ex, "Audit-Collect fehlgeschlagen — User-Write laeuft normal weiter.");
            _pending.Value = null;
        }
    }

    // ── Phase 2: nach erfolgreichem SaveChanges audit_log via Raw-SQL fuellen ──
    private void TryPersist(AppDbContext? ctx)
    {
        var list = _pending.Value;
        _pending.Value = null;
        if (ctx == null || list == null || list.Count == 0 || _auditDisabled) return;
        try
        {
            // EINE Multi-Row-INSERT pro SaveChanges (statt N Roundtrips).
            var sql = new System.Text.StringBuilder();
            sql.Append("INSERT INTO audit_log (created_at,user_id,user_name,user_role,entity_type,entity_id,action,changes_json,route,ip_address) VALUES ");
            var args = new List<object?>();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sql.Append(',');
                int b = i * 10;
                sql.Append($"({{{b}}},{{{b+1}}},{{{b+2}}},{{{b+3}}},{{{b+4}}},{{{b+5}}},{{{b+6}}},{{{b+7}}}::jsonb,{{{b+8}}},{{{b+9}}})");
                var a = list[i];
                args.Add(a.CreatedAt);
                args.Add((object?)a.UserId    ?? DBNull.Value);
                args.Add((object?)a.UserName  ?? DBNull.Value);
                args.Add((object?)a.UserRole  ?? DBNull.Value);
                args.Add(a.EntityType);
                args.Add((object?)a.EntityId    ?? DBNull.Value);
                args.Add(a.Action);
                args.Add((object?)a.ChangesJson ?? DBNull.Value);
                args.Add((object?)a.Route       ?? DBNull.Value);
                args.Add((object?)a.IpAddress   ?? DBNull.Value);
            }
            // ExecuteSqlRaw — geht NICHT durch den SaveChanges-Lifecycle,
            // also kein rekursives „Audit fuer Audit".
            ctx.Database.ExecuteSqlRaw(sql.ToString(), args.ToArray());
        }
        catch (Exception ex)
        {
            // Tabelle fehlt? Schema-Mismatch? → Audit fuer den Rest der
            // Laufzeit deaktivieren. User-Write ist schon erfolgreich
            // gespeichert — der Fehler bleibt fuer den User unsichtbar.
            _auditDisabled = true;
            _log.LogWarning(ex,
                "Audit-Log konnte nicht geschrieben werden — Audit fuer diese Session deaktiviert. " +
                "Hinweis: Migration migrations-archive/add_audit_log.sql in TablePlus ausfuehren.");
        }
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
        string?   IpAddress);
}

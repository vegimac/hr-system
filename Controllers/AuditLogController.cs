using System.Text;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Walter-Vorgabe 27.05.2026: Admin-Sicht auf das zentrale Audit-Log.
/// Nur admin darf das Log lesen — sonst koennten User die Aktivitaeten
/// anderer User einsehen (Datenschutz).
/// </summary>
[ApiController]
[Route("api/audit-log")]
[Authorize(Roles = "admin")]
public class AuditLogController : ControllerBase
{
    private readonly AppDbContext _db;
    public AuditLogController(AppDbContext db) { _db = db; }

    /// <summary>
    /// Liste mit Filter: nach Datum, User, Entity-Typ, Action.
    /// Walter 26.07.2026: Employee-Zeilen mit Personalnummer+Name; Suche
    /// «Strasse» findet auch Feld «Street»; Personalnummer/Name matcht MA.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLog(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to   = null,
        [FromQuery] int?      userId = null,
        [FromQuery] string?   entityType = null,
        [FromQuery] string?   entityId   = null,
        [FromQuery] string?   action     = null,
        [FromQuery] string?   search     = null,
        [FromQuery] int       limit      = 200)
    {
        var q = ApplyFilters(_db.AuditLogs.AsQueryable(), from, to, userId, entityType, entityId, action);
        q = await ApplySearchAsync(q, search);

        var capped = Math.Clamp(limit, 1, 2000);
        var rows = await q.OrderByDescending(a => a.CreatedAt)
                          .Take(capped)
                          .Select(a => new AuditRowDto(
                              a.Id, a.CreatedAt, a.UserId, a.UserName, a.UserRole,
                              a.EntityType, a.EntityId, a.Action, a.ChangesJson,
                              a.Route, a.IpAddress, null, null))
                          .ToListAsync();

        var enriched = await EnrichEmployeesAsync(rows);
        return Ok(enriched.Select(r => new {
            id = r.Id,
            createdAt = r.CreatedAt,
            userId = r.UserId,
            userName = r.UserName,
            userRole = r.UserRole,
            entityType = r.EntityType,
            entityId = r.EntityId,
            action = r.Action,
            changesJson = r.ChangesJson,
            route = r.Route,
            ipAddress = r.IpAddress,
            employeeNumber = r.EmployeeNumber,
            employeeName = r.EmployeeName,
        }));
    }

    /// <summary>
    /// Liste der unterschiedlichen Entity-Typen — fuer das Filter-Dropdown.
    /// </summary>
    [HttpGet("entity-types")]
    public async Task<IActionResult> GetEntityTypes()
    {
        var types = await _db.AuditLogs
            .Select(a => a.EntityType)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();
        return Ok(types);
    }

    /// <summary>
    /// Health-Check: schreibt das Aktivitäts-Log noch?
    /// Stille-Schwelle = dashboard_warning_config.warn_days für
    /// «audit_log_stumm» (Default 1 Tag = 24 h).
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth()
    {
        var silenceDays = await _db.DashboardWarningConfigs.AsNoTracking()
            .Where(c => c.Category == "audit_log_stumm" && c.WarnDays != null)
            .Select(c => c.WarnDays!.Value)
            .FirstOrDefaultAsync();
        if (silenceDays <= 0) silenceDays = AuditLogHealth.DefaultSilenceDays;

        var h = await AuditLogHealth.CheckAsync(_db, silenceDays);
        var lastTxt = h.LastCreatedAt?.ToString("dd.MM.yyyy HH:mm");
        var silentDaysFloor = h.LastCreatedAt.HasValue
            ? (int)Math.Floor(h.SilentHours / 24.0)
            : silenceDays;
        return Ok(new {
            ok = h.Ok,
            lastCreatedAt = h.LastCreatedAt,
            lastCreatedAtText = lastTxt,
            silentHours = double.IsInfinity(h.SilentHours) ? (double?)null : Math.Round(h.SilentHours, 1),
            silentDays = h.LastCreatedAt.HasValue ? silentDaysFloor : (int?)null,
            thresholdHours = h.ThresholdHours,
            silenceDaysConfig = h.SilenceDays,
            message = h.Ok
                ? "Aktivitäts-Log schreibt."
                : (h.LastCreatedAt.HasValue
                    ? $"Aktivitäts-Log schreibt nicht mehr — letzter Eintrag {lastTxt} (Stille ≥ {h.SilenceDays} Tag(e))."
                    : "Aktivitäts-Log ist leer — es wurde noch nie protokolliert.")
        });
    }

    /// <summary>
    /// Aenderungs-Historie einer einzelnen Entitaet (z.B. ein MA mit ID 123).
    /// </summary>
    [HttpGet("entity/{entityType}/{entityId}")]
    public async Task<IActionResult> GetForEntity(string entityType, string entityId)
    {
        var rows = await _db.AuditLogs
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new {
                id          = a.Id,
                createdAt   = a.CreatedAt,
                userId      = a.UserId,
                userName    = a.UserName,
                userRole    = a.UserRole,
                action      = a.Action,
                changesJson = a.ChangesJson,
                route       = a.Route,
            })
            .ToListAsync();
        return Ok(rows);
    }

    /// <summary>CSV-Export mit aktuellen Filtern.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to   = null,
        [FromQuery] int?      userId = null,
        [FromQuery] string?   entityType = null,
        [FromQuery] string?   action     = null,
        [FromQuery] string?   search     = null)
    {
        var q = ApplyFilters(_db.AuditLogs.AsQueryable(), from, to, userId, entityType, null, action);
        q = await ApplySearchAsync(q, search);

        var rows = await q.OrderByDescending(a => a.CreatedAt).Take(50000)
            .Select(a => new AuditRowDto(
                a.Id, a.CreatedAt, a.UserId, a.UserName, a.UserRole,
                a.EntityType, a.EntityId, a.Action, a.ChangesJson,
                a.Route, a.IpAddress, null, null))
            .ToListAsync();
        var enriched = await EnrichEmployeesAsync(rows);

        var sb = new StringBuilder();
        sb.AppendLine("Zeit;User;Rolle;Aktion;Entitaet;ID;Personalnummer;MA-Name;Route;IP;Aenderungen");
        foreach (var r in enriched)
        {
            string esc(string? s) => s == null ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";
            sb.Append(r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")).Append(';');
            sb.Append(esc(r.UserName ?? (r.UserId?.ToString() ?? ""))).Append(';');
            sb.Append(esc(r.UserRole)).Append(';');
            sb.Append(esc(r.Action)).Append(';');
            sb.Append(esc(r.EntityType)).Append(';');
            sb.Append(esc(r.EntityId)).Append(';');
            sb.Append(esc(r.EmployeeNumber)).Append(';');
            sb.Append(esc(r.EmployeeName)).Append(';');
            sb.Append(esc(r.Route)).Append(';');
            sb.Append(esc(r.IpAddress)).Append(';');
            sb.Append(esc(r.ChangesJson)).AppendLine();
        }
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var name = $"audit-log_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        return File(bytes, "text/csv; charset=utf-8", name);
    }

    private static IQueryable<AuditLog> ApplyFilters(
        IQueryable<AuditLog> q,
        DateTime? from, DateTime? to, int? userId,
        string? entityType, string? entityId, string? action)
    {
        if (from.HasValue) q = q.Where(a => a.CreatedAt >= from.Value);
        if (to.HasValue)   q = q.Where(a => a.CreatedAt <= to.Value.AddDays(1));
        if (userId.HasValue) q = q.Where(a => a.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(entityType))
            q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(entityId))
            q = q.Where(a => a.EntityId == entityId);
        if (!string.IsNullOrWhiteSpace(action))
            q = q.Where(a => a.Action == action.ToUpperInvariant());
        return q;
    }

    private async Task<IQueryable<AuditLog>> ApplySearchAsync(
        IQueryable<AuditLog> q, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return q;

        var s = search.Trim();
        // DE-Alias: «Strasse» / «straße» → JSON-Feld «Street»
        bool wantStreet = s.Equals("strasse", StringComparison.OrdinalIgnoreCase)
                       || s.Equals("straße", StringComparison.OrdinalIgnoreCase)
                       || s.Equals("street", StringComparison.OrdinalIgnoreCase);

        // Personalnummer / Name → Employee-EntityIds
        var empIdStrs = await _db.Employees.AsNoTracking()
            .Where(e => EF.Functions.ILike(e.EmployeeNumber, "%" + s + "%")
                     || EF.Functions.ILike(e.FirstName, "%" + s + "%")
                     || EF.Functions.ILike(e.LastName, "%" + s + "%"))
            .Select(e => e.Id.ToString())
            .Take(400)
            .ToListAsync();

        if (wantStreet)
        {
            q = q.Where(a =>
                (a.ChangesJson != null && (
                    EF.Functions.ILike(a.ChangesJson, "%Street%")
                    || EF.Functions.ILike(a.ChangesJson, "%" + s + "%")))
                || (a.UserName != null && EF.Functions.ILike(a.UserName, "%" + s + "%"))
                || (a.Route    != null && EF.Functions.ILike(a.Route,    "%" + s + "%"))
                || (a.EntityType == "Employee" && a.EntityId != null && empIdStrs.Contains(a.EntityId)));
        }
        else
        {
            q = q.Where(a =>
                (a.ChangesJson != null && EF.Functions.ILike(a.ChangesJson, "%" + s + "%"))
                || (a.UserName != null && EF.Functions.ILike(a.UserName, "%" + s + "%"))
                || (a.Route    != null && EF.Functions.ILike(a.Route,    "%" + s + "%"))
                || (a.EntityType == "Employee" && a.EntityId != null && empIdStrs.Contains(a.EntityId)));
        }
        return q;
    }

    // Walter 16.08.2026: MA-Bezug auch aus dem ChangesJson ziehen («EmployeeId»
    // als nackte Zahl ODER als {old/new}-Objekt) — damit zeigen z.B. Absenz-,
    // Ferienplanungs- und Dienstplan-Zeilen Vorname/Name + MA-Nummer statt Id.
    private static readonly System.Text.RegularExpressions.Regex _empIdRx =
        new("\"EmployeeId\"\\s*:\\s*(?:\\{[^}]*?\"new\"\\s*:\\s*)?(\\d+)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static int? ExtractEmployeeId(AuditRowDto r)
    {
        if (r.EntityType == "Employee" && int.TryParse(r.EntityId, out var id)) return id;
        if (r.ChangesJson is null) return null;
        var m = _empIdRx.Match(r.ChangesJson);
        return m.Success && int.TryParse(m.Groups[1].Value, out var jid) ? jid : null;
    }

    private async Task<List<AuditRowDto>> EnrichEmployeesAsync(List<AuditRowDto> rows)
    {
        var empIds = new List<int>();
        foreach (var r in rows)
        {
            var id0 = ExtractEmployeeId(r);
            if (id0.HasValue) empIds.Add(id0.Value);
        }
        empIds = empIds.Distinct().ToList();

        var map = empIds.Count == 0
            ? new Dictionary<int, (string Nr, string Name)>()
            : (await _db.Employees.AsNoTracking()
                .Where(e => empIds.Contains(e.Id))
                .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName })
                .ToListAsync())
              .ToDictionary(
                  e => e.Id,
                  e => (e.EmployeeNumber ?? "", $"{e.FirstName} {e.LastName}".Trim()));

        return rows.Select(r =>
        {
            string? nr = null, name = null;
            var id = ExtractEmployeeId(r);
            if (id.HasValue && map.TryGetValue(id.Value, out var info))
            {
                nr = info.Item1;
                name = info.Item2;
            }
            return r with { EmployeeNumber = nr, EmployeeName = name };
        }).ToList();
    }

    private sealed record AuditRowDto(
        long Id, DateTime CreatedAt, int? UserId, string? UserName, string? UserRole,
        string EntityType, string? EntityId, string Action, string? ChangesJson,
        string? Route, string? IpAddress, string? EmployeeNumber, string? EmployeeName);
}

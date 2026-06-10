using HrSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

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
    /// </summary>
    /// <param name="from">ISO-Datum, von (inkl.)</param>
    /// <param name="to">ISO-Datum, bis (inkl.)</param>
    /// <param name="userId">Nur Aenderungen dieses Users</param>
    /// <param name="entityType">Nur diese Entity-Klasse (z.B. „Employee")</param>
    /// <param name="entityId">Nur diese Entitaet (zusammen mit entityType)</param>
    /// <param name="action">CREATE / UPDATE / DELETE</param>
    /// <param name="search">Volltext in changes_json, user_name, route</param>
    /// <param name="limit">Max. Einträge (Default 200, hart limitiert auf 2000)</param>
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
        var q = _db.AuditLogs.AsQueryable();

        if (from.HasValue) q = q.Where(a => a.CreatedAt >= from.Value);
        if (to.HasValue)   q = q.Where(a => a.CreatedAt <= to.Value.AddDays(1));
        if (userId.HasValue) q = q.Where(a => a.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(entityType))
            q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(entityId))
            q = q.Where(a => a.EntityId == entityId);
        if (!string.IsNullOrWhiteSpace(action))
            q = q.Where(a => a.Action == action.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(a =>
                (a.ChangesJson != null && EF.Functions.ILike(a.ChangesJson, "%" + s + "%"))
                || (a.UserName != null && EF.Functions.ILike(a.UserName, "%" + s + "%"))
                || (a.Route    != null && EF.Functions.ILike(a.Route,    "%" + s + "%")));
        }

        var capped = Math.Clamp(limit, 1, 2000);
        var rows = await q.OrderByDescending(a => a.CreatedAt)
                          .Take(capped)
                          .Select(a => new {
                              id          = a.Id,
                              createdAt   = a.CreatedAt,
                              userId      = a.UserId,
                              userName    = a.UserName,
                              userRole    = a.UserRole,
                              entityType  = a.EntityType,
                              entityId    = a.EntityId,
                              action      = a.Action,
                              changesJson = a.ChangesJson,
                              route       = a.Route,
                              ipAddress   = a.IpAddress,
                          })
                          .ToListAsync();
        return Ok(rows);
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
        var q = _db.AuditLogs.AsQueryable();
        if (from.HasValue) q = q.Where(a => a.CreatedAt >= from.Value);
        if (to.HasValue)   q = q.Where(a => a.CreatedAt <= to.Value.AddDays(1));
        if (userId.HasValue) q = q.Where(a => a.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(action))     q = q.Where(a => a.Action == action.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(a =>
                (a.ChangesJson != null && EF.Functions.ILike(a.ChangesJson, "%" + s + "%"))
                || (a.UserName != null && EF.Functions.ILike(a.UserName, "%" + s + "%"))
                || (a.Route    != null && EF.Functions.ILike(a.Route,    "%" + s + "%")));
        }

        var rows = await q.OrderByDescending(a => a.CreatedAt).Take(50000).ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine("Zeit;User;Rolle;Aktion;Entitaet;ID;Route;IP;Aenderungen");
        foreach (var r in rows)
        {
            string esc(string? s) => s == null ? "" : '"' + s.Replace("\"", "\"\"") + '"';
            sb.Append(r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")).Append(';');
            sb.Append(esc(r.UserName ?? (r.UserId?.ToString() ?? ""))).Append(';');
            sb.Append(esc(r.UserRole)).Append(';');
            sb.Append(esc(r.Action)).Append(';');
            sb.Append(esc(r.EntityType)).Append(';');
            sb.Append(esc(r.EntityId)).Append(';');
            sb.Append(esc(r.Route)).Append(';');
            sb.Append(esc(r.IpAddress)).Append(';');
            sb.Append(esc(r.ChangesJson)).AppendLine();
        }
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var name = $"audit-log_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";
        return File(bytes, "text/csv; charset=utf-8", name);
    }
}

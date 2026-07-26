using HrSystem.Data;
using HrSystem.Models;
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

    private async Task<List<AuditRowDto>> EnrichEmployeesAsync(List<AuditRowDto> rows)
    {
        var empIds = new List<int>();
        foreach (var r in rows)
        {
            if (r.EntityType == "Employee" && int.TryParse(r.EntityId, out var id))
                empIds.Add(id);
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
            if (r.EntityType == "Employee" && int.TryParse(r.EntityId, out var id)
                && map.TryGetValue(id, out var info))
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

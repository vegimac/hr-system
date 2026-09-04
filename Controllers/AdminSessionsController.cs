using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Aktive Sitzungen (Walter 04.09.2026): System › Aktive Sitzungen — wer ist
/// gerade angemeldet (laut SessionRegistry), und «Abmelden» pro Benutzer.
///
///   GET  /api/admin/sessions                → Liste
///   POST /api/admin/sessions/{userId}/logout → Sperrvermerk setzen (alle
///        Tokens dieses Benutzers mit login_at vor jetzt sind ungültig)
///
/// Nur Admin. Der Sperrvermerk landet in app_user.session_revoked_before
/// (überlebt Neustarts) und wird in Program.cs OnTokenValidated geprüft.
/// </summary>
[ApiController]
[Route("api/admin/sessions")]
[Authorize(Roles = "admin")]
public class AdminSessionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SessionRegistry _reg;

    public AdminSessionsController(AppDbContext db, SessionRegistry reg)
    {
        _db = db;
        _reg = reg;
    }

    [HttpGet]
    public async Task<IActionResult> Liste()
    {
        var eintraege = _reg.Alle();
        var ids = eintraege.Select(e => e.UserId)
                           .Concat(eintraege.Where(e => e.ImpersonatedBy != null).Select(e => e.ImpersonatedBy!.Value))
                           .Distinct().ToList();
        var users = await _db.AppUsers.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Username, u.FirstName, u.LastName, u.Role, u.IdleTimeoutMinutes })
            .ToDictionaryAsync(u => u.Id);

        string Name(int id) => users.TryGetValue(id, out var u)
            ? (string.Join(" ", new[] { u.FirstName, u.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } n ? n : u.Username)
            : $"#{id}";

        var meId = SessionRegistry.UserIdAus(User);
        var meLogin = SessionRegistry.LoginAtAus(User);
        var now = DateTime.UtcNow;

        var liste = eintraege.Select(e =>
        {
            users.TryGetValue(e.UserId, out var u);
            var letzteAktion = e.LastActivity > e.LastSeen ? e.LastActivity!.Value : e.LastSeen;
            var hb = e.LastHeartbeat ?? e.LastSeen;
            // online = Heartbeat/Zugriff in den letzten 2 Minuten; sonst
            // «gesperrt/inaktiv» (Sperrbildschirm, Tab geschlossen, Rechner zu).
            var online = now - hb <= TimeSpan.FromMinutes(2);
            return new
            {
                userId = e.UserId,
                username = e.Username,
                name = Name(e.UserId),
                role = u?.Role ?? e.Role,
                loginAt = e.LoginAt,
                lastSeen = e.LastSeen,
                lastActivity = letzteAktion,
                lastHeartbeat = e.LastHeartbeat,
                online,
                ip = e.Ip,
                userAgent = e.UserAgent,
                lastPath = e.LastPath,
                impersonatedBy = e.ImpersonatedBy,
                impersonatedByName = e.ImpersonatedBy != null ? Name(e.ImpersonatedBy.Value) : null,
                idleTimeoutMinutes = u?.IdleTimeoutMinutes,
                istEigene = meId == e.UserId && meLogin == e.LoginAt
            };
        }).ToList();

        return Ok(new { serverZeit = now, sitzungen = liste });
    }

    [HttpPost("{userId:int}/logout")]
    public async Task<IActionResult> Abmelden(int userId)
    {
        var user = await _db.AppUsers.FindAsync(userId);
        if (user == null) return NotFound(new { message = "Benutzer nicht gefunden." });
        var bis = DateTime.UtcNow;
        user.SessionRevokedBefore = bis;
        await _db.SaveChangesAsync();
        _reg.Sperren(userId, bis);
        var selbst = SessionRegistry.UserIdAus(User) == userId;
        return Ok(new { message = $"{user.Username} wurde abgemeldet.", selbst });
    }
}

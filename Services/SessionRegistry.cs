using System.Collections.Concurrent;
using System.Security.Claims;
using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Aktive Sitzungen (Walter 04.09.2026): Das JWT ist zustandslos — der Server
/// führt keine Sitzungsliste. Diese Registry merkt sich pro Token (Benutzer +
/// login_at) den letzten Zugriff, damit der Admin unter System › Aktive
/// Sitzungen sieht, wer angemeldet ist, und einzelne Benutzer abmelden kann.
///
/// Abmelden durch den Admin = app_user.session_revoked_before := jetzt.
/// Jedes Token, dessen login_at davor liegt, wird beim nächsten Zugriff mit
/// 401 abgewiesen (OnTokenValidated in Program.cs). Die Verlängerung
/// (POST /api/auth/refresh) behält login_at bei — ein gesperrtes Token kann
/// sich also nicht «frisch verlängern». Nach einem Sperrvermerk hilft nur
/// eine neue Anmeldung (login_at neu → nach dem Sperrzeitpunkt).
///
/// Singleton, rein im Speicher (eine App-Instanz). Nach einem Neustart ist
/// die Liste leer und füllt sich mit dem nächsten Zugriff jedes Benutzers.
/// </summary>
public sealed class SessionRegistry
{
    public sealed class Eintrag
    {
        public int UserId { get; set; }
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
        public DateTime LoginAt { get; set; }
        /// <summary>Letzter «echter» API-Zugriff (ohne Heartbeat).</summary>
        public DateTime LastSeen { get; set; }
        /// <summary>Letzter Heartbeat des Wächters (Browser offen, nicht gesperrt).</summary>
        public DateTime? LastHeartbeat { get; set; }
        /// <summary>Letzte Tastatur-/Mausaktivität laut Heartbeat.</summary>
        public DateTime? LastActivity { get; set; }
        public string? Ip { get; set; }
        public string? UserAgent { get; set; }
        public string? LastPath { get; set; }
        public int? ImpersonatedBy { get; set; }
    }

    private readonly ConcurrentDictionary<string, Eintrag> _sessions = new();
    private readonly ConcurrentDictionary<int, DateTime> _revoked = new();
    private volatile bool _revokedLoaded;
    private readonly object _loadLock = new();

    /// <summary>Älter als die harte Obergrenze (14 h) + Reserve → raus aus der Liste.</summary>
    private static readonly TimeSpan MaxAlter = TimeSpan.FromHours(15);

    private static string Key(int userId, DateTime loginAt) => $"{userId}|{loginAt:O}";

    public static DateTime? LoginAtAus(ClaimsPrincipal user)
    {
        var s = user.FindFirst("login_at")?.Value ?? user.FindFirst("session_started_at")?.Value;
        if (string.IsNullOrEmpty(s)) return null;
        return DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)
            ? d.ToUniversalTime() : null;
    }

    public static int? UserIdAus(ClaimsPrincipal user)
        => int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    /// <summary>Zugriff registrieren. heartbeat=true → nur Heartbeat-Zeit setzen.</summary>
    public void Touch(ClaimsPrincipal user, HttpContext ctx, bool heartbeat, bool aktiv)
    {
        var uid = UserIdAus(user);
        var loginAt = LoginAtAus(user);
        if (uid == null || loginAt == null) return;
        var now = DateTime.UtcNow;
        var e = _sessions.GetOrAdd(Key(uid.Value, loginAt.Value), _ => new Eintrag
        {
            UserId = uid.Value,
            LoginAt = loginAt.Value,
            LastSeen = now
        });
        e.Username = user.FindFirst(ClaimTypes.Name)?.Value ?? e.Username;
        e.Role = user.FindFirst(ClaimTypes.Role)?.Value ?? e.Role;
        var imp = user.Claims.FirstOrDefault(c => c.Type == "impersonated_by" || c.Type.EndsWith("impersonated_by"));
        e.ImpersonatedBy = imp != null && int.TryParse(imp.Value, out var ib) ? ib : null;
        e.Ip = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
               ?? ctx.Connection.RemoteIpAddress?.ToString();
        e.UserAgent = ctx.Request.Headers.UserAgent.FirstOrDefault();
        if (heartbeat)
        {
            e.LastHeartbeat = now;
            if (aktiv) e.LastActivity = now;
        }
        else
        {
            e.LastSeen = now;
            e.LastPath = ctx.Request.Path.Value;
        }
        Aufraeumen(now);
    }

    private void Aufraeumen(DateTime now)
    {
        foreach (var kv in _sessions)
        {
            var letzte = kv.Value.LastHeartbeat > kv.Value.LastSeen ? kv.Value.LastHeartbeat.Value : kv.Value.LastSeen;
            if (now - letzte > MaxAlter) _sessions.TryRemove(kv.Key, out _);
        }
    }

    public IReadOnlyList<Eintrag> Alle()
        => _sessions.Values.OrderByDescending(e => e.LastHeartbeat ?? e.LastSeen).ToList();

    /// <summary>Eigene Sitzung austragen (Abmelden-Button).</summary>
    public void Entfernen(ClaimsPrincipal user)
    {
        var uid = UserIdAus(user);
        var loginAt = LoginAtAus(user);
        if (uid != null && loginAt != null) _sessions.TryRemove(Key(uid.Value, loginAt.Value), out _);
    }

    /// <summary>Alle Sitzungen eines Benutzers aus der Liste nehmen.</summary>
    public void EntfernenBenutzer(int userId)
    {
        foreach (var kv in _sessions)
            if (kv.Value.UserId == userId) _sessions.TryRemove(kv.Key, out _);
    }

    // ── Sperrvermerke (session_revoked_before) ────────────────────────────

    private void RevokedLaden(AppDbContext db)
    {
        if (_revokedLoaded) return;
        lock (_loadLock)
        {
            if (_revokedLoaded) return;
            foreach (var row in db.AppUsers.AsNoTracking()
                         .Where(u => u.SessionRevokedBefore != null)
                         .Select(u => new { u.Id, u.SessionRevokedBefore }))
                _revoked[row.Id] = DateTime.SpecifyKind(row.SessionRevokedBefore!.Value, DateTimeKind.Utc);
            _revokedLoaded = true;
        }
    }

    /// <summary>true = Token ist durch einen Admin-Abmeldevermerk ungültig.</summary>
    public bool IstGesperrt(ClaimsPrincipal user, AppDbContext db)
    {
        var uid = UserIdAus(user);
        var loginAt = LoginAtAus(user);
        if (uid == null || loginAt == null) return false;
        RevokedLaden(db);
        return _revoked.TryGetValue(uid.Value, out var bis) && loginAt.Value < bis;
    }

    public void Sperren(int userId, DateTime bis)
    {
        _revoked[userId] = bis;
        EntfernenBenutzer(userId);
    }
}

using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;

    // ── Lockout-Parameter ─────────────────────────────────────────────────
    // Nach 5 aufeinanderfolgenden Fehlversuchen wird der Account für 15 Min
    // gesperrt. Wirkt für ALLE Rollen (auch Admin), schützt vor Brute-Force.
    private const int    LOCKOUT_THRESHOLD    = 5;
    private const int    LOCKOUT_MINUTES      = 15;
    // Walter-Vorgabe 13.06.2026: kurzlebige JWTs für echte Produktivdaten.
    //   • Backoffice (admin/superuser/user/buchhaltung) → 8 h = 1 Arbeitstag
    //     (morgens einloggen reicht). Vorher 30 Tage — bei Token-Diebstahl
    //     hätte ein Angreifer einen Monat Zugang gehabt.
    //   • Mitarbeiter-Postfach (Rolle 'employee')         → 4 h. Sessions
    //     sind kurz (Lohnzettel anschauen, Passwort wechseln), Risiko bei
    //     Token-Diebstahl höher (Handy verloren).
    private const int    JWT_HOURS_BACKOFFICE = 8;
    private const int    JWT_HOURS_EMPLOYEE   = 4;

    public AuthController(AppDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    // Login per Email (Backoffice-User) oder Username (= EmployeeNumber für
    // MA-Postfach-Accounts). Frontend kann beides ans gleiche Feld senden.
    public record LoginRequest(string Email, string Password);

    [AllowAnonymous]   // Token-Ausgabe (HR-User + MA-Postfach-Login) — muss ohne Login erreichbar sein
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var input = (req?.Email ?? "").Trim();
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(req?.Password))
            return Unauthorized(new { message = "E-Mail/Benutzername und Passwort sind Pflicht." });

        // Login akzeptiert sowohl E-Mail als auch Username — bei MA ist's
        // typisch die EmployeeNumber, die auch in app_user.username steht.
        var lookup = input.ToLower();
        var user = await _context.AppUsers
            .Include(u => u.BranchAccess)
                .ThenInclude(ba => ba.CompanyProfile)
            .Include(u => u.Employee)
            .FirstOrDefaultAsync(u =>
                u.IsActive &&
                (u.Email.ToLower() == lookup || u.Username.ToLower() == lookup));

        // Bewusst gleiche Antwort bei "User unbekannt" und "Passwort falsch"
        // (Username-Enumeration vermeiden) — aber mit nuancierter Behandlung
        // bei lockout, damit der User weiss warum er nicht reinkommt.
        if (user == null)
            return Unauthorized(new { message = "E-Mail/Benutzername oder Passwort falsch." });

        // Lockout-Check
        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            var remaining = (int)Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            return Unauthorized(new { message = $"Account ist gesperrt. Bitte in {remaining} Minute(n) erneut versuchen." });
        }

        // Passwort prüfen
        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        {
            user.FailedLoginCount += 1;
            if (user.FailedLoginCount >= LOCKOUT_THRESHOLD)
            {
                user.LockedUntil = DateTime.UtcNow.AddMinutes(LOCKOUT_MINUTES);
                await _context.SaveChangesAsync();
                return Unauthorized(new { message = $"Zu viele Fehlversuche — Account für {LOCKOUT_MINUTES} Minuten gesperrt." });
            }
            await _context.SaveChangesAsync();
            return Unauthorized(new { message = "E-Mail/Benutzername oder Passwort falsch." });
        }

        // MA-Postfach-Login: zusätzlich prüfen, ob der zugehörige MA noch
        // aktiv ist (kein Austritt). Bei inaktivem MA wird der Account
        // automatisch gesperrt — Walter's Anforderung "MA inaktiv → Zugang
        // gesperrt, Postfach bleibt 1 Jahr für HR/Admin einsehbar".
        if (user.Role == "employee" && user.EmployeeId.HasValue)
        {
            var emp = user.Employee;
            if (emp == null || !emp.IsActive)
            {
                user.IsActive = false;
                await _context.SaveChangesAsync();
                return Unauthorized(new { message = "Postfach-Zugang gesperrt — Mitarbeiterverhältnis nicht (mehr) aktiv." });
            }
        }

        // Erfolgreich → Lockout-Counter zurücksetzen, Letzten Login speichern
        user.FailedLoginCount = 0;
        user.LockedUntil      = null;
        user.LastLoginAt      = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var sessionStart = DateTime.UtcNow;
        var token = GenerateToken(user, sessionStart);

        return Ok(new
        {
            token,
            mustChangePassword = user.MustChangePassword,
            // Session-Policy (Walter-Vorgabe 21.06.2026) — der Frontend-Wächter
            // startet damit sofort, ohne auf /me warten zu müssen.
            sessionStartedAt   = sessionStart.ToString("o"),
            idleTimeoutMinutes = EffectiveIdleTimeout(user),
            maxSessionMinutes  = EffectiveMaxSession(user),
            user = new
            {
                user.Id,
                user.Username,
                firstName = user.FirstName,   // persoenliche Anrede (To-do-Anleitung)
                user.Email,
                user.Role,
                user.Theme,
                preferredLanguage = user.PreferredLanguage,
                startPage = user.StartPage,
                employeeId = user.EmployeeId,
                isHrTeam   = user.IsHrTeam,
                // Zugriff Filial-Dokumente (Walter 06.08.2026) — Frontend
                // blendet damit den Tab «Dokumente» im Filial-Detail ein/aus.
                canCompanyDokumente = user.CanCompanyDokumente,
                // Manager-Dienstplan planbar in mind. einer Filiale (Walter 08.08.2026).
                canDienstplan = user.BranchAccess.Any(b => b.CanDienstplan),
                canVertragSms = user.BranchAccess.Any(b => b.CanVertragSms),
                isSuperAdmin = user.IsSuperAdmin,
                // Sichtbare Bereiche (Walter 28.06.2026): NULL = Rollen-Default.
                allowedAreas = user.AllowedAreas == null
                    ? null
                    : user.AllowedAreas.Split(',', StringSplitOptions.RemoveEmptyEntries),
                // Admin + lowuser → "all" (Walter 14.06.2026: lowuser im
                // Filial-Selektor wie superuser, Einschränkungen sind ÜBER
                // den Menü-Umfang, nicht über Filialen). Andere → eigene
                // UserBranchAccess. MA-Postfach: keine Branches im klassischen
                // Sinn — sieht nur eigenes Postfach (im Frontend gefiltert).
                branches = user.Role == "admin" || user.Role == "lowuser"
                    ? (object)"all"
                    : user.BranchAccess.Select(ba => new
                    {
                        id = ba.CompanyProfileId,
                        name = ba.CompanyProfile.BranchName ?? ba.CompanyProfile.CompanyName,
                        code = ba.CompanyProfile.RestaurantCode
                    })
            }
        });
    }

    [HttpGet("me")]
    // Walter 14.06.2026: lowuser ergänzt — sonst kriegt der eingeschränkte
    // Benutzer beim Login 403 auf /me, das Frontend hat kein currentUser.branches
    // und der Filial-Selektor bleibt leer.
    [Authorize(Roles = "admin,superuser,user,employee,buchhaltung,lowuser")]
    public async Task<IActionResult> Me()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.AppUsers
            .Include(u => u.BranchAccess)
            .ThenInclude(ba => ba.CompanyProfile)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return NotFound();

        // Session-Policy aus den Token-Claims (am Login fixiert → bleibt über
        // Seiten-Reload konsistent). Fallback auf die effektiven DB-Werte.
        var idleClaim    = int.TryParse(User.FindFirst("idle_timeout_minutes")?.Value, out var ic) ? ic : EffectiveIdleTimeout(user);
        var maxClaim     = int.TryParse(User.FindFirst("max_session_minutes")?.Value, out var mc) ? mc : EffectiveMaxSession(user);
        var sessionStart = User.FindFirst("session_started_at")?.Value;

        // Testmodus: wer hat den Wechsel ausgeloest? (Claim aus dem JWT, kein
        // DB-Feld). Nur fuer die Anzeige im Balken und die «Zurueck»-Aktion.
        object? impersonatedByInfo = null;
        var impBy = User.FindFirst("impersonated_by")?.Value;
        if (int.TryParse(impBy, out var impById))
        {
            var caller = await _context.AppUsers.AsNoTracking()
                .Where(u => u.Id == impById)
                .Select(u => new { u.Id, u.Username })
                .FirstOrDefaultAsync();
            impersonatedByInfo = caller ?? (object)new { Id = impById, Username = (string?)null };
        }

        return Ok(new
        {
            user.Id,
            user.Username,
            firstName = user.FirstName,   // persoenliche Anrede (To-do-Anleitung)
            // Walter-Bug 31.08.2026: Der Testmodus-Balken kam bisher allein aus
            // dem localStorage des Browsers. Lief das Testmodus-Token ab und
            // meldete sich der Admin neu an, blieb der Balken stehen — die App
            // arbeitete aber wieder mit dem Admin-Konto. Man testete Rechte und
            // sah die eigenen. Deshalb sagt jetzt der SERVER, ob das benutzte
            // Token ein Testmodus-Token ist (Claim impersonated_by).
            impersonating = User.FindFirst("impersonated_by") != null,
            impersonatedBy = impersonatedByInfo,
            user.Email,
            user.Role,
            user.Theme,
            preferredLanguage  = user.PreferredLanguage,
            startPage          = user.StartPage,
            user.FirstName,
            user.LastName,
            employeeId         = user.EmployeeId,
            mustChangePassword = user.MustChangePassword,
            isHrTeam           = user.IsHrTeam,
            // Zugriff Filial-Dokumente (Walter 06.08.2026) — Frontend blendet
            // damit den Tab «Dokumente» im Filial-Detail ein/aus.
            canCompanyDokumente = user.CanCompanyDokumente,
            canDienstplan      = user.BranchAccess.Any(b => b.CanDienstplan),
            canVertragSms      = user.BranchAccess.Any(b => b.CanVertragSms),
            isSuperAdmin       = user.IsSuperAdmin,
            // Sichtbare Bereiche (Walter 28.06.2026): NULL = Rollen-Default.
            allowedAreas = user.AllowedAreas == null
                ? null
                : user.AllowedAreas.Split(',', StringSplitOptions.RemoveEmptyEntries),
            // Session-Policy für den Frontend-Wächter.
            sessionStartedAt   = sessionStart,
            idleTimeoutMinutes = idleClaim,
            maxSessionMinutes  = maxClaim,
            // Walter-Vorgabe 14.06.2026: lowuser im Filial-Selektor 1:1 wie
            // superuser — sieht „Alle Filialen" plus jede einzelne. Die
            // Einschränkungen wirken NUR auf den Menü-Umfang (Dashboard +
            // Mitarbeiter + Verträge), nicht auf den Filial-Selektor.
            branches = user.Role == "admin" || user.Role == "superuser" || user.Role == "lowuser"
                ? (object)"all"
                : user.BranchAccess.Select(ba => new
                {
                    id = ba.CompanyProfileId,
                    name = ba.CompanyProfile.BranchName ?? ba.CompanyProfile.CompanyName,
                    code = ba.CompanyProfile.RestaurantCode
                })
        });
    }

    public record UpdateThemeRequest(string Theme);

    /// <summary>Theme-Präferenz des eingeloggten Users speichern (light/dark).</summary>
    [HttpPut("theme")]
    [Authorize(Roles = "admin,superuser,user,employee,buchhaltung,lowuser")]
    public async Task<IActionResult> UpdateTheme([FromBody] UpdateThemeRequest req)
    {
        var theme = (req?.Theme ?? "").ToLowerInvariant();
        if (theme != "light" && theme != "dark")
            return BadRequest(new { message = "Theme muss 'light' oder 'dark' sein." });

        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.AppUsers.FindAsync(userId);
        if (user == null) return NotFound();
        user.Theme = theme;
        await _context.SaveChangesAsync();
        return Ok(new { theme });
    }

    public record UpdateLanguageRequest(string Language);

    /// <summary>
    /// Bevorzugte UI-Sprache speichern (de/en). Wird vom Flag-Toggle in der
    /// Top-Bar aufgerufen, wenn der User die Wahl persistieren möchte.
    /// </summary>
    [HttpPut("language")]
    [Authorize(Roles = "admin,superuser,user,employee,buchhaltung,lowuser")]
    public async Task<IActionResult> UpdateLanguage([FromBody] UpdateLanguageRequest req)
    {
        var lang = (req?.Language ?? "").ToLowerInvariant();
        if (lang != "de" && lang != "en")
            return BadRequest(new { message = "Sprache muss 'de' oder 'en' sein." });

        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.AppUsers.FindAsync(userId);
        if (user == null) return NotFound();
        user.PreferredLanguage = lang;
        await _context.SaveChangesAsync();
        return Ok(new { language = lang });
    }

    public record UpdateStartPageRequest(string? StartPage);

    /// <summary>
    /// Persönliche Startseite (Walter 14.08.2026) — analog language.
    /// Leer/NULL = Dashboard (Default).
    /// </summary>
    [HttpPut("start-page")]
    [Authorize(Roles = "admin,superuser,user,buchhaltung,lowuser")]
    public async Task<IActionResult> UpdateStartPage([FromBody] UpdateStartPageRequest req)
    {
        // Alle Sidebar-Hauptseiten (Walter 14.08.2026: «auf alle seiten»).
        var erlaubt = new[]
        {
            "dashboard", "todos", "mitarbeiter", "moments", "posteingang",
            "auswertungen", "manager-dienstplan", "roster-absence-import",
            "lohn", "hr-hub", "fibu", "admin-hub",
        };
        var sp = (req?.StartPage ?? "").Trim().ToLowerInvariant();
        if (sp.Length > 0 && !erlaubt.Contains(sp))
            return BadRequest(new { message = "Ungültige Startseite." });

        var uid = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.AppUsers.FindAsync(uid);
        if (user == null) return NotFound();
        user.StartPage = sp.Length > 0 ? sp : null;
        await _context.SaveChangesAsync();
        return Ok(new { startPage = user.StartPage });
    }

    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    /// <summary>
    /// Eingeloggter User wechselt sein Passwort. Wird auch beim Pflicht-
    /// Wechsel nach Initial-Passwort / Admin-Reset verwendet (then:
    /// MustChangePassword wird auf false gesetzt). Mindestlänge 8 Zeichen.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize(Roles = "admin,superuser,user,employee,buchhaltung,lowuser")]   // MA + Buchhaltung müssen Passwort wechseln können
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
            return BadRequest(new { message = "Aktuelles und neues Passwort sind Pflicht." });
        if (req.NewPassword.Length < 8)
            return BadRequest(new { message = "Neues Passwort muss mindestens 8 Zeichen lang sein." });
        if (req.CurrentPassword == req.NewPassword)
            return BadRequest(new { message = "Neues Passwort muss sich vom aktuellen unterscheiden." });

        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.AppUsers.FindAsync(userId);
        if (user == null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
            return Unauthorized(new { message = "Aktuelles Passwort ist falsch." });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        user.MustChangePassword = false;
        user.FailedLoginCount   = 0;
        user.LockedUntil        = null;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Passwort wurde geändert." });
    }

    // ── Impersonation / „View-as" (Walter-Vorgabe 28.06.2026) ─────────────
    // Der Superadmin gibt einen Ziel-Benutzernamen + SEIN EIGENES Passwort
    // ein und erhält ein Token für den Ziel-User — um zu testen, was jeder
    // Benutzer im Programm sieht. KEIN fremdes Passwort nötig. Nur IsSuperAdmin.
    public record ImpersonateRequest(string TargetUsername, string Password);

    [HttpPost("impersonate")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Impersonate([FromBody] ImpersonateRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.TargetUsername) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "Benutzername und dein Passwort sind Pflicht." });

        var callerId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var caller = await _context.AppUsers.FindAsync(callerId);
        if (caller == null) return Unauthorized();

        // Nur der Superadmin darf in andere Benutzer wechseln.
        if (!caller.IsSuperAdmin)
            return StatusCode(403, new { message = "Nur der Superadmin darf in andere Benutzer wechseln." });

        // Eigenes Passwort prüfen — BEWUSST ohne Lockout-Nebenwirkung auf den
        // Admin (kein FailedLoginCount), damit man sich beim Testen nicht aussperrt.
        if (!BCrypt.Net.BCrypt.Verify(req.Password, caller.PasswordHash))
            return Unauthorized(new { message = "Dein Passwort ist falsch." });

        var lookup = req.TargetUsername.Trim().ToLower();
        var target = await _context.AppUsers
            .Include(u => u.BranchAccess)
                .ThenInclude(ba => ba.CompanyProfile)
            .Include(u => u.Employee)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == lookup || u.Email.ToLower() == lookup);

        if (target == null)
            return NotFound(new { message = $"Kein Benutzer mit Benutzername/E-Mail '{req.TargetUsername}' gefunden." });
        if (!target.IsActive)
            return BadRequest(new { message = "Ziel-Benutzer ist deaktiviert." });

        var sessionStart = DateTime.UtcNow;
        var token = GenerateToken(target, sessionStart, impersonatedBy: caller.Id);

        return Ok(new
        {
            token,
            impersonating  = true,
            impersonatedBy = new { caller.Id, caller.Username },
            sessionStartedAt   = sessionStart.ToString("o"),
            idleTimeoutMinutes = EffectiveIdleTimeout(target),
            maxSessionMinutes  = EffectiveMaxSession(target),
            user = new
            {
                target.Id,
                target.Username,
                firstName = target.FirstName,
                target.Email,
                target.Role,
                target.Theme,
                preferredLanguage = target.PreferredLanguage,
                startPage = target.StartPage,
                employeeId        = target.EmployeeId,
                isHrTeam          = target.IsHrTeam,
                canCompanyDokumente = target.CanCompanyDokumente,
                isSuperAdmin      = target.IsSuperAdmin,
                branches = target.Role == "admin" || target.Role == "lowuser"
                    ? (object)"all"
                    : target.BranchAccess.Select(ba => new
                    {
                        id   = ba.CompanyProfileId,
                        name = ba.CompanyProfile.BranchName ?? ba.CompanyProfile.CompanyName,
                        code = ba.CompanyProfile.RestaurantCode
                    })
            }
        });
    }

    // ── Benutzerbezogene Session-/Logout-Policy (Walter-Vorgabe 21.06.2026) ──
    // Effektiver Wert = User-Wert ODER Rollen-Default. Defaults:
    //   employee → idle 15 / max 30 ;  alle übrigen → idle 30 / max 480.
    public const int POLICY_MIN = 5;
    public const int POLICY_MAX = 1440;

    public static int EffectiveIdleTimeout(AppUser u) =>
        Clamp(u.IdleTimeoutMinutes ?? (u.Role == "employee" ? 15 : 30));
    public static int EffectiveMaxSession(AppUser u) =>
        Clamp(u.MaxSessionMinutes ?? (u.Role == "employee" ? 30 : 480));
    private static int Clamp(int v) => Math.Max(POLICY_MIN, Math.Min(POLICY_MAX, v));

    private string GenerateToken(AppUser user, DateTime sessionStart, int? impersonatedBy = null)
    {
        // Walter-Vorgabe 13.06.2026: KEIN hardgecodeter Fallback.
        var secret = _config["Jwt:Secret"]
            ?? Environment.GetEnvironmentVariable("JWT_SECRET")
            ?? throw new InvalidOperationException(
                "JWT-Secret nicht konfiguriert (Jwt:Secret oder ENV JWT_SECRET).");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var idle = EffectiveIdleTimeout(user);
        var max  = EffectiveMaxSession(user);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            // Session-Policy-Claims für den Frontend-Wächter.
            new Claim("session_started_at",    sessionStart.ToString("o")),
            new Claim("idle_timeout_minutes",  idle.ToString()),
            new Claim("max_session_minutes",   max.ToString())
        };
        // Buchhaltung = wie Superuser (volle HR-Feature-Rechte) PLUS Fibu-Bereich.
        // Zweiter Rollen-Claim 'superuser' → alle [Authorize(Roles="admin,superuser")]-
        // Endpunkte greifen, ohne jedes Attribut anzufassen. Der eigene
        // 'buchhaltung'-Claim bleibt (erster Claim) und schaltet zusätzlich die
        // Fibu-Endpunkte frei + dient der branch-genauen Zugriffsprüfung.
        if (user.Role == "buchhaltung")
            claims.Add(new Claim(ClaimTypes.Role, "superuser"));

        // Impersonation (Walter-Vorgabe 28.06.2026): wenn ein Superadmin in
        // diesen User „hineinwechselt", merkt sich der Token wer impersoniert
        // (Audit-Spur). Aktionen laufen ansonsten als der Ziel-User.
        if (impersonatedBy.HasValue)
            claims.Add(new Claim("impersonated_by", impersonatedBy.Value.ToString()));

        // JWT-Ablauf = maximale Session-Dauer (Walter-Vorgabe 21.06.2026):
        // läuft der Token ab, MUSS neu eingeloggt werden — der Server erzwingt
        // die Max-Session hart, unabhängig vom Frontend-Wächter.
        var expires = sessionStart.AddMinutes(max);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace HrSystem.Controllers;

/// <summary>
/// WebAuthn / Passkeys fürs MA-Postfach-Login (Face ID / Touch ID / Fingerprint),
/// Walter 01.07.2026. Registrierung ist eingeloggt (Passwort), Login läuft anonym
/// über den signierten Assertion-Nachweis. Passwort bleibt immer als Rückfall.
///
/// Es werden NUR öffentliche Schlüssel gespeichert — keine biometrischen Daten.
/// </summary>
[ApiController]
[Route("api/webauthn")]
public class WebAuthnController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFido2 _fido2;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;

    public WebAuthnController(AppDbContext db, IFido2 fido2, IMemoryCache cache, IConfiguration config)
    {
        _db = db; _fido2 = fido2; _cache = cache; _config = config;
    }

    private int? UserId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    private static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(5);

    // ─────────────────────────── Registrierung ───────────────────────────
    // Der User ist bereits per Passwort eingeloggt und aktiviert Face ID auf DIESEM Gerät.

    [HttpPost("register/begin")]
    [Authorize(Roles = "admin,superuser,user,employee,buchhaltung")]
    public async Task<IActionResult> RegisterBegin()
    {
        var uid = UserId();
        if (uid == null) return Unauthorized();
        var user = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid.Value);
        if (user == null) return Unauthorized();

        var fidoUser = new Fido2User
        {
            Id = Encoding.UTF8.GetBytes(user.Id.ToString()),
            Name = user.Username,
            DisplayName = ($"{user.FirstName} {user.LastName}").Trim() is { Length: > 0 } dn ? dn : user.Username,
        };

        // Bereits registrierte Credentials dieses Users ausschliessen (kein Doppel).
        var existing = await _db.WebAuthnCredentials.AsNoTracking()
            .Where(c => c.AppUserId == user.Id)
            .Select(c => c.CredentialId)
            .ToListAsync();
        var excludeCredentials = existing
            .Select(id => new PublicKeyCredentialDescriptor(id))
            .ToList();

        var authenticatorSelection = new AuthenticatorSelection
        {
            RequireResidentKey = true,   // discoverable → Login ohne Benutzernamen (Fido2 v3-API)
            UserVerification = UserVerificationRequirement.Required,
            AuthenticatorAttachment = AuthenticatorAttachment.Platform, // Face ID/Touch ID des Geräts
        };

        var options = _fido2.RequestNewCredential(
            fidoUser, excludeCredentials, authenticatorSelection, AttestationConveyancePreference.None);

        var optionsJson = options.ToJson();
        var session = Guid.NewGuid().ToString("N");
        _cache.Set("webauthn:reg:" + session, optionsJson, ChallengeTtl);

        // options als Fido2-eigenes JSON (String) zurückgeben → Client JSON.parse.
        return Ok(new { session, options = optionsJson });
    }

    public record RegisterCompleteDto(string Session, AuthenticatorAttestationRawResponse AttestationResponse, string? DeviceLabel);

    [HttpPost("register/complete")]
    [Authorize(Roles = "admin,superuser,user,employee,buchhaltung")]
    public async Task<IActionResult> RegisterComplete([FromBody] RegisterCompleteDto dto)
    {
        var uid = UserId();
        if (uid == null) return Unauthorized();
        if (dto?.AttestationResponse == null || string.IsNullOrWhiteSpace(dto.Session))
            return BadRequest(new { error = "Ungültige Anfrage." });

        if (!_cache.TryGetValue("webauthn:reg:" + dto.Session, out string? optionsJson) || optionsJson == null)
            return BadRequest(new { error = "Die Registrierung ist abgelaufen. Bitte erneut versuchen." });
        _cache.Remove("webauthn:reg:" + dto.Session);

        var options = CredentialCreateOptions.FromJson(optionsJson);

        // Prüfen: dieselbe Credential-ID darf nicht schon existieren.
        async Task<bool> IsUnique(IsCredentialIdUniqueToUserParams args, CancellationToken ct)
            => !await _db.WebAuthnCredentials.AsNoTracking().AnyAsync(c => c.CredentialId == args.CredentialId, ct);

        Fido2.CredentialMakeResult result;
        try
        {
            result = await _fido2.MakeNewCredentialAsync(dto.AttestationResponse, options, IsUnique);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "Registrierung fehlgeschlagen.", detail = ex.Message });
        }

        var cred = new WebAuthnCredential
        {
            AppUserId   = uid.Value,
            CredentialId = result.Result!.CredentialId,
            PublicKey    = result.Result.PublicKey,
            SignCount    = result.Result.Counter,
            UserHandle   = result.Result.User?.Id,
            Aaguid       = result.Result.Aaguid.ToString(),
            DeviceLabel  = string.IsNullOrWhiteSpace(dto.DeviceLabel) ? "Mein Gerät" : dto.DeviceLabel!.Trim(),
            CreatedAt    = DateTime.Now,
        };
        _db.WebAuthnCredentials.Add(cred);
        await _db.SaveChangesAsync();

        return Ok(new { ok = true });
    }

    // ─────────────────────────── Login ───────────────────────────
    // Anonym: der Authenticator weist den Besitz des privaten Schlüssels nach.

    [AllowAnonymous]
    [HttpPost("login/begin")]
    public IActionResult LoginBegin()
    {
        // Usernameless: leere allowCredentials → das Gerät zeigt die passenden Passkeys.
        var options = _fido2.GetAssertionOptions(
            new List<PublicKeyCredentialDescriptor>(), UserVerificationRequirement.Required);

        var optionsJson = options.ToJson();
        var session = Guid.NewGuid().ToString("N");
        _cache.Set("webauthn:login:" + session, optionsJson, ChallengeTtl);

        return Ok(new { session, options = optionsJson });
    }

    public record LoginCompleteDto(string Session, AuthenticatorAssertionRawResponse AssertionResponse);

    [AllowAnonymous]
    [HttpPost("login/complete")]
    public async Task<IActionResult> LoginComplete([FromBody] LoginCompleteDto dto)
    {
        if (dto?.AssertionResponse == null || string.IsNullOrWhiteSpace(dto.Session))
            return BadRequest(new { error = "Ungültige Anfrage." });

        if (!_cache.TryGetValue("webauthn:login:" + dto.Session, out string? optionsJson) || optionsJson == null)
            return BadRequest(new { error = "Die Anmeldung ist abgelaufen. Bitte erneut versuchen." });
        _cache.Remove("webauthn:login:" + dto.Session);

        var options = AssertionOptions.FromJson(optionsJson);

        // Credential über die vom Gerät gelieferte Credential-ID finden.
        var credId = dto.AssertionResponse.Id;
        var cred = await _db.WebAuthnCredentials.FirstOrDefaultAsync(c => c.CredentialId == credId);
        if (cred == null) return Unauthorized(new { error = "Dieser Passkey ist nicht registriert." });

        async Task<bool> IsUserHandleOwner(IsUserHandleOwnerOfCredentialIdParams args, CancellationToken ct)
            => await _db.WebAuthnCredentials.AsNoTracking()
                .AnyAsync(c => c.CredentialId == args.CredentialId && c.UserHandle != null && c.UserHandle == args.UserHandle, ct);

        AssertionVerificationResult res;
        try
        {
            res = await _fido2.MakeAssertionAsync(
                dto.AssertionResponse, options, cred.PublicKey, (uint)cred.SignCount, IsUserHandleOwner);
        }
        catch (Exception ex)
        {
            return Unauthorized(new { error = "Anmeldung fehlgeschlagen.", detail = ex.Message });
        }

        // Zähler + Zeitpunkt aktualisieren.
        cred.SignCount = res.Counter;
        cred.LastUsedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        var user = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == cred.AppUserId);
        if (user == null || !user.IsActive) return Unauthorized(new { error = "Konto nicht aktiv." });

        var sessionStart = DateTime.UtcNow;
        var token = GenerateToken(user, sessionStart);
        return Ok(new
        {
            token,
            user = new { user.Id, user.Username, user.Role, user.FirstName, employeeId = user.EmployeeId },
            mustChangePassword = user.MustChangePassword,
        });
    }

    // ─────────────────────────── Geräteverwaltung ───────────────────────────

    [HttpGet("credentials")]
    [Authorize(Roles = "admin,superuser,user,employee,buchhaltung")]
    public async Task<IActionResult> ListCredentials()
    {
        var uid = UserId();
        if (uid == null) return Unauthorized();
        var list = await _db.WebAuthnCredentials.AsNoTracking()
            .Where(c => c.AppUserId == uid.Value)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Id, c.DeviceLabel, c.CreatedAt, c.LastUsedAt })
            .ToListAsync();
        return Ok(list);
    }

    [HttpDelete("credentials/{id:int}")]
    [Authorize(Roles = "admin,superuser,user,employee,buchhaltung")]
    public async Task<IActionResult> DeleteCredential(int id)
    {
        var uid = UserId();
        if (uid == null) return Unauthorized();
        var cred = await _db.WebAuthnCredentials.FirstOrDefaultAsync(c => c.Id == id && c.AppUserId == uid.Value);
        if (cred == null) return NotFound();
        _db.WebAuthnCredentials.Remove(cred);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ── HR/Admin: alle Passkeys eines MA löschen (z.B. bei Geräteverlust) ──
    [HttpGet("admin/by-employee/{employeeId:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> AdminListByEmployee(int employeeId)
    {
        var count = await _db.WebAuthnCredentials
            .Where(c => c.AppUser != null && c.AppUser.EmployeeId == employeeId)
            .CountAsync();
        return Ok(new { count });
    }

    [HttpDelete("admin/by-employee/{employeeId:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> AdminDeleteByEmployee(int employeeId)
    {
        var creds = await _db.WebAuthnCredentials
            .Where(c => c.AppUser != null && c.AppUser.EmployeeId == employeeId)
            .ToListAsync();
        if (creds.Count > 0)
        {
            _db.WebAuthnCredentials.RemoveRange(creds);
            await _db.SaveChangesAsync();
        }
        return Ok(new { ok = true, removed = creds.Count });
    }

    // ── JWT wie beim Passwort-Login (spiegelt AuthController.GenerateToken) ──
    private string GenerateToken(AppUser user, DateTime sessionStart)
    {
        var secret = _config["Jwt:Secret"]
            ?? Environment.GetEnvironmentVariable("JWT_SECRET")
            ?? throw new InvalidOperationException("JWT-Secret nicht konfiguriert.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var idle = AuthController.EffectiveIdleTimeout(user);
        var max  = AuthController.EffectiveMaxSession(user);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("session_started_at",   sessionStart.ToString("o")),
            new Claim("idle_timeout_minutes", idle.ToString()),
            new Claim("max_session_minutes",  max.ToString()),
        };
        if (user.Role == "buchhaltung")
            claims.Add(new Claim(ClaimTypes.Role, "superuser"));

        var token = new JwtSecurityToken(
            claims: claims,
            expires: sessionStart.AddMinutes(max),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

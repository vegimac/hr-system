using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QRCoder;

namespace HrSystem.Controllers;

/// <summary>
/// Onboarding / Passwort-Reset des MA-Postfachs per Einmal-QR-Link (Walter 01.07.2026).
/// HR erzeugt einen QR-Code; der MA scannt ihn, setzt sein Passwort und wird direkt
/// eingeloggt. Der Token ist einmalig verwendbar und zeitlich begrenzt (72 h). Der
/// Klartext-Token steht nur im Link, in der DB nur der SHA-256-Hash.
///
/// Derselbe Mechanismus kann später auch per SMS ausgeliefert werden (ASPSMS) und
/// so eine echte Selbstbedienung „Passwort vergessen" ermöglichen.
/// </summary>
[ApiController]
[Route("api/postfach-setup")]
public class PostfachSetupController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EmployeePostfachService _postfach;
    private readonly IConfiguration _config;

    public PostfachSetupController(AppDbContext db, EmployeePostfachService postfach, IConfiguration config)
    {
        _db = db; _postfach = postfach; _config = config;
    }

    private int? UserId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    private static (string token, string hash) NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        var token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (token, HashToken(token));
    }
    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private const int ExpiryHours = 72;

    // ── HR: QR-/Link erzeugen ───────────────────────────────────────────
    [HttpPost("create/{employeeId:int}")]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> Create(int employeeId, [FromQuery] string purpose = "onboarding")
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound(new { error = "Mitarbeiter nicht gefunden." });

        // Account sicherstellen (legt an, falls noch keiner existiert).
        var primary = await _postfach.GetPrimaryCompanyAsync(employeeId);
        await _postfach.EnsureAccountAsync(emp, primary);

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.EmployeeId == employeeId);
        if (user == null) return BadRequest(new { error = "Postfach-Account konnte nicht angelegt werden." });

        // Bisher offene (unbenutzte) Tokens dieses Users entwerten — nur der neue QR gilt.
        var old = await _db.PostfachSetupTokens.Where(t => t.AppUserId == user.Id && t.UsedAt == null).ToListAsync();
        if (old.Count > 0) _db.PostfachSetupTokens.RemoveRange(old);

        var (token, hash) = NewToken();
        var expiresAt = DateTime.Now.AddHours(ExpiryHours);
        _db.PostfachSetupTokens.Add(new PostfachSetupToken
        {
            AppUserId = user.Id,
            TokenHash = hash,
            Purpose   = purpose == "reset" ? "reset" : "onboarding",
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.Now,
            CreatedBy = UserId(),
        });
        await _db.SaveChangesAsync();

        // Öffentliche Basis-URL für MA-Links = kanonische SiteUrl (dieselbe Quelle wie
        // die E-Mails, aus smtp_setting), NICHT die Admin-Domain (Request.Host). So zeigt
        // der QR IMMER auf die MA-Domain (onecrew.ch), egal auf welcher Domain HR gerade
        // arbeitet. Fallback: https://onecrew.ch/. Walter-Vorgabe 05.07.2026.
        var siteRow = await _db.SmtpSettings.AsNoTracking().FirstOrDefaultAsync();
        var baseUrl = (siteRow != null && !string.IsNullOrWhiteSpace(siteRow.SiteUrl))
            ? siteRow.SiteUrl.Trim()
            : "https://onecrew.ch/";
        var url = $"{baseUrl.TrimEnd('/')}/setup.html?t={token}";

        // QR-Code serverseitig (dependency-frei).
        using var qrGen = new QRCodeGenerator();
        using var qrData = qrGen.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(qrData).GetGraphic(10);
        var qrPng = "data:image/png;base64," + Convert.ToBase64String(png);

        return Ok(new
        {
            url,
            qrPng,
            expiresAt,
            firstName = emp.FirstName,
            username  = user.Username,
        });
    }

    // ── MA: Token prüfen (Landing zeigt Begrüssung) ─────────────────────
    private async Task<PostfachSetupToken?> FindValidAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = HashToken(token);
        var t = await _db.PostfachSetupTokens.Include(x => x.AppUser)
            .FirstOrDefaultAsync(x => x.TokenHash == hash);
        if (t == null || t.UsedAt != null || t.ExpiresAt < DateTime.Now) return null;
        return t;
    }

    [AllowAnonymous]
    [HttpGet("verify")]
    public async Task<IActionResult> Verify([FromQuery] string token)
    {
        var t = await FindValidAsync(token);
        if (t == null) return StatusCode(410, new { error = "Dieser Link ist ungültig oder abgelaufen." });
        return Ok(new { ok = true, firstName = t.AppUser?.FirstName, username = t.AppUser?.Username });
    }

    public record CompleteDto(string Token, string NewPassword);

    [AllowAnonymous]
    [HttpPost("complete")]
    public async Task<IActionResult> Complete([FromBody] CompleteDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 8)
            return BadRequest(new { error = "Bitte ein Passwort mit mindestens 8 Zeichen setzen." });

        var t = await FindValidAsync(dto.Token);
        if (t == null || t.AppUser == null)
            return StatusCode(410, new { error = "Dieser Link ist ungültig oder abgelaufen." });

        var user = t.AppUser;
        user.PasswordHash       = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.MustChangePassword = false;
        user.FailedLoginCount   = 0;
        user.LockedUntil        = null;
        t.UsedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        // Direkt einloggen.
        var jwt = GenerateToken(user, DateTime.UtcNow);
        return Ok(new
        {
            token = jwt,
            user = new { user.Id, user.Username, user.Role, user.FirstName, employeeId = user.EmployeeId },
            redirect = "postfach.html",
        });
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

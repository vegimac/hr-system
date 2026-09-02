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
    private readonly EmailService _email;

    public PostfachSetupController(AppDbContext db, EmployeePostfachService postfach, IConfiguration config, EmailService email)
    {
        _db = db; _postfach = postfach; _config = config; _email = email;
    }

    /// <summary>
    /// TESTMODUS (Walter-Vorgabe 18.08.2026): App-Link-Mails gehen NICHT an den
    /// MA, sondern umgeleitet an diese Adresse — bis Walter den Versand scharf
    /// schaltet (dann Konstante auf null setzen). Der eigentliche Empfänger wird
    /// im Betreff + Mail-Kopf ausgewiesen.
    /// </summary>
    // TESTMODUS aktiv (Walter 18.08.2026 abends) — Mails umgeleitet an Walter.
    // Zum Scharfschalten auf null setzen.
    private const string? AppLinkTestRedirect = "walter.schaub@gmail.com";

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

    // ── HR: App-Link per E-MAIL an bestehenden MA (Walter-Vorgabe 18.08.2026).
    // Gleiche Token-Mechanik wie der QR, aber 7 Tage gültig und als Mail mit
    // Kurz-Anleitung. Kostenlos (statt SMS); SMS bleibt als Kanal-Reserve.
    public record AppLinkDto(string? DokumentWunsch);

    [HttpPost("send-app-link/{employeeId:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> SendAppLink(int employeeId, [FromBody] AppLinkDto? dto = null)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound(new { error = "Mitarbeiter nicht gefunden." });
        if (string.IsNullOrWhiteSpace(emp.Email))
            return BadRequest(new { error = "Beim MA ist keine E-Mail-Adresse hinterlegt." });

        var primary = await _postfach.GetPrimaryCompanyAsync(employeeId);
        await _postfach.EnsureAccountAsync(emp, primary);
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.EmployeeId == employeeId);
        if (user == null) return BadRequest(new { error = "Postfach-Account konnte nicht angelegt werden." });

        var old = await _db.PostfachSetupTokens.Where(t => t.AppUserId == user.Id && t.UsedAt == null).ToListAsync();
        if (old.Count > 0) _db.PostfachSetupTokens.RemoveRange(old);

        var (token, hash) = NewToken();
        var expiresAt = DateTime.Now.AddDays(7);
        _db.PostfachSetupTokens.Add(new PostfachSetupToken
        {
            AppUserId = user.Id,
            TokenHash = hash,
            Purpose   = "onboarding",
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.Now,
            CreatedBy = UserId(),
        });
        await _db.SaveChangesAsync();

        var siteRow = await _db.SmtpSettings.AsNoTracking().FirstOrDefaultAsync();
        var baseUrl = (siteRow != null && !string.IsNullOrWhiteSpace(siteRow.SiteUrl))
            ? siteRow.SiteUrl.Trim() : "https://onecrew.ch/";
        var url = $"{baseUrl.TrimEnd('/')}/setup.html?t={token}";
        var anleitungUrl = $"{baseUrl.TrimEnd('/')}/anleitung.html";

        var to = AppLinkTestRedirect ?? emp.Email!.Trim();
        var redirected = AppLinkTestRedirect != null;
        var subject = redirected
            ? $"[TEST — eigentlich an {emp.Email}] Dein OneCrew-Postfach"
            : "Dein OneCrew-Postfach";
        var bisTxt = expiresAt.ToString("dd.MM.yyyy");

        var wunsch = dto?.DokumentWunsch?.Trim();
        var wunschHtml = string.IsNullOrWhiteSpace(wunsch) ? "" :
            $"<div style='background:#ffffff;border-left:5px solid #1a1a1a;border-radius:10px;padding:14px 16px;margin:0 0 18px;font-size:15px'>📄 <b>Bitte sende uns:</b> {System.Net.WebUtility.HtmlEncode(wunsch)}</div>";

        var testHinweis = redirected
            ? $"<div style='background:#fef3c7;border:1px solid #fde68a;border-radius:10px;padding:10px 14px;margin-bottom:16px;font-size:13px;color:#92400e'>TESTMODUS — diese Mail wäre an <b>{emp.Email}</b> gegangen.</div>"
            : "";
        var html = $@"<div style='font-family:-apple-system,Segoe UI,Roboto,sans-serif;max-width:560px;margin:0 auto;background:#f4f0e8;border-radius:16px;padding:28px 30px;color:#3f3f3f'>
            {testHinweis}
            <h2 style='margin:0 0 6px;font-size:20px'>Hallo {emp.FirstName}</h2>
            <p style='font-size:14px;line-height:1.55;margin:0 0 18px'>Ab sofort hast du dein persönliches <b>OneCrew-Postfach</b> auf dem Handy: Lohnabrechnungen, Dokumente senden und Mitteilungen — alles an einem Ort.</p>
            {wunschHtml}
            <p style='text-align:center;margin:22px 0'>
                <a href='{url}' style='background:#1a1a1a;color:#ffffff;text-decoration:none;font-weight:700;font-size:15px;padding:13px 30px;border-radius:12px;display:inline-block'>Postfach jetzt einrichten</a>
            </p>
            <p style='font-size:12px;color:#8b8578;text-align:center;margin:0 0 22px'>Der Link ist persönlich und bis {bisTxt} gültig.</p>
            <div style='background:#ffffff;border-radius:12px;padding:16px 18px;font-size:13.5px;line-height:1.7'>
                <b>So geht es — in 4 Schritten:</b><br>
                ① Link oben antippen und dein eigenes Passwort festlegen.<br>
                ② Danach bietet dir die App <b>Face ID / Fingerabdruck</b> an — empfohlen, dann brauchst du kein Passwort mehr.<br>
                ③ App aufs Handy: iPhone → Teilen-Symbol → «Zum Home-Bildschirm». Android → Menü → «App installieren».<br>
                ④ Dokumente senden: einfach ein <b>Foto</b> machen (z.B. Ausweis) und abschicken — den Rest erledigt das Büro.
            </div>
            <p style='text-align:center;margin:20px 0 0'>
                <a href='{anleitungUrl}' style='display:inline-block;background:#ffffff;color:#3f3f3f;text-decoration:none;font-weight:700;font-size:15px;padding:12px 28px;border-radius:12px;border:1px solid #d8d2c6'>📖 Anleitung mit Bildern ansehen</a>
            </p>
            <p style='font-size:12.5px;color:#8b8578;margin:18px 0 0'>Fragen? Melde dich bei deiner Restaurantleitung.<br>Schaub Restaurants GmbH</p>
        </div>";
        var wunschText = string.IsNullOrWhiteSpace(wunsch) ? "" : $"\n\nBITTE SENDE UNS: {wunsch}";
        var text = $"Hallo {emp.FirstName}{wunschText}\n\nDein persönliches OneCrew-Postfach: {url}\n(gültig bis {bisTxt})\n\n1. Link öffnen und Passwort festlegen\n2. Face ID aktivieren (empfohlen)\n3. iPhone: Teilen -> Zum Home-Bildschirm / Android: App installieren\n4. Dokumente: Foto machen und senden - den Rest erledigt das Büro\n\nAnleitung: {anleitungUrl}\n\nSchaub Restaurants GmbH";

        var ok = await _email.SendAsync(to, $"{emp.FirstName} {emp.LastName}", subject, html, text,
            VersandKategorie.Postfach, emp.Id);
        if (!ok) return StatusCode(502, new { error = "E-Mail-Versand fehlgeschlagen (SMTP prüfen)." });

        return Ok(new { sentTo = to, redirected, empEmail = emp.Email, expiresAt });
    }

    // ── MA: Token prüfen (Landing zeigt Begrüssung) ─────────────────────
    // ── HR: Status des letzten Links (gesendet / geöffnet / eingerichtet) ──
    [HttpGet("status/{employeeId:int}")]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> Status(int employeeId)
    {
        var user = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.EmployeeId == employeeId);
        if (user == null) return Ok(new { hasToken = false });
        var t = await _db.PostfachSetupTokens.AsNoTracking()
            .Where(x => x.AppUserId == user.Id)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
        return Ok(new
        {
            hasToken   = t != null,
            createdAt  = t?.CreatedAt,
            expiresAt  = t?.ExpiresAt,
            openedAt   = t?.OpenedAt,
            usedAt     = t?.UsedAt,
            lastLoginAt = user.LastLoginAt,
        });
    }

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
        // «Link geöffnet»-Stempel (Walter 18.08.2026): erster Aufruf der Setup-Seite.
        if (t.OpenedAt == null)
        {
            t.OpenedAt = DateTime.Now;
            await _db.SaveChangesAsync();
        }
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

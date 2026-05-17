using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Security.Claims;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;

    public UsersController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/users – nur admin/superuser
    [HttpGet]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> GetAll()
    {
        // MA-Postfach-Accounts (employee_id NOT NULL) werden im MA-Detail
        // verwaltet, nicht in der Benutzer-Liste. Hier nur Backoffice-User.
        var users = await _context.AppUsers
            .Include(u => u.BranchAccess)
            .ThenInclude(ba => ba.CompanyProfile)
            .Where(u => u.EmployeeId == null)
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.FirstName,
                u.LastName,
                u.Email,
                u.Phone,
                u.Role,
                u.IsActive,
                u.IsHrTeam,
                u.IsSuperAdmin,
                u.CreatedAt,
                u.LastLoginAt,
                hasSignature = u.SignaturePng != null && u.SignaturePng.Length > 0,
                branches = u.BranchAccess.Select(ba => new
                {
                    id   = ba.CompanyProfileId,
                    name = ba.CompanyProfile.BranchName ?? ba.CompanyProfile.CompanyName,
                    code = ba.CompanyProfile.RestaurantCode
                })
            })
            .ToListAsync();

        return Ok(users);
    }

    public record CreateUserRequest(
        string Username, string? FirstName, string? LastName,
        string Email, string? Phone, string Password, string Role,
        List<int> BranchIds, bool? IsHrTeam = false);

    public record UpdateUserRequest(
        string Username, string? FirstName, string? LastName,
        string Email, string? Phone, string? Password,
        string Role, bool IsActive, List<int> BranchIds,
        bool? IsHrTeam = false);

    // POST /api/users – nur admin/superuser
    [HttpPost]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;

        if (callerRole == "superuser" && req.Role == "admin")
            return Forbid();

        if (await _context.AppUsers.AnyAsync(u => u.Email == req.Email))
            return BadRequest(new { message = "Diese E-Mail ist bereits vergeben." });

        var user = new AppUser
        {
            Username  = req.Username,
            FirstName = req.FirstName,
            LastName  = req.LastName,
            Email     = req.Email,
            Phone     = req.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role      = req.Role,
            IsActive  = true,
            IsHrTeam  = req.IsHrTeam ?? false,
            CreatedAt = DateTime.UtcNow
        };

        _context.AppUsers.Add(user);
        await _context.SaveChangesAsync();

        foreach (var branchId in req.BranchIds)
        {
            _context.UserBranchAccesses.Add(new UserBranchAccess
            {
                UserId = user.Id,
                CompanyProfileId = branchId
            });
        }
        await _context.SaveChangesAsync();

        return Ok(new { user.Id, user.Username, user.FirstName, user.LastName, user.Email, user.Phone, user.Role });
    }

    // PUT /api/users/{id}
    [HttpPut("{id}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest req)
    {
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var callerId   = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var user = await _context.AppUsers
            .Include(u => u.BranchAccess)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();

        if (callerRole == "superuser" && (user.Role == "admin" || req.Role == "admin"))
            return Forbid();

        // Super-Admin-Schutz (Walter-Vorgabe 15.05.2026):
        //   Ein Super-Admin-Datensatz darf nur von einem Super-Admin geändert
        //   werden — verhindert dass ein normaler Admin Walters Profil
        //   (Email, Passwort, Role, IsActive) manipuliert. Der IsSuperAdmin-
        //   Flag selbst wird über die API NIE gesetzt, nur per SQL.
        var callerIsSuper = await _context.AppUsers
            .Where(u => u.Id == callerId)
            .Select(u => u.IsSuperAdmin)
            .FirstOrDefaultAsync();
        if (user.IsSuperAdmin && !callerIsSuper)
            return StatusCode(403, new { message = "Nur ein Super-Admin darf einen Super-Admin-Account ändern." });

        if (callerId == id && !req.IsActive)
            return BadRequest(new { message = "Sie können sich nicht selbst deaktivieren." });

        user.Username  = req.Username;
        user.FirstName = req.FirstName;
        user.LastName  = req.LastName;
        user.Email     = req.Email;
        user.Phone     = req.Phone;
        user.Role      = req.Role;
        user.IsActive  = req.IsActive;
        user.IsHrTeam  = req.IsHrTeam ?? false;

        if (!string.IsNullOrWhiteSpace(req.Password))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);

        // Filialen-Zuweisungen neu setzen
        _context.UserBranchAccesses.RemoveRange(user.BranchAccess);
        foreach (var branchId in req.BranchIds)
        {
            _context.UserBranchAccesses.Add(new UserBranchAccess
            {
                UserId = user.Id,
                CompanyProfileId = branchId
            });
        }

        await _context.SaveChangesAsync();
        return Ok(new { user.Id, user.Username, user.FirstName, user.LastName, user.Email, user.Phone, user.Role, user.IsActive });
    }

    // DELETE /api/users/{id} – nur admin
    //
    // Super-Admin-Schutzregeln (Walter-Vorgabe 15.05.2026):
    //   • Super-Admin-Accounts können NIEMALS gelöscht werden (auch nicht
    //     von einem anderen Super-Admin oder dem User selbst).
    //   • Administrator-Accounts dürfen nur von einem Super-Admin gelöscht
    //     werden — normale Admins können sich nicht gegenseitig entfernen.
    [HttpDelete("{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var callerId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        if (callerId == id)
            return BadRequest(new { message = "Sie können sich nicht selbst löschen." });

        var user = await _context.AppUsers.FindAsync(id);
        if (user == null) return NotFound();

        if (user.IsSuperAdmin)
            return StatusCode(403, new { message = "Super-Admin-Accounts können nicht gelöscht werden." });

        if (user.Role == "admin")
        {
            var callerIsSuper = await _context.AppUsers
                .Where(u => u.Id == callerId)
                .Select(u => u.IsSuperAdmin)
                .FirstOrDefaultAsync();
            if (!callerIsSuper)
                return StatusCode(403, new { message = "Nur ein Super-Admin darf Administratoren löschen." });
        }

        _context.AppUsers.Remove(user);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ── Unterschrift ──────────────────────────────────────────────────────
    // Eigener User darf seine Unterschrift jederzeit pflegen. Admin/Superuser
    // dürfen auch fremde Unterschriften pflegen (z.B. für Kollegen einrichten).

    /// <summary>Unterschrift als Bild zurückgeben (Content-Type aus DB-Heuristik).</summary>
    [HttpGet("{id}/signature")]
    [AllowAnonymous]   // Damit <img src="..."> ohne Auth-Header funktioniert
    public async Task<IActionResult> GetSignature(int id)
    {
        var u = await _context.AppUsers
            .Where(x => x.Id == id)
            .Select(x => new { x.SignaturePng })
            .FirstOrDefaultAsync();
        if (u?.SignaturePng == null || u.SignaturePng.Length == 0)
            return NotFound();
        // Heuristik: PNG-Magic = 89 50 4E 47, JPEG-Magic = FF D8 FF
        var mime = "application/octet-stream";
        var b = u.SignaturePng;
        if (b.Length >= 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) mime = "image/png";
        else if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)            mime = "image/jpeg";
        Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        return File(b, mime);
    }

    /// <summary>Unterschrift hochladen / ersetzen (multipart, Feld "file").</summary>
    [HttpPut("{id}/signature")]
    [RequestSizeLimit(2 * 1024 * 1024)]  // 2 MB Maximum
    public async Task<IActionResult> UploadSignature(int id, [FromForm] IFormFile? file)
    {
        var callerId   = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        if (callerId != id && callerRole != "admin" && callerRole != "superuser")
            return Forbid();

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Keine Datei hochgeladen." });
        if (file.Length > 2 * 1024 * 1024)
            return BadRequest(new { message = "Datei zu groß (max 2 MB)." });

        // Nur Bild-MIME-Types
        var ct = (file.ContentType ?? "").ToLowerInvariant();
        if (!ct.StartsWith("image/"))
            return BadRequest(new { message = "Nur Bild-Dateien (PNG/JPG) erlaubt." });

        var user = await _context.AppUsers.FindAsync(id);
        if (user == null) return NotFound();

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var rawBytes = ms.ToArray();

        // Hintergrund-Entfernung: nahezu-weisse Pixel werden auf Alpha=0 gesetzt,
        // damit die Unterschrift auf Formularen ohne weisses Feld erscheint.
        // Eingang darf JPEG oder PNG sein — Output ist immer PNG mit Alpha.
        byte[] processed;
        try
        {
            processed = MakeWhiteTransparent(rawBytes);
        }
        catch
        {
            // Fallback: Bild konnte nicht verarbeitet werden → Original behalten.
            processed = rawBytes;
        }

        user.SignaturePng = processed;
        await _context.SaveChangesAsync();
        return Ok(new { id = user.Id, sizeBytes = user.SignaturePng.Length });
    }

    /// <summary>
    /// Wandelt nahezu-weisse Pixel in transparent um. Erweitert ein Bild mit
    /// weissem Hintergrund (Foto/Scan einer Unterschrift) zu einer PNG mit
    /// echtem Alpha-Kanal — Anti-Aliasing-Pixel werden semi-transparent.
    /// Schwellwerte sind defensiv gewählt; wenn die Unterschrift zu hell ist
    /// (Bleistift, blasse Tinte) kann sie ggf. zu stark ausgeblendet werden.
    /// </summary>
    private static byte[] MakeWhiteTransparent(byte[] inputBytes)
    {
        using var image = Image.Load<Rgba32>(inputBytes);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    ref Rgba32 px = ref row[x];
                    int min  = Math.Min(Math.Min(px.R, px.G), px.B);
                    if (min >= 240)
                    {
                        // sehr hell → komplett transparent
                        px.A = 0;
                    }
                    else if (min >= 180)
                    {
                        // Übergangsbereich → semi-transparent für weiche Kanten
                        // 180 → A≈255, 240 → A≈0 (lineare Skala)
                        var alpha = (byte)Math.Clamp((240 - min) * 255 / 60, 0, 255);
                        // nur abdunkeln, nicht aufhellen
                        if (alpha < px.A) px.A = alpha;
                    }
                    // sonst (dunklere Pixel) → unverändert: ist die Unterschrift selbst
                }
            }
        });
        using var outMs = new MemoryStream();
        image.SaveAsPng(outMs);
        return outMs.ToArray();
    }

    /// <summary>Unterschrift entfernen.</summary>
    [HttpDelete("{id}/signature")]
    public async Task<IActionResult> DeleteSignature(int id)
    {
        var callerId   = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        if (callerId != id && callerRole != "admin" && callerRole != "superuser")
            return Forbid();

        var user = await _context.AppUsers.FindAsync(id);
        if (user == null) return NotFound();
        user.SignaturePng = null;
        await _context.SaveChangesAsync();
        return NoContent();
    }
}

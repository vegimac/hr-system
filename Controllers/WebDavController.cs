using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace HrSystem.Controllers;

/// <summary>
/// WebDAV-Endpoint für Scan-Apps (Genius Scan, iOS Files, Adobe Scan, ScannerPro …).
///
/// Postfach-Struktur:
///   /webdav/                        ← Wurzel
///   /webdav/filialen/               ← Übersicht aller Filialen (jeder kann reinschicken)
///   /webdav/filialen/{slug}/        ← Filial-Postfach (Inhalt sichtbar bei Filial-Zugriff)
///   /webdav/hr/                     ← gemeinsames HR-Postfach (sichtbar: AppUser.IsHrTeam + Admin)
///   /webdav/admin/                  ← Admin-Postfach (sichtbar: nur Admin)
///   /webdav/benutzer/               ← App-Benutzer (User→User; nur reinlegen, Inbox fremder User unsichtbar)
///   /webdav/meine-mitteilungen/     ← eigene USER-Inbox (lesen/löschen/reinlegen «An mich»)
///
/// Senden ist offen: jeder authentifizierte App-User (nicht MA-Rolle) kann an
/// Filiale/HR/Admin/Benutzer senden. Lesen folgt dem Berechtigungs-Schema oben.
///
/// Authentifizierung: HTTP Basic mit E-Mail-Adresse oder Username + HR-Passwort.
/// </summary>
[ApiController]
// WebDAV macht seine EIGENE HTTP-Basic-Authentifizierung intern (E-Mail/Username
// + HR-Passwort, siehe unten). Daher von der globalen JWT-FallbackPolicy
// ausnehmen — sonst würde der 401-JWT-Check vor der Basic-Auth greifen und
// Scan-Apps könnten sich gar nicht erst anmelden.
[AllowAnonymous]
public class WebDavController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly string _storagePath;
    private readonly ILogger<WebDavController> _log;

    private const string F_FILIALEN = "filialen";
    private const string F_HR       = "hr";
    private const string F_ADMIN    = "admin";
    private const string F_BENUTZER = "benutzer";
    private const string F_MEINE    = "meine-mitteilungen";

    public WebDavController(AppDbContext db, IConfiguration config, IWebHostEnvironment env, ILogger<WebDavController> log)
    {
        _db = db;
        _log = log;
        var configured = config["Documents:StoragePath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine(env.ContentRootPath, "data", "documents");
        _storagePath = configured;
    }

    [AcceptVerbs("OPTIONS", "PROPFIND", "GET", "HEAD", "PUT", "DELETE",
                 "MKCOL", "PROPPATCH", "LOCK", "UNLOCK", "COPY", "MOVE")]
    [Route("/webdav/{**path}")]
    [Route("/webdav")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Handle(string? path = null)
    {
        var method = Request.Method.ToUpperInvariant();
        if (method == "OPTIONS") return HandleOptions();

        var user = await AuthenticateAsync();
        if (user == null)
        {
            Response.Headers["WWW-Authenticate"] = "Basic realm=\"Schaub HR Posteingang\"";
            return StatusCode(401);
        }

        var segments = string.IsNullOrEmpty(path)
            ? Array.Empty<string>()
            : path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        switch (method)
        {
            case "PROPFIND":  return await HandlePropFindAsync(user, segments);
            case "PUT":       return await HandlePutAsync(user, segments);
            case "GET":       return await HandleGetAsync(user, segments, includeBody: true);
            case "HEAD":      return await HandleGetAsync(user, segments, includeBody: false);
            case "DELETE":    return await HandleDeleteAsync(user, segments);
            case "MKCOL":     return StatusCode(403);
            case "PROPPATCH": return StatusCode(403);
            case "LOCK":      return Ok();
            case "UNLOCK":    return NoContent();
            case "COPY":
            case "MOVE":      return StatusCode(403);
            default:          return StatusCode(405);
        }
    }

    // ── OPTIONS ───────────────────────────────────────────────────────
    private IActionResult HandleOptions()
    {
        Response.Headers["DAV"] = "1, 2";
        Response.Headers["MS-Author-Via"] = "DAV";
        Response.Headers["Allow"] = "OPTIONS, GET, HEAD, PUT, DELETE, PROPFIND, PROPPATCH, MKCOL, COPY, MOVE, LOCK, UNLOCK";
        Response.Headers["Accept-Ranges"] = "bytes";
        return Ok();
    }

    // ── PROPFIND ──────────────────────────────────────────────────────
    private async Task<IActionResult> HandlePropFindAsync(AppUser user, string[] segments)
    {
        var depth = Request.Headers["Depth"].ToString();
        if (string.IsNullOrEmpty(depth)) depth = "1";

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.Append("<d:multistatus xmlns:d=\"DAV:\">");

        // ── Root: Top-Level-Postfächer ──
        if (segments.Length == 0)
        {
            AppendCollection(sb, "/webdav/", "Schaub HR Posteingang", DateTime.UtcNow);
            if (depth != "0")
            {
                AppendCollection(sb, "/webdav/filialen/", "Filialen", DateTime.UtcNow);
                AppendCollection(sb, "/webdav/hr/",       "HR",       DateTime.UtcNow);
                AppendCollection(sb, "/webdav/admin/",    "Admin",    DateTime.UtcNow);
                if (IsAppUser(user))
                {
                    AppendCollection(sb, $"/webdav/{F_BENUTZER}/", "Benutzer", DateTime.UtcNow);
                    AppendCollection(sb, $"/webdav/{F_MEINE}/", "Meine Mitteilungen", DateTime.UtcNow);
                }
            }
        }
        // ── /webdav/filialen/ ──
        else if (segments.Length == 1 && segments[0] == F_FILIALEN)
        {
            AppendCollection(sb, "/webdav/filialen/", "Filialen", DateTime.UtcNow);
            if (depth != "0")
            {
                // Alle aktiven Filialen — jeder kann an jede senden
                var filialen = await _db.CompanyProfiles
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.RestaurantCode)
                    .ThenBy(c => c.BranchName ?? c.CompanyName)
                    .ToListAsync();
                foreach (var f in filialen)
                {
                    var slug = FilialeSlug(f);
                    AppendCollection(sb, $"/webdav/filialen/{Uri.EscapeDataString(slug)}/", FilialeDisplay(f), DateTime.UtcNow);
                }
            }
        }
        // ── /webdav/filialen/{slug}/ ──
        else if (segments.Length == 2 && segments[0] == F_FILIALEN)
        {
            var filiale = await FindFilialeBySlugAsync(segments[1]);
            if (filiale == null) return StatusCode(404);

            AppendCollection(sb, $"/webdav/filialen/{Uri.EscapeDataString(segments[1])}/", FilialeDisplay(filiale), DateTime.UtcNow);

            if (depth != "0" && CanSeeBranchInbox(user, filiale.Id))
            {
                var docs = await _db.MailboxDocuments
                    .Where(d => d.TargetType == "BRANCH" && d.CompanyProfileId == filiale.Id)
                    .OrderByDescending(d => d.UploadedAt)
                    .ToListAsync();
                foreach (var d in docs)
                {
                    var fname = MakeListingFilename(d);
                    AppendFile(sb,
                        $"/webdav/filialen/{Uri.EscapeDataString(segments[1])}/{Uri.EscapeDataString(fname)}",
                        d.OriginalFilename, d.MimeType, d.FileSizeBytes ?? 0, d.UploadedAt);
                }
            }
        }
        // ── /webdav/filialen/{slug}/{file} ──
        else if (segments.Length == 3 && segments[0] == F_FILIALEN)
        {
            var filiale = await FindFilialeBySlugAsync(segments[1]);
            if (filiale == null) return StatusCode(404);
            if (!CanSeeBranchInbox(user, filiale.Id)) return StatusCode(403);

            var doc = await FindDocumentByListingNameAsync("BRANCH", filiale.Id, segments[2]);
            if (doc == null) return StatusCode(404);

            AppendFile(sb,
                $"/webdav/filialen/{Uri.EscapeDataString(segments[1])}/{Uri.EscapeDataString(segments[2])}",
                doc.OriginalFilename, doc.MimeType, doc.FileSizeBytes ?? 0, doc.UploadedAt);
        }
        // ── /webdav/hr/ ──
        else if (segments.Length == 1 && segments[0] == F_HR)
        {
            AppendCollection(sb, "/webdav/hr/", "HR", DateTime.UtcNow);
            if (depth != "0" && CanSeeHrInbox(user))
            {
                var docs = await _db.MailboxDocuments
                    .Where(d => d.TargetType == "HR")
                    .OrderByDescending(d => d.UploadedAt)
                    .ToListAsync();
                foreach (var d in docs)
                {
                    var fname = MakeListingFilename(d);
                    AppendFile(sb, $"/webdav/hr/{Uri.EscapeDataString(fname)}",
                        d.OriginalFilename, d.MimeType, d.FileSizeBytes ?? 0, d.UploadedAt);
                }
            }
        }
        else if (segments.Length == 2 && segments[0] == F_HR)
        {
            if (!CanSeeHrInbox(user)) return StatusCode(403);
            var doc = await FindDocumentByListingNameAsync("HR", null, segments[1]);
            if (doc == null) return StatusCode(404);
            AppendFile(sb, $"/webdav/hr/{Uri.EscapeDataString(segments[1])}",
                doc.OriginalFilename, doc.MimeType, doc.FileSizeBytes ?? 0, doc.UploadedAt);
        }
        // ── /webdav/admin/ ──
        else if (segments.Length == 1 && segments[0] == F_ADMIN)
        {
            AppendCollection(sb, "/webdav/admin/", "Admin", DateTime.UtcNow);
            if (depth != "0" && CanSeeAdminInbox(user))
            {
                var docs = await _db.MailboxDocuments
                    .Where(d => d.TargetType == "ADMIN")
                    .OrderByDescending(d => d.UploadedAt)
                    .ToListAsync();
                foreach (var d in docs)
                {
                    var fname = MakeListingFilename(d);
                    AppendFile(sb, $"/webdav/admin/{Uri.EscapeDataString(fname)}",
                        d.OriginalFilename, d.MimeType, d.FileSizeBytes ?? 0, d.UploadedAt);
                }
            }
        }
        else if (segments.Length == 2 && segments[0] == F_ADMIN)
        {
            if (!CanSeeAdminInbox(user)) return StatusCode(403);
            var doc = await FindDocumentByListingNameAsync("ADMIN", null, segments[1]);
            if (doc == null) return StatusCode(404);
            AppendFile(sb, $"/webdav/admin/{Uri.EscapeDataString(segments[1])}",
                doc.OriginalFilename, doc.MimeType, doc.FileSizeBytes ?? 0, doc.UploadedAt);
        }
        // ── /webdav/benutzer/ — Empfänger-Ordner (User→User) ──
        else if (segments.Length == 1 && segments[0] == F_BENUTZER)
        {
            if (!IsAppUser(user)) return StatusCode(403);
            AppendCollection(sb, $"/webdav/{F_BENUTZER}/", "Benutzer", DateTime.UtcNow);
            if (depth != "0")
            {
                var recipients = await ListUserRecipientsAsync(user);
                foreach (var r in recipients)
                {
                    var slug = UserSlug(r);
                    var label = r.Id == user.Id ? "An mich" : UserDisplay(r);
                    AppendCollection(sb,
                        $"/webdav/{F_BENUTZER}/{Uri.EscapeDataString(slug)}/",
                        label, DateTime.UtcNow);
                }
            }
        }
        // ── /webdav/benutzer/{slug}/ — Dropbox; eigene Inbox lesbar, fremde nicht ──
        else if (segments.Length == 2 && segments[0] == F_BENUTZER)
        {
            if (!IsAppUser(user)) return StatusCode(403);
            var target = await FindUserBySlugAsync(segments[1]);
            if (target == null || !IsAppUser(target)) return StatusCode(404);
            var display = target.Id == user.Id ? "An mich" : UserDisplay(target);
            AppendCollection(sb,
                $"/webdav/{F_BENUTZER}/{Uri.EscapeDataString(segments[1])}/",
                display, DateTime.UtcNow);
            if (depth != "0" && target.Id == user.Id)
            {
                var docs = await _db.MailboxDocuments
                    .Where(d => d.TargetType == "USER" && d.TargetUserId == user.Id)
                    .OrderByDescending(d => d.UploadedAt)
                    .ToListAsync();
                foreach (var d in docs)
                {
                    var fname = MakeListingFilename(d);
                    AppendFile(sb,
                        $"/webdav/{F_BENUTZER}/{Uri.EscapeDataString(segments[1])}/{Uri.EscapeDataString(fname)}",
                        d.OriginalFilename, d.MimeType, d.FileSizeBytes ?? 0, d.UploadedAt);
                }
            }
            // Fremde Inbox: keine Dateiliste (nur PUT).
        }
        else if (segments.Length == 3 && segments[0] == F_BENUTZER)
        {
            if (!IsAppUser(user)) return StatusCode(403);
            var target = await FindUserBySlugAsync(segments[1]);
            if (target == null || target.Id != user.Id) return StatusCode(404);
            var doc = await FindUserInboxDocumentAsync(user.Id, segments[2]);
            if (doc == null) return StatusCode(404);
            AppendFile(sb,
                $"/webdav/{F_BENUTZER}/{Uri.EscapeDataString(segments[1])}/{Uri.EscapeDataString(segments[2])}",
                doc.OriginalFilename, doc.MimeType, doc.FileSizeBytes ?? 0, doc.UploadedAt);
        }
        // ── /webdav/meine-mitteilungen/ — eigene USER-Inbox ──
        else if (segments.Length == 1 && segments[0] == F_MEINE)
        {
            if (!IsAppUser(user)) return StatusCode(403);
            AppendCollection(sb, $"/webdav/{F_MEINE}/", "Meine Mitteilungen", DateTime.UtcNow);
            if (depth != "0")
            {
                var docs = await _db.MailboxDocuments
                    .Where(d => d.TargetType == "USER" && d.TargetUserId == user.Id)
                    .OrderByDescending(d => d.UploadedAt)
                    .ToListAsync();
                foreach (var d in docs)
                {
                    var fname = MakeListingFilename(d);
                    AppendFile(sb, $"/webdav/{F_MEINE}/{Uri.EscapeDataString(fname)}",
                        d.OriginalFilename, d.MimeType, d.FileSizeBytes ?? 0, d.UploadedAt);
                }
            }
        }
        else if (segments.Length == 2 && segments[0] == F_MEINE)
        {
            if (!IsAppUser(user)) return StatusCode(403);
            var doc = await FindUserInboxDocumentAsync(user.Id, segments[1]);
            if (doc == null) return StatusCode(404);
            AppendFile(sb, $"/webdav/{F_MEINE}/{Uri.EscapeDataString(segments[1])}",
                doc.OriginalFilename, doc.MimeType, doc.FileSizeBytes ?? 0, doc.UploadedAt);
        }
        else
        {
            return StatusCode(404);
        }

        sb.Append("</d:multistatus>");
        return new ContentResult
        {
            Content = sb.ToString(),
            ContentType = "application/xml; charset=utf-8",
            StatusCode = 207
        };
    }

    // ── PUT ───────────────────────────────────────────────────────────
    private async Task<IActionResult> HandlePutAsync(AppUser user, string[] segments)
    {
        // Erlaubte Pfade:
        //   filialen/{slug}/{file}
        //   hr/{file}
        //   admin/{file}
        //   benutzer/{slug}/{file}   → TargetType USER
        string targetType;
        int companyProfileId;
        string fname;
        int? targetUserId = null;

        if (segments.Length == 3 && segments[0] == F_FILIALEN)
        {
            var filiale = await FindFilialeBySlugAsync(segments[1]);
            if (filiale == null) return StatusCode(404);
            targetType = "BRANCH";
            companyProfileId = filiale.Id;
            fname = segments[2];
        }
        else if (segments.Length == 2 && (segments[0] == F_HR || segments[0] == F_ADMIN))
        {
            targetType = segments[0] == F_HR ? "HR" : "ADMIN";
            // Filial-ID = Sender-Filiale (für Herkunfts-Info)
            var senderBranch = await ResolveSenderBranchAsync(user);
            if (senderBranch == null) return StatusCode(403);
            companyProfileId = senderBranch.Value;
            fname = segments[1];
        }
        else if (segments.Length == 3 && segments[0] == F_BENUTZER)
        {
            if (!IsAppUser(user)) return StatusCode(403);
            var target = await FindUserBySlugAsync(segments[1]);
            if (target == null || !target.IsActive || !IsAppUser(target))
                return StatusCode(404);
            // Selbst-Zustellung erlaubt (Walter 24.07.2026) — eigene Ablage-Box
            targetType = "USER";
            targetUserId = target.Id;
            var senderBranch = await ResolveSenderBranchAsync(user);
            if (senderBranch == null) return StatusCode(403);
            companyProfileId = senderBranch.Value;
            fname = segments[2];
        }
        else if (segments.Length == 2 && segments[0] == F_MEINE)
        {
            // Direkt in «Meine Mitteilungen» scannen (An mich)
            if (!IsAppUser(user)) return StatusCode(403);
            targetType = "USER";
            targetUserId = user.Id;
            var senderBranch = await ResolveSenderBranchAsync(user);
            if (senderBranch == null) return StatusCode(403);
            companyProfileId = senderBranch.Value;
            fname = segments[1];
        }
        else
        {
            return StatusCode(403);
        }

        // Datei in Storage schreiben
        var ext = Path.GetExtension(fname);
        var storageName = Guid.NewGuid().ToString("N") + ext;
        var dir = Path.Combine(_storagePath, "mailbox", companyProfileId.ToString());
        Directory.CreateDirectory(dir);
        var fullPath = Path.Combine(dir, storageName);

        long size;
        await using (var fs = System.IO.File.Create(fullPath))
        {
            await Request.Body.CopyToAsync(fs);
            size = fs.Length;
        }
        if (size == 0)
        {
            try { System.IO.File.Delete(fullPath); } catch { }
            return BadRequest();
        }

        var doc = new MailboxDocument
        {
            CompanyProfileId = companyProfileId,
            UploadedBy       = user.Id,
            UploadedAt       = DateTime.Now,
            OriginalFilename = fname,
            StorageFilename  = storageName,
            MimeType         = GuessMimeType(fname),
            FileSizeBytes    = size,
            TargetType       = targetType,
            TargetUserId     = targetUserId,
        };
        _db.MailboxDocuments.Add(doc);
        await _db.SaveChangesAsync();

        _log.LogInformation("WebDAV upload: doc {DocId} → {TargetType} user={TargetUserId} ({Size} bytes) {Filename} from {Uploader}",
            doc.Id, targetType, targetUserId, size, fname, user.Username);

        // TODO Phase 3: Email an HR-Team / Admin (je nach TargetType)

        Response.Headers["Location"] = "/" + string.Join("/", segments.Select(Uri.EscapeDataString));
        return StatusCode(201);
    }

    // ── GET / HEAD ────────────────────────────────────────────────────
    private async Task<IActionResult> HandleGetAsync(AppUser user, string[] segments, bool includeBody)
    {
        MailboxDocument? doc = null;

        if (segments.Length == 3 && segments[0] == F_FILIALEN)
        {
            var filiale = await FindFilialeBySlugAsync(segments[1]);
            if (filiale == null) return StatusCode(404);
            if (!CanSeeBranchInbox(user, filiale.Id)) return StatusCode(403);
            doc = await FindDocumentByListingNameAsync("BRANCH", filiale.Id, segments[2]);
        }
        else if (segments.Length == 2 && segments[0] == F_HR)
        {
            if (!CanSeeHrInbox(user)) return StatusCode(403);
            doc = await FindDocumentByListingNameAsync("HR", null, segments[1]);
        }
        else if (segments.Length == 2 && segments[0] == F_ADMIN)
        {
            if (!CanSeeAdminInbox(user)) return StatusCode(403);
            doc = await FindDocumentByListingNameAsync("ADMIN", null, segments[1]);
        }
        else if (segments.Length == 2 && segments[0] == F_MEINE)
        {
            if (!IsAppUser(user)) return StatusCode(403);
            doc = await FindUserInboxDocumentAsync(user.Id, segments[1]);
        }
        else if (segments.Length == 3 && segments[0] == F_BENUTZER)
        {
            if (!IsAppUser(user)) return StatusCode(403);
            var target = await FindUserBySlugAsync(segments[1]);
            if (target == null || target.Id != user.Id) return StatusCode(403);
            doc = await FindUserInboxDocumentAsync(user.Id, segments[2]);
        }

        if (doc == null) return StatusCode(404);

        var path = Path.Combine(_storagePath, "mailbox", doc.CompanyProfileId.ToString(), doc.StorageFilename);
        if (!System.IO.File.Exists(path)) return StatusCode(404);

        Response.Headers["Last-Modified"] = doc.UploadedAt.ToUniversalTime().ToString("R");
        if (!includeBody)
        {
            Response.Headers["Content-Length"] = (doc.FileSizeBytes ?? 0).ToString();
            Response.ContentType = doc.MimeType ?? "application/octet-stream";
            return new EmptyResult();
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(path);
        return File(bytes, doc.MimeType ?? "application/octet-stream", doc.OriginalFilename);
    }

    // ── DELETE ────────────────────────────────────────────────────────
    // Jeder darf löschen, der lesen darf. Genau der Postfach-Owner kontrolliert
    // also seinen Inhalt selbst.
    private async Task<IActionResult> HandleDeleteAsync(AppUser user, string[] segments)
    {
        MailboxDocument? doc = null;
        if (segments.Length == 3 && segments[0] == F_FILIALEN)
        {
            var filiale = await FindFilialeBySlugAsync(segments[1]);
            if (filiale == null) return StatusCode(404);
            if (!CanSeeBranchInbox(user, filiale.Id)) return StatusCode(403);
            doc = await FindDocumentByListingNameAsync("BRANCH", filiale.Id, segments[2]);
        }
        else if (segments.Length == 2 && segments[0] == F_HR)
        {
            if (!CanSeeHrInbox(user)) return StatusCode(403);
            doc = await FindDocumentByListingNameAsync("HR", null, segments[1]);
        }
        else if (segments.Length == 2 && segments[0] == F_ADMIN)
        {
            if (!CanSeeAdminInbox(user)) return StatusCode(403);
            doc = await FindDocumentByListingNameAsync("ADMIN", null, segments[1]);
        }
        else if (segments.Length == 2 && segments[0] == F_MEINE)
        {
            if (!IsAppUser(user)) return StatusCode(403);
            doc = await FindUserInboxDocumentAsync(user.Id, segments[1]);
        }
        else if (segments.Length == 3 && segments[0] == F_BENUTZER)
        {
            if (!IsAppUser(user)) return StatusCode(403);
            var target = await FindUserBySlugAsync(segments[1]);
            if (target == null || target.Id != user.Id) return StatusCode(403);
            doc = await FindUserInboxDocumentAsync(user.Id, segments[2]);
        }

        if (doc == null) return StatusCode(404);

        var path = Path.Combine(_storagePath, "mailbox", doc.CompanyProfileId.ToString(), doc.StorageFilename);
        if (System.IO.File.Exists(path))
        {
            try { System.IO.File.Delete(path); } catch { }
        }
        _db.MailboxDocuments.Remove(doc);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Auth ──────────────────────────────────────────────────────────
    private async Task<AppUser?> AuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader)) return null;
        var auth = authHeader.ToString();
        if (!auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return null;

        string decoded;
        try { decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth.Substring(6).Trim())); }
        catch { return null; }

        var idx = decoded.IndexOf(':');
        if (idx < 0) return null;
        var login = decoded.Substring(0, idx);
        var password = decoded.Substring(idx + 1);
        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password)) return null;

        // Login case-INSENSITIV + getrimmt (Walter 12.07.2026, Genius-Scan-401):
        // Scan-Apps normalisieren E-Mail-Adressen gern klein — der exakte
        // Vergleich schlug dann fehl, obwohl die Zugangsdaten stimmten.
        var loginNorm = login.Trim().ToLowerInvariant();
        var user = await _db.AppUsers
            .Include(u => u.BranchAccess)
            .FirstOrDefaultAsync(u => u.IsActive
                && ((u.Email ?? "").ToLower() == loginNorm
                 || (u.Username ?? "").ToLower() == loginNorm));
        if (user == null) return null;
        try
        {
            if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) return null;
        }
        catch { return null; }
        return user;
    }

    // ── Berechtigungen ────────────────────────────────────────────────
    private bool CanSeeBranchInbox(AppUser user, int companyProfileId)
    {
        if (user.Role == "admin") return true;
        // Superuser: nur wenn er Branch-Access auf diese Filiale hat
        // (Pat hat Zugriff auf alle 6, Senada nur auf Oftringen — beides funktioniert)
        return user.BranchAccess?.Any(b => b.CompanyProfileId == companyProfileId) ?? false;
    }

    private bool CanSeeHrInbox(AppUser user)
        => user.Role == "admin" || user.IsHrTeam;

    private bool CanSeeAdminInbox(AppUser user)
        => user.Role == "admin";

    /// <summary>App-User (nicht MA-Rolle employee) — darf User→User via WebDAV.</summary>
    private static bool IsAppUser(AppUser user)
        => !string.Equals(user.Role, "employee", StringComparison.OrdinalIgnoreCase);

    private async Task<int?> ResolveSenderBranchAsync(AppUser user)
    {
        var first = user.BranchAccess?.OrderBy(b => b.CompanyProfileId).FirstOrDefault();
        if (first != null) return first.CompanyProfileId;
        if (user.Role == "admin")
        {
            var any = await _db.CompanyProfiles
                .Where(c => c.IsActive).OrderBy(c => c.Id).FirstOrDefaultAsync();
            return any?.Id;
        }
        return null;
    }

    // ── Filiale-Slug ──────────────────────────────────────────────────
    private async Task<CompanyProfile?> FindFilialeBySlugAsync(string slug)
    {
        // Slug-Format: "{code}" oder "{code}-{name-slug}"
        if (string.IsNullOrEmpty(slug)) return null;
        var firstDash = slug.IndexOf('-');
        var code = firstDash > 0 ? slug.Substring(0, firstDash) : slug;
        return await _db.CompanyProfiles
            .FirstOrDefaultAsync(c => c.IsActive && c.RestaurantCode == code);
    }

    private static string FilialeSlug(CompanyProfile c)
    {
        var code = c.RestaurantCode ?? c.Id.ToString();
        var name = c.BranchName ?? c.CompanyName ?? "";
        var nameSlug = Slugify(name);
        return string.IsNullOrEmpty(nameSlug) ? code : $"{code}-{nameSlug}";
    }

    private static string FilialeDisplay(CompanyProfile c)
    {
        var code = c.RestaurantCode ?? "";
        var name = c.BranchName ?? c.CompanyName ?? "";
        return string.IsNullOrEmpty(code) ? name : $"{code} {name}".Trim();
    }

    // ── Benutzer-Slug (User→User) ─────────────────────────────────────
    // Format: "{id}-{name-slug}" — Id ist stabil, Name nur Anzeige.
    private async Task<List<AppUser>> ListUserRecipientsAsync(AppUser sender)
    {
        // Eigener User zuerst («An mich»), danach die anderen alphabetisch.
        var others = await _db.AppUsers.AsNoTracking()
            .Where(u => u.IsActive
                     && u.Role != "employee"
                     && u.Id != sender.Id)
            .OrderBy(u => u.FirstName ?? "")
            .ThenBy(u => u.LastName ?? u.Username)
            .ToListAsync();
        var me = await _db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == sender.Id);
        var list = new List<AppUser>();
        if (me != null) list.Add(me);
        list.AddRange(others);
        return list;
    }

    private async Task<AppUser?> FindUserBySlugAsync(string slug)
    {
        if (string.IsNullOrEmpty(slug)) return null;
        var dash = slug.IndexOf('-');
        var idPart = dash > 0 ? slug.Substring(0, dash) : slug;
        if (!int.TryParse(idPart, out var id)) return null;
        return await _db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && u.IsActive);
    }

    private static string UserSlug(AppUser u)
    {
        var name = $"{u.FirstName} {u.LastName}".Trim();
        if (string.IsNullOrEmpty(name)) name = u.Username ?? "";
        var nameSlug = Slugify(name);
        return string.IsNullOrEmpty(nameSlug) ? u.Id.ToString() : $"{u.Id}-{nameSlug}";
    }

    private static string UserDisplay(AppUser u)
    {
        var name = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrEmpty(name) ? (u.Username ?? $"User {u.Id}") : name;
    }

    private async Task<MailboxDocument?> FindUserInboxDocumentAsync(int userId, string fname)
    {
        var us = fname.IndexOf('_');
        if (us > 0 && int.TryParse(fname.Substring(0, us), out var docId))
        {
            return await _db.MailboxDocuments
                .FirstOrDefaultAsync(d => d.Id == docId
                                       && d.TargetType == "USER"
                                       && d.TargetUserId == userId);
        }
        return await _db.MailboxDocuments
            .Where(d => d.TargetType == "USER"
                     && d.TargetUserId == userId
                     && d.OriginalFilename == fname)
            .OrderByDescending(d => d.UploadedAt)
            .FirstOrDefaultAsync();
    }

    // ── Document Lookup ───────────────────────────────────────────────
    private async Task<MailboxDocument?> FindDocumentByListingNameAsync(string targetType, int? branchId, string fname)
    {
        var us = fname.IndexOf('_');
        if (us > 0 && int.TryParse(fname.Substring(0, us), out var docId))
        {
            return await _db.MailboxDocuments
                .Where(d => d.Id == docId && d.TargetType == targetType
                            && (branchId == null || d.CompanyProfileId == branchId))
                .FirstOrDefaultAsync();
        }
        // Fallback: OriginalFilename matchen (z.B. direkt nach PUT)
        var q = _db.MailboxDocuments
            .Where(d => d.TargetType == targetType && d.OriginalFilename == fname);
        if (branchId != null) q = q.Where(d => d.CompanyProfileId == branchId);
        return await q.OrderByDescending(d => d.UploadedAt).FirstOrDefaultAsync();
    }

    // ── XML / Slug-Helpers ────────────────────────────────────────────
    private static void AppendCollection(StringBuilder sb, string href, string displayName, DateTime modified)
    {
        sb.Append("<d:response>");
        sb.Append($"<d:href>{XmlEscape(href)}</d:href>");
        sb.Append("<d:propstat>");
        sb.Append("<d:prop>");
        sb.Append("<d:resourcetype><d:collection/></d:resourcetype>");
        sb.Append($"<d:displayname>{XmlEscape(displayName)}</d:displayname>");
        sb.Append($"<d:getlastmodified>{modified.ToUniversalTime():R}</d:getlastmodified>");
        sb.Append("<d:supportedlock><d:lockentry><d:lockscope><d:exclusive/></d:lockscope><d:locktype><d:write/></d:locktype></d:lockentry></d:supportedlock>");
        sb.Append("</d:prop>");
        sb.Append("<d:status>HTTP/1.1 200 OK</d:status>");
        sb.Append("</d:propstat>");
        sb.Append("</d:response>");
    }

    private static void AppendFile(StringBuilder sb, string href, string displayName, string? mimeType, long size, DateTime modified)
    {
        sb.Append("<d:response>");
        sb.Append($"<d:href>{XmlEscape(href)}</d:href>");
        sb.Append("<d:propstat>");
        sb.Append("<d:prop>");
        sb.Append("<d:resourcetype/>");
        sb.Append($"<d:displayname>{XmlEscape(displayName)}</d:displayname>");
        sb.Append($"<d:getcontentlength>{size}</d:getcontentlength>");
        sb.Append($"<d:getcontenttype>{XmlEscape(mimeType ?? "application/octet-stream")}</d:getcontenttype>");
        sb.Append($"<d:getlastmodified>{modified.ToUniversalTime():R}</d:getlastmodified>");
        sb.Append("</d:prop>");
        sb.Append("<d:status>HTTP/1.1 200 OK</d:status>");
        sb.Append("</d:propstat>");
        sb.Append("</d:response>");
    }

    private static string Slugify(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Normalize(NormalizationForm.FormD);
        var noDiacritics = new StringBuilder();
        foreach (var c in s)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
                noDiacritics.Append(c);
        }
        var slug = Regex.Replace(noDiacritics.ToString(), @"[^A-Za-z0-9._-]+", "-")
                        .Trim('-').ToLowerInvariant();
        return slug;
    }

    private static string MakeListingFilename(MailboxDocument d)
    {
        var safe = Regex.Replace(d.OriginalFilename ?? "scan.pdf", @"[\\/:""*?<>|]", "_");
        return $"{d.Id}_{safe}";
    }

    private static string GuessMimeType(string fname)
    {
        var ext = Path.GetExtension(fname).ToLowerInvariant();
        return ext switch
        {
            ".pdf"  => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"  => "image/png",
            ".gif"  => "image/gif",
            ".heic" => "image/heic",
            ".webp" => "image/webp",
            ".tif" or ".tiff" => "image/tiff",
            ".txt"  => "text/plain",
            ".doc"  => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls"  => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _       => "application/octet-stream",
        };
    }

    private static string XmlEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
    }
}

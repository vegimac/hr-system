using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HrSystem.Controllers;

/// <summary>
/// Posteingang pro Filiale: Geschäftsführer laden Dokumente hoch
/// (Arztzeugnisse, unterschriebene Verträge etc.), Admin/Superuser
/// sortieren sie in die MA-Personalakte ein oder löschen sie.
/// </summary>
// KEIN klassenweites [Authorize] (Walter-Vorgabe 20.05.2026): die globale
// DefaultPolicy/FallbackPolicy (admin,superuser,user) greift bereits für jede
// Methode OHNE eigenes Attribut → die nicht annotierten Methoden (postfaecher,
// count, notify-recipients, upload, delete) sind damit HR-only. Die MA-Methoden
// (GET, ma-outbox, ma-upload, download, preview) tragen ein eigenes
// [Authorize(Roles="...,employee")] und prüfen die Eigentümerschaft selbst.
// Ein klassenweites [Authorize] würde via UND-Verknüpfung die employee-Freigabe
// auf Methodenebene wieder aushebeln — daher bewusst weggelassen.
[ApiController]
[Route("api/mailbox")]
public class MailboxController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly string _storagePath;

    public MailboxController(AppDbContext db, IConfiguration config, IWebHostEnvironment env)
    {
        _db = db;
        var configured = config["Documents:StoragePath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine(env.ContentRootPath, "data", "documents");
        _storagePath = configured;
        Directory.CreateDirectory(Path.Combine(_storagePath, "mailbox"));
    }

    // ── GET: Liste der Dokumente eines Postfachs ──────────────────────────
    // type = "BRANCH" (default, mit companyProfileId) | "HR" | "ADMIN"
    [HttpGet]
    [Authorize(Roles = "admin,superuser,user,employee")]   // MA sieht NUR eigenes EMPLOYEE-Postfach (selfAccess-Check unten)
    public async Task<IActionResult> GetForPostfach(
        [FromQuery] int? companyProfileId,
        [FromQuery] int? employeeId,
        [FromQuery] string? type)
    {
        var t = (type ?? "BRANCH").ToUpperInvariant();
        IQueryable<MailboxDocument> q = _db.MailboxDocuments;

        if (t == "BRANCH")
        {
            if (companyProfileId == null) return BadRequest(new { error = "companyProfileId fehlt." });
            if (!await UserHasBranchAccessAsync(companyProfileId.Value)) return Forbid();
            q = q.Where(m => m.TargetType == "BRANCH" && m.CompanyProfileId == companyProfileId.Value);
        }
        else if (t == "HR")
        {
            if (!await UserCanSeeHrAsync()) return Forbid();
            q = q.Where(m => m.TargetType == "HR");
        }
        else if (t == "ADMIN")
        {
            if (!UserIsAdmin()) return Forbid();
            q = q.Where(m => m.TargetType == "ADMIN");
        }
        else if (t == "EMPLOYEE")
        {
            if (employeeId == null) return BadRequest(new { error = "employeeId fehlt." });
            // Berechtigung: Backoffice mit Branch-Zugang zur Filiale des MA,
            // oder der MA selbst (eigenes Postfach).
            var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId.Value);
            if (emp == null) return NotFound(new { error = "Mitarbeiter nicht gefunden." });
            var uid = GetCurrentUserId();
            var user = uid.HasValue ? await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == uid) : null;
            bool selfAccess = user?.EmployeeId == employeeId;
            // Branch-Access wird gegen alle aktiven Employments des MA geprüft
            bool branchAccess = false;
            if (UserIsAdmin()) branchAccess = true;
            else
            {
                var empBranches = await _db.Employments
                    .Where(e => e.EmployeeId == employeeId && e.IsActive && e.CompanyProfileId.HasValue)
                    .Select(e => e.CompanyProfileId!.Value)
                    .Distinct()
                    .ToListAsync();
                foreach (var bId in empBranches)
                {
                    if (await UserHasBranchAccessAsync(bId)) { branchAccess = true; break; }
                }
            }
            if (!selfAccess && !branchAccess) return Forbid();
            q = q.Where(m => m.TargetType == "EMPLOYEE" && m.EmployeeId == employeeId.Value);
        }
        else
        {
            return BadRequest(new { error = "Unbekannter Postfach-Typ." });
        }

        var docs = await q
            .OrderByDescending(m => m.UploadedAt)
            .Select(m => new {
                m.Id,
                m.OriginalFilename,
                m.MimeType,
                m.FileSizeBytes,
                m.Bemerkung,
                m.UploadedAt,
                m.TargetType,
                m.CompanyProfileId,
                CompanyProfile = m.CompanyProfile == null ? null : new {
                    m.CompanyProfile.Id, m.CompanyProfile.RestaurantCode,
                    name = m.CompanyProfile.BranchName ?? m.CompanyProfile.CompanyName
                },
                Uploader   = m.Uploader == null ? null : new { m.Uploader.Id, name = (m.Uploader.FirstName ?? "") + " " + (m.Uploader.LastName ?? ""), m.Uploader.Username },
                Employee   = m.Employee == null ? null : new { m.Employee.Id, name = m.Employee.FirstName + " " + m.Employee.LastName, m.Employee.EmployeeNumber },
                NotifyUser = m.NotifyUser == null ? null : new { m.NotifyUser.Id, name = (m.NotifyUser.FirstName ?? "") + " " + (m.NotifyUser.LastName ?? ""), m.NotifyUser.Username },
            })
            .ToListAsync();

        return Ok(docs);
    }

    // ── GET: Liste der für den User sichtbaren Postfächer ─────────────────
    // Frontend nutzt das, um den Postfach-Picker zu rendern.
    [HttpGet("postfaecher")]
    public async Task<IActionResult> GetVisiblePostfaecher()
    {
        var allowedBranchIds = await GetUserAllowedBranchIdsAsync();
        var branches = await _db.CompanyProfiles
            .Where(c => c.IsActive && allowedBranchIds.Contains(c.Id))
            .OrderBy(c => c.RestaurantCode)
            .ThenBy(c => c.BranchName ?? c.CompanyName)
            .ToListAsync();

        var counts = await _db.MailboxDocuments
            .GroupBy(m => new { m.TargetType, m.CompanyProfileId })
            .Select(g => new { g.Key.TargetType, g.Key.CompanyProfileId, count = g.Count() })
            .ToListAsync();

        var result = new List<object>();
        foreach (var b in branches)
        {
            result.Add(new
            {
                type = "BRANCH",
                companyProfileId = (int?)b.Id,
                code = b.RestaurantCode,
                name = b.BranchName ?? b.CompanyName,
                count = counts.Where(c => c.TargetType == "BRANCH" && c.CompanyProfileId == b.Id).Sum(c => c.count),
            });
        }
        if (await UserCanSeeHrAsync())
        {
            result.Add(new
            {
                type = "HR",
                companyProfileId = (int?)null,
                code = (string?)null,
                name = "HR",
                count = counts.Where(c => c.TargetType == "HR").Sum(c => c.count),
            });
        }
        if (UserIsAdmin())
        {
            result.Add(new
            {
                type = "ADMIN",
                companyProfileId = (int?)null,
                code = (string?)null,
                name = "Admin",
                count = counts.Where(c => c.TargetType == "ADMIN").Sum(c => c.count),
            });
        }
        return Ok(result);
    }

    // ── GET: Gesamt-Anzahl offener Dokumente für Sidebar-Badge ────────────
    [HttpGet("count")]
    public async Task<IActionResult> Count()
    {
        var allowedBranches = await GetUserAllowedBranchIdsAsync();

        var branchCount = await _db.MailboxDocuments
            .Where(m => m.TargetType == "BRANCH" && allowedBranches.Contains(m.CompanyProfileId))
            .CountAsync();

        var hrCount = await UserCanSeeHrAsync()
            ? await _db.MailboxDocuments.Where(m => m.TargetType == "HR").CountAsync()
            : 0;

        var adminCount = UserIsAdmin()
            ? await _db.MailboxDocuments.Where(m => m.TargetType == "ADMIN").CountAsync()
            : 0;

        return Ok(new { count = branchCount + hrCount + adminCount });
    }

    // ── GET: Empfänger-Dropdown (Admin/Superuser für Email-Benachrichtigung) ──
    [HttpGet("notify-recipients")]
    public async Task<IActionResult> GetRecipients()
    {
        var users = await _db.AppUsers
            .Where(u => u.IsActive && (u.Role == "admin" || u.Role == "superuser"))
            .OrderBy(u => u.LastName ?? u.Username)
            .Select(u => new {
                u.Id,
                Name = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
                u.Username, u.Email, u.Role
            })
            .ToListAsync();
        return Ok(users);
    }

    // ── POST: Dokument hochladen ──────────────────────────────────────────
    // targetType: BRANCH (default) | HR | ADMIN
    //   BRANCH benötigt companyProfileId (Filial-Postfach)
    //   HR/ADMIN: companyProfileId optional — wenn leer, wird die erste Filiale
    //   des Uploaders verwendet (für Herkunfts-Information)
    [HttpPost("upload")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 100_000_000)] // 100 MB
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        [FromForm] int? companyProfileId,
        [FromForm] string? bemerkung,
        [FromForm] int? employeeId,
        [FromForm] int? notifyUserId,
        [FromForm] string? targetType)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });

        var t = (targetType ?? "BRANCH").ToUpperInvariant();
        int effectiveBranchId;

        if (t == "BRANCH")
        {
            if (companyProfileId == null) return BadRequest(new { error = "companyProfileId fehlt." });
            // Senden ist offen — auch ohne Lese-Zugriff auf die Filiale ist
            // Reinschicken erlaubt. Lese-Berechtigung wird beim Anzeigen geprüft.
            effectiveBranchId = companyProfileId.Value;
        }
        else if (t == "HR" || t == "ADMIN")
        {
            // Filial-ID = Sender-Filiale (für Herkunfts-Info)
            if (companyProfileId.HasValue)
            {
                effectiveBranchId = companyProfileId.Value;
            }
            else
            {
                var allowed = await GetUserAllowedBranchIdsAsync();
                if (allowed.Count == 0) return BadRequest(new { error = "Keine Filial-Zuordnung." });
                effectiveBranchId = allowed.First();
            }
        }
        else
        {
            return BadRequest(new { error = "Unbekannter Postfach-Typ." });
        }

        var ext = Path.GetExtension(file.FileName);
        var storageName = Guid.NewGuid().ToString("N") + ext;
        var dir = Path.Combine(_storagePath, "mailbox", effectiveBranchId.ToString());
        Directory.CreateDirectory(dir);
        var fullPath = Path.Combine(dir, storageName);

        await using (var stream = System.IO.File.Create(fullPath))
            await file.CopyToAsync(stream);

        var doc = new MailboxDocument
        {
            CompanyProfileId  = effectiveBranchId,
            UploadedBy        = GetCurrentUserId(),
            UploadedAt        = DateTime.UtcNow,
            OriginalFilename  = file.FileName,
            StorageFilename   = storageName,
            MimeType          = file.ContentType,
            FileSizeBytes     = file.Length,
            Bemerkung         = string.IsNullOrWhiteSpace(bemerkung) ? null : bemerkung.Trim(),
            EmployeeId        = employeeId,
            NotifyUserId      = notifyUserId,
            TargetType        = t,
        };
        _db.MailboxDocuments.Add(doc);
        await _db.SaveChangesAsync();

        // TODO Phase 2: Email-Benachrichtigung an notifyUserId senden

        return Ok(new { id = doc.Id });
    }

    // ── GET: MA-Outbox (Dokumente die der MA selbst gesendet hat) ────────
    // Listet MailboxDocuments wo UploadedBy = aktueller User AND TargetType
    // in (BRANCH, HR). Wird in der Mobile-Postfach-Sicht als "Gesendet"-Tab
    // angezeigt — der MA hat damit Übersicht über seine Sendungen an
    // Geschäftsführung und HR.
    [HttpGet("ma-outbox")]
    [Authorize(Roles = "admin,superuser,user,employee")]   // filtert auf UploadedBy == eigener User
    public async Task<IActionResult> MaOutbox()
    {
        var uid = GetCurrentUserId();
        if (uid is null) return Unauthorized();
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == uid);
        if (user == null || user.Role != "employee" || !user.EmployeeId.HasValue)
            return Forbid();

        var docs = await _db.MailboxDocuments
            .Where(m => m.UploadedBy == uid
                     && (m.TargetType == "BRANCH" || m.TargetType == "HR"))
            .OrderByDescending(m => m.UploadedAt)
            .Select(m => new {
                m.Id,
                m.OriginalFilename,
                m.MimeType,
                m.FileSizeBytes,
                m.Bemerkung,
                m.UploadedAt,
                m.TargetType,
                m.CompanyProfileId,
                CompanyProfile = m.CompanyProfile == null ? null : new {
                    m.CompanyProfile.Id, m.CompanyProfile.RestaurantCode,
                    name = m.CompanyProfile.BranchName ?? m.CompanyProfile.CompanyName
                }
            })
            .ToListAsync();

        return Ok(docs);
    }

    // ── POST: MA-Upload (Mitarbeiter sendet Dokument an GF oder HR) ──────
    // Schmaler, auf "employee"-Rolle beschränkter Upload-Endpoint. Wird
    // von der postfach.html-Mobile-Seite genutzt — z.B. für Arztzeugnis-
    // Fotos. Sicherheits-Pattern: alle relevanten Felder werden vom Server
    // gesetzt (CompanyProfileId aus aktivem Employment, EmployeeId aus
    // Auth-Token), der MA kann nur Datei + Empfänger-Typ + optionale
    // Bemerkung wählen.
    [HttpPost("ma-upload")]
    [Authorize(Roles = "admin,superuser,user,employee")]   // setzt EmployeeId/Filiale serverseitig aus Token
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 20_000_000)] // 20 MB
    public async Task<IActionResult> MaUpload(
        [FromForm] IFormFile file,
        [FromForm] string? targetType,
        [FromForm] string? bemerkung)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });
        if (file.Length > 10_000_000)
            return BadRequest(new { error = "Datei zu gross (max. 10 MB)." });

        // MIME-Type-Whitelist: Bilder + PDF (HEIC für iPhones)
        var allowedMimes = new[] {
            "image/jpeg", "image/jpg", "image/png", "image/heic", "image/heif", "image/webp",
            "application/pdf"
        };
        var mt = (file.ContentType ?? "").ToLowerInvariant();
        if (!allowedMimes.Contains(mt))
            return BadRequest(new { error = "Nur Fotos (JPG/PNG/HEIC) oder PDF sind erlaubt." });

        // Aufrufer muss employee-Rolle mit verknüpftem MA sein
        var uid = GetCurrentUserId();
        if (uid is null) return Unauthorized();
        var user = await _db.AppUsers
            .Include(u => u.Employee)
            .FirstOrDefaultAsync(u => u.Id == uid);
        if (user == null || user.Role != "employee" || !user.EmployeeId.HasValue || user.Employee == null)
            return Forbid();
        if (!user.Employee.IsActive)
            return Forbid();

        // Empfänger-Typ: nur BRANCH oder HR — alles andere blockiert
        var t = (targetType ?? "BRANCH").ToUpperInvariant();
        if (t != "BRANCH" && t != "HR")
            return BadRequest(new { error = "Empfänger ungültig." });

        // CompanyProfileId aus aktivem Employment des MA — der MA kann
        // NICHT eine andere Filiale wählen.
        var branchId = await _db.Employments
            .Where(e => e.EmployeeId == user.EmployeeId.Value && e.IsActive && e.CompanyProfileId.HasValue)
            .OrderByDescending(e => e.ContractStartDate)
            .Select(e => e.CompanyProfileId)
            .FirstOrDefaultAsync();
        if (!branchId.HasValue)
            return BadRequest(new { error = "Keine aktive Filial-Zuordnung gefunden." });

        // Filename sanitization: nur Extension behalten, Rest neu generieren
        var ext = (Path.GetExtension(file.FileName) ?? "").ToLowerInvariant();
        if (ext.Length > 8 || ext.Contains("/") || ext.Contains("\\"))
            ext = ".bin";
        var storageName = Guid.NewGuid().ToString("N") + ext;
        var dir = Path.Combine(_storagePath, "mailbox", branchId.Value.ToString());
        Directory.CreateDirectory(dir);
        var fullPath = Path.Combine(dir, storageName);

        await using (var stream = System.IO.File.Create(fullPath))
            await file.CopyToAsync(stream);

        // Default-Bemerkung wenn keine angegeben: "Upload — Vorname Nachname"
        var rawBemerkung = (bemerkung ?? "").Trim();
        var senderLabel = $"{user.Employee.FirstName} {user.Employee.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(rawBemerkung))
            rawBemerkung = $"Upload — {senderLabel}";

        // Originalfilename säubern (max 200 Zeichen, ohne Pfad)
        var origName = Path.GetFileName(file.FileName ?? "upload");
        if (origName.Length > 200) origName = origName.Substring(0, 200);

        var doc = new MailboxDocument
        {
            CompanyProfileId = branchId.Value,
            UploadedBy       = uid,
            UploadedAt       = DateTime.UtcNow,
            OriginalFilename = origName,
            StorageFilename  = storageName,
            MimeType         = file.ContentType,
            FileSizeBytes    = file.Length,
            Bemerkung        = rawBemerkung,
            EmployeeId       = user.EmployeeId,
            NotifyUserId     = null,
            TargetType       = t,
        };
        _db.MailboxDocuments.Add(doc);
        await _db.SaveChangesAsync();

        return Ok(new { id = doc.Id, message = "Dokument wurde versendet." });
    }

    // ── GET: Dokument herunterladen ───────────────────────────────────────
    [HttpGet("{id}/download")]
    [Authorize(Roles = "admin,superuser,user,employee")]   // UserCanViewDocumentAsync prüft Eigentümerschaft
    public async Task<IActionResult> Download(int id)
    {
        var doc = await _db.MailboxDocuments.FindAsync(id);
        if (doc is null) return NotFound();
        if (!await UserCanViewDocumentAsync(doc))
            return Forbid();

        var path = Path.Combine(_storagePath, "mailbox", doc.CompanyProfileId.ToString(), doc.StorageFilename);
        if (!System.IO.File.Exists(path)) return NotFound(new { error = "Datei nicht mehr im Storage." });

        var bytes = await System.IO.File.ReadAllBytesAsync(path);
        return File(bytes, doc.MimeType ?? "application/octet-stream", doc.OriginalFilename);
    }

    // ── GET: Dokument inline anzeigen (für Vorschau-Modal) ────────────────
    [HttpGet("{id}/preview")]
    [Authorize(Roles = "admin,superuser,user,employee")]   // UserCanViewDocumentAsync prüft Eigentümerschaft
    public async Task<IActionResult> Preview(int id)
    {
        var doc = await _db.MailboxDocuments.FindAsync(id);
        if (doc is null) return NotFound();
        if (!await UserCanViewDocumentAsync(doc))
            return Forbid();

        var path = Path.Combine(_storagePath, "mailbox", doc.CompanyProfileId.ToString(), doc.StorageFilename);
        if (!System.IO.File.Exists(path)) return NotFound();

        var bytes = await System.IO.File.ReadAllBytesAsync(path);
        // Content-Disposition: inline → Browser zeigt PDF/Bild direkt im iframe
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{doc.OriginalFilename}\"";
        return File(bytes, doc.MimeType ?? "application/octet-stream");
    }

    // ── POST: In MA-Personalakte verschieben (admin/superuser) ────────────
    [HttpPost("{id}/move-to-employee")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> MoveToEmployee(int id, [FromBody] MoveToEmployeeDto dto)
    {
        var doc = await _db.MailboxDocuments.FindAsync(id);
        if (doc is null) return NotFound();

        var emp = await _db.Employees.FindAsync(dto.EmployeeId);
        if (emp is null) return BadRequest(new { error = "Mitarbeiter nicht gefunden." });

        var typ = await _db.DokumentTypen.FindAsync(dto.DokumentTypId);
        if (typ is null) return BadRequest(new { error = "Dokument-Typ nicht gefunden." });

        // Filiale-Code für employee_dokument-Pfad ermitteln
        var company = await _db.CompanyProfiles.FindAsync(doc.CompanyProfileId);
        var branchCode = company?.RestaurantCode ?? "000";

        // Datei aus Mailbox-Storage in employee_dokument-Storage verschieben
        var srcPath = Path.Combine(_storagePath, "mailbox", doc.CompanyProfileId.ToString(), doc.StorageFilename);
        var ext = Path.GetExtension(doc.OriginalFilename);
        if (string.IsNullOrEmpty(ext)) ext = ".pdf";
        var newStorageName = Guid.NewGuid().ToString("N") + ext;
        var destDir = Path.Combine(_storagePath, branchCode, emp.Id.ToString());
        Directory.CreateDirectory(destDir);
        var destPath = Path.Combine(destDir, newStorageName);

        if (System.IO.File.Exists(srcPath))
            System.IO.File.Move(srcPath, destPath);

        var empDoc = new EmployeeDokument
        {
            EmployeeId       = emp.Id,
            DokumentTypId    = typ.Id,
            BranchCode       = branchCode,
            FilenameOriginal = doc.OriginalFilename,
            FilenameStorage  = newStorageName,
            MimeType         = doc.MimeType ?? "application/octet-stream",
            GroesseBytes     = doc.FileSizeBytes ?? 0,
            Bemerkung        = string.IsNullOrWhiteSpace(dto.Bemerkung) ? doc.Bemerkung : dto.Bemerkung,
            HochgeladenVon   = GetCurrentUserId(),
            HochgeladenAm    = DateTime.UtcNow,
        };
        _db.EmployeeDokumente.Add(empDoc);
        _db.MailboxDocuments.Remove(doc);
        await _db.SaveChangesAsync();

        return Ok(new { employeeDokumentId = empDoc.Id });
    }

    // ── DELETE: Dokument verwerfen ────────────────────────────────────────
    // Jeder User, der das Dokument sehen darf, darf es auch löschen.
    // Filial-Postfach: alle mit Filial-Zugriff. HR-Postfach: HR-Team + Admin.
    // Admin-Postfach: nur Admin.
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var doc = await _db.MailboxDocuments.FindAsync(id);
        if (doc is null) return NotFound();
        if (!await UserCanViewDocumentAsync(doc)) return Forbid();

        var path = Path.Combine(_storagePath, "mailbox", doc.CompanyProfileId.ToString(), doc.StorageFilename);
        if (System.IO.File.Exists(path))
            System.IO.File.Delete(path);

        _db.MailboxDocuments.Remove(doc);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private int? GetCurrentUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }

    private bool UserIsAdmin()
        => User.FindFirstValue(ClaimTypes.Role) == "admin";

    /// <summary>
    /// Darf der aktuelle User das Dokument sehen?
    ///   BRANCH → Filial-Zugriff via UserBranchAccess (oder Admin)
    ///   HR     → IsHrTeam (oder Admin)
    ///   ADMIN  → nur Admin
    /// </summary>
    private async Task<bool> UserCanViewDocumentAsync(MailboxDocument doc)
    {
        if (UserIsAdmin()) return true;
        // Sender darf eigene Sendungen immer einsehen — egal in welches
        // Postfach er sie geschickt hat. Wirkt auch für die MA-Outbox auf
        // postfach.html: der MA kann seine eigenen Uploads anschauen, auch
        // wenn er regulär keinen Lese-Zugriff auf das Empfänger-Postfach
        // (Filial-/HR-Posteingang) hätte.
        var uid = GetCurrentUserId();
        if (uid.HasValue && doc.UploadedBy == uid.Value) return true;

        return doc.TargetType switch
        {
            "BRANCH"   => await UserHasBranchAccessAsync(doc.CompanyProfileId),
            "HR"       => await UserCanSeeHrAsync(),
            "ADMIN"    => false, // Admin schon oben gehandhabt
            "EMPLOYEE" => await UserCanSeeEmployeeMailboxAsync(doc),
            _          => false,
        };
    }

    /// <summary>
    /// MA-Postfach sichtbar für:
    ///   • Backoffice-User (admin/superuser/user) mit Branch-Zugang zur
    ///     Filiale des MA — sehen das Postfach im MA-Detail.
    ///   • Der MA selbst (wenn user.EmployeeId = doc.EmployeeId) — sieht
    ///     sein eigenes Postfach beim Login (Phase 2 MA-View).
    /// </summary>
    private async Task<bool> UserCanSeeEmployeeMailboxAsync(MailboxDocument doc)
    {
        var uid = GetCurrentUserId();
        if (uid is null) return false;
        // Der MA selbst sieht sein eigenes Postfach
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == uid);
        if (user?.EmployeeId.HasValue == true && user.EmployeeId == doc.EmployeeId)
            return true;
        // Backoffice mit Branch-Zugang zur Filiale des MA
        return await UserHasBranchAccessAsync(doc.CompanyProfileId);
    }

    /// <summary>
    /// HR-Postfach sichtbar für: Admin oder User mit AppUser.IsHrTeam=true.
    /// (Superuser allein reicht NICHT — Pat ist HR-Team, Senada nicht.)
    /// </summary>
    private async Task<bool> UserCanSeeHrAsync()
    {
        if (UserIsAdmin()) return true;
        var uid = GetCurrentUserId();
        if (uid is null) return false;
        return await _db.AppUsers.AnyAsync(u => u.Id == uid && u.IsHrTeam);
    }

    private async Task<bool> UserHasBranchAccessAsync(int companyProfileId)
    {
        // Admin → alle Filialen
        // Sonst (auch Superuser) → nur was in UserBranchAccess steht
        if (UserIsAdmin()) return true;
        var uid = GetCurrentUserId();
        if (uid is null) return false;
        return await _db.UserBranchAccesses
            .AnyAsync(b => b.UserId == uid && b.CompanyProfileId == companyProfileId);
    }

    private async Task<List<int>> GetUserAllowedBranchIdsAsync()
    {
        // Admin → alle Filialen
        // Sonst (auch Superuser) → nur was in UserBranchAccess steht
        if (UserIsAdmin())
            return await _db.CompanyProfiles.Where(c => c.IsActive).Select(c => c.Id).ToListAsync();
        var uid = GetCurrentUserId();
        if (uid is null) return new List<int>();
        return await _db.UserBranchAccesses
            .Where(b => b.UserId == uid)
            .Select(b => b.CompanyProfileId)
            .ToListAsync();
    }
}

public record MoveToEmployeeDto(int EmployeeId, int DokumentTypId, string? Bemerkung);

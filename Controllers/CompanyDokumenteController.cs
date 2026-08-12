using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using iText.Kernel.Pdf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HrSystem.Controllers;

/// <summary>
/// Filial-Dokumentenverwaltung (Walter-Vorgabe 06.08.2026): Dokumente pro
/// FILIALE — Versicherungspolicen, AHV-Korrespondenz, QST-Unterlagen etc.
/// Hochladen / Ansehen / Drucken / Herunterladen wie bei den MA-Dokumenten
/// (DocumentsController), aber mit eigenem Zugriffs-Guard:
///   • admin → alles.
///   • sonst: AppUser.CanCompanyDokumente == true UND user_branch_access-
///     Eintrag für die betroffene Filiale — sonst 403.
///   • Bewusst KEIN automatischer superuser-/buchhaltung-Zugang: der Zugriff
///     wird pro Benutzer vergeben (Häkchen in der Benutzerverwaltung);
///     superuser-User bekommen das Häkchen bei Bedarf.
/// User-Id kommt IMMER aus dem JWT (ClaimTypes.NameIdentifier), nie aus dem
/// Request. Löschen ist admin-only.
/// </summary>
[Authorize]
[ApiController]
[Route("api/company-dokumente")]
public class CompanyDokumenteController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly string _storagePath;
    private readonly OfficeToPdfService _officePdf;

    /// <summary>
    /// Kategorie-Codes → Labels (fixe Liste, keine Verwaltungstabelle).
    /// Frontend-Pendant: CDOK_KATEGORIEN in wwwroot/js/branches-detail.js —
    /// bei Änderungen BEIDE Stellen pflegen.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Kategorien = new Dictionary<string, string>
    {
        ["VERSICHERUNG"] = "Versicherungen",
        ["AHV_SV"]       = "AHV / Sozialversicherungen",
        ["QST"]          = "Quellensteuer",
        ["VERTRAEGE"]    = "Verträge & Behörden",
        // PDFs dieser Kategorie hängen automatisch am öffentlichen
        // Vertrags-SMS-Link der Filiale (ContractShareController, Walter 10.08.2026).
        ["ONBOARDING"]   = "Onboarding (Vertrags-Link)",
        ["SONSTIGES"]    = "Sonstiges",
    };

    public CompanyDokumenteController(AppDbContext db, IConfiguration config, IWebHostEnvironment env, OfficeToPdfService officePdf)
    {
        _db = db;
        _officePdf = officePdf;
        // Gleiche Storage-Wurzel wie die MA-Dokumente (Documents:StoragePath),
        // Unterordner filiale/{companyProfileId}/ — siehe DocumentsController.
        var configured = config["Documents:StoragePath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine(env.ContentRootPath, "data", "documents");
        _storagePath = configured;
        Directory.CreateDirectory(_storagePath);
    }

    // ──────────────────────────────────────────────────────────────────────
    // ZUGRIFFS-GUARD
    // ──────────────────────────────────────────────────────────────────────

    private int? GetCurrentUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idStr, out var uid) ? uid : null;
    }

    /// <summary>
    /// admin → immer. Sonst: CanCompanyDokumente-Häkchen UND Filial-Zugang
    /// (user_branch_access) für genau diese Filiale.
    /// </summary>
    private async Task<bool> HasAccessAsync(int companyProfileId)
    {
        if (User.IsInRole("admin")) return true;
        var uid = GetCurrentUserId();
        if (uid == null) return false;
        var can = await _db.AppUsers
            .Where(u => u.Id == uid.Value && u.IsActive)
            .Select(u => u.CanCompanyDokumente)
            .FirstOrDefaultAsync();
        if (!can) return false;
        return await _db.UserBranchAccesses
            .AnyAsync(a => a.UserId == uid.Value && a.CompanyProfileId == companyProfileId);
    }

    private ObjectResult Verboten() => StatusCode(403, new
    {
        error = "KEIN_ZUGRIFF",
        message = "Kein Zugriff auf die Filial-Dokumente dieser Filiale. "
                + "Das Häkchen «Zugriff Filial-Dokumente» wird in der Benutzerverwaltung vergeben."
    });

    /// <summary>Anzeigename des eingeloggten Users (Vor+Nachname, Fallback Username).</summary>
    private async Task<string?> GetActorNameAsync()
    {
        var uid = GetCurrentUserId();
        if (uid != null)
        {
            var u = await _db.AppUsers.Where(x => x.Id == uid.Value)
                .Select(x => new { x.FirstName, x.LastName, x.Username })
                .FirstOrDefaultAsync();
            if (u != null)
            {
                var full = $"{u.FirstName} {u.LastName}".Trim();
                return string.IsNullOrWhiteSpace(full) ? u.Username : full;
            }
        }
        return User.FindFirstValue(ClaimTypes.Name);
    }

    // ──────────────────────────────────────────────────────────────────────
    // LISTE
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] int companyProfileId)
    {
        if (companyProfileId <= 0) return BadRequest(new { error = "companyProfileId fehlt." });
        if (!await HasAccessAsync(companyProfileId)) return Verboten();

        var docs = await _db.CompanyDokumente.AsNoTracking()
            .Where(d => d.CompanyProfileId == companyProfileId)
            .OrderBy(d => d.Kategorie)
            .ThenByDescending(d => d.CreatedAt)
            .Select(d => new
            {
                d.Id,
                d.CompanyProfileId,
                d.Kategorie,
                d.OriginalFilename,
                d.Bemerkung,
                d.UploadedByName,
                d.CreatedAt,
                d.ZugriffAm,
                d.ZugriffVon
            })
            .ToListAsync();

        return Ok(docs);
    }

    // ──────────────────────────────────────────────────────────────────────
    // UPLOAD
    // ──────────────────────────────────────────────────────────────────────

    [HttpPost("upload")]
    [RequestSizeLimit(20 * 1024 * 1024)] // 20 MB pro Datei
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        [FromForm] int companyProfileId,
        [FromForm] string kategorie,
        [FromForm] string? bemerkung)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });
        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { error = "Datei zu gross (max. 20 MB)." });
        if (companyProfileId <= 0)
            return BadRequest(new { error = "companyProfileId fehlt." });

        var kat = (kategorie ?? "").Trim().ToUpperInvariant();
        if (!Kategorien.ContainsKey(kat))
            return BadRequest(new { error = $"Unbekannte Kategorie «{kategorie}». Erlaubt: {string.Join(", ", Kategorien.Keys)}" });

        if (!await HasAccessAsync(companyProfileId)) return Verboten();

        var branchExists = await _db.CompanyProfiles.AnyAsync(c => c.Id == companyProfileId);
        if (!branchExists) return BadRequest(new { error = "Filiale nicht gefunden." });

        // Filename sanitization (Muster MailboxController.MaUpload): nur die
        // Extension behalten, Rest neu generieren — Path-Traversal unmöglich.
        var ext = (Path.GetExtension(file.FileName) ?? "").ToLowerInvariant();
        if (ext.Length > 8 || ext.Contains("/") || ext.Contains("\\"))
            ext = ".bin";
        var storageName = Guid.NewGuid().ToString("N") + ext;

        // Storage-Pfad: {storage}/filiale/{companyProfileId}/
        var dir = Path.Combine(_storagePath, "filiale", companyProfileId.ToString());
        Directory.CreateDirectory(dir);
        var fullPath = Path.Combine(dir, storageName);

        await using (var stream = System.IO.File.Create(fullPath))
            await file.CopyToAsync(stream);

        // Originalfilename säubern (max 200 Zeichen, ohne Pfad)
        var origName = Path.GetFileName(file.FileName ?? "upload");
        if (origName.Length > 200) origName = origName.Substring(0, 200);

        var doc = new CompanyDokument
        {
            CompanyProfileId = companyProfileId,
            Kategorie        = kat,
            OriginalFilename = origName,
            StorageFilename  = storageName,
            Bemerkung        = string.IsNullOrWhiteSpace(bemerkung) ? null : bemerkung.Trim(),
            UploadedByName   = await GetActorNameAsync(),
            CreatedAt        = DateTime.Now
        };
        _db.CompanyDokumente.Add(doc);
        await _db.SaveChangesAsync();

        return Ok(new { doc.Id, doc.Kategorie, doc.OriginalFilename, doc.CreatedAt, doc.UploadedByName });
    }

    // ──────────────────────────────────────────────────────────────────────
    // PREVIEW / DOWNLOAD (Muster DocumentsController)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Datei inline anzeigen (PDF-/Bild-Vorschau im Browser).</summary>
    [HttpGet("{id:long}/preview")]
    public async Task<IActionResult> Preview(long id) => await ServeFile(id, asAttachment: false);

    /// <summary>Datei herunterladen (Content-Disposition: attachment, Original).</summary>
    [HttpGet("{id:long}/download")]
    public async Task<IActionResult> Download(long id) => await ServeFile(id, asAttachment: true);

    /// <summary>
    /// Vorschau als PDF (analog DocumentsController.PreviewPdf): PDFs inline
    /// as-is; Office-Dokumente via LibreOffice serverseitig nach PDF gewandelt;
    /// andere Typen → 415.
    /// </summary>
    [HttpGet("{id:long}/preview-pdf")]
    public async Task<IActionResult> PreviewPdf(long id)
    {
        var doc = await _db.CompanyDokumente.FindAsync(id);
        if (doc is null) return NotFound();
        if (!await HasAccessAsync(doc.CompanyProfileId)) return Verboten();

        var fullPath = ResolveFilePath(doc);
        if (fullPath is null) return NotFound("Datei nicht im Storage gefunden.");

        await TouchZugriffAsync(doc);   // Zugriffsdatum + wer (best-effort)

        var ext = Path.GetExtension(doc.OriginalFilename ?? "").ToLowerInvariant();

        if (ext == ".pdf")
        {
            var stream = System.IO.File.OpenRead(fullPath);
            Response.Headers["Content-Disposition"] =
                ContentDispositionUtil.Build("inline", doc.OriginalFilename, "dokument.pdf");
            return File(stream, "application/pdf");
        }

        if (OfficeToPdfService.CanConvert(doc.OriginalFilename))
        {
            byte[] input;
            try { input = await System.IO.File.ReadAllBytesAsync(fullPath); }
            catch { return NotFound("Datei konnte nicht gelesen werden."); }

            var pdf = await _officePdf.ConvertToPdfAsync(input, doc.OriginalFilename ?? ("datei" + ext));
            if (pdf is null)
                return StatusCode(500, new { error = "PDF-Konvertierung fehlgeschlagen. Ist LibreOffice auf dem Server installiert?" });

            var name = Path.GetFileNameWithoutExtension(doc.OriginalFilename ?? "dokument") + ".pdf";
            Response.Headers["Content-Disposition"] = ContentDispositionUtil.Build("inline", name, "dokument.pdf");
            return File(pdf, "application/pdf");
        }

        return StatusCode(415, new { error = "Für diesen Dateityp ist keine PDF-Vorschau möglich." });
    }

    // ──────────────────────────────────────────────────────────────────────
    // PDF DREHEN (iText-Muster aus DocumentsController — nur echte PDFs)
    // ──────────────────────────────────────────────────────────────────────

    [HttpPost("{id:long}/rotate")]
    public async Task<IActionResult> Rotate(long id, [FromQuery] int deg = 90)
    {
        var doc = await _db.CompanyDokumente.FindAsync(id);
        if (doc is null) return NotFound();
        if (!await HasAccessAsync(doc.CompanyProfileId)) return Verboten();

        var ext = Path.GetExtension(doc.OriginalFilename ?? "").ToLowerInvariant();
        if (ext != ".pdf")
            return BadRequest(new { error = "Drehen ist nur für PDF-Dateien möglich." });

        var fullPath = ResolveFilePath(doc);
        if (fullPath is null) return NotFound("Datei nicht im Storage gefunden.");

        // deg auf 0/90/180/270 normalisieren (negativ erlaubt: -90 = 270).
        int delta = ((deg % 360) + 360) % 360;
        if (delta == 0) return Ok(new { ok = true, unchanged = true });

        try
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
            byte[] outBytes;
            using (var reader = new PdfReader(new MemoryStream(bytes)))
            using (var ms = new MemoryStream())
            {
                using (var writer = new PdfWriter(ms))
                using (var pdf = new PdfDocument(reader, writer))
                {
                    int n = pdf.GetNumberOfPages();
                    for (int i = 1; i <= n; i++)
                    {
                        var pg = pdf.GetPage(i);
                        int cur = pg.GetRotation();
                        pg.SetRotation(((cur + delta) % 360 + 360) % 360);
                    }
                }
                outBytes = ms.ToArray();
            }
            await System.IO.File.WriteAllBytesAsync(fullPath, outBytes);
            return Ok(new { ok = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Drehen fehlgeschlagen: " + ex.Message });
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // LÖSCHEN — nur admin (Datei + DB-Zeile)
    // ──────────────────────────────────────────────────────────────────────

    [HttpDelete("{id:long}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(long id)
    {
        var doc = await _db.CompanyDokumente.FindAsync(id);
        if (doc is null) return NotFound();

        var fullPath = ResolveFilePath(doc);
        try
        {
            if (fullPath != null && System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }
        catch { /* Datei-Löschung best-effort — DB-Zeile geht trotzdem weg */ }

        _db.CompanyDokumente.Remove(doc);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ──────────────────────────────────────────────────────────────────────
    // HELFER
    // ──────────────────────────────────────────────────────────────────────

    private async Task<IActionResult> ServeFile(long id, bool asAttachment)
    {
        var doc = await _db.CompanyDokumente.FindAsync(id);
        if (doc is null) return NotFound();
        if (!await HasAccessAsync(doc.CompanyProfileId)) return Verboten();

        var fullPath = ResolveFilePath(doc);
        if (fullPath is null) return NotFound("Datei nicht im Storage gefunden.");

        await TouchZugriffAsync(doc);   // Zugriffsdatum + wer (best-effort)

        var stream = System.IO.File.OpenRead(fullPath);
        var contentDisposition = asAttachment ? "attachment" : "inline";
        Response.Headers["Content-Disposition"] =
            ContentDispositionUtil.Build(contentDisposition, doc.OriginalFilename, "dokument");
        return File(stream, GuessMime(doc.OriginalFilename));
    }

    private string? ResolveFilePath(CompanyDokument doc)
    {
        var p = Path.Combine(_storagePath, "filiale", doc.CompanyProfileId.ToString(), doc.StorageFilename);
        return System.IO.File.Exists(p) ? p : null;
    }

    /// <summary>Setzt Zugriffsdatum + Zugriff-von. Best-effort — Fehler nie weiterreichen.</summary>
    private async Task TouchZugriffAsync(CompanyDokument doc)
    {
        try
        {
            doc.ZugriffAm  = DateTime.Now;   // Lokalzeit (timestamp without time zone)
            doc.ZugriffVon = await GetActorNameAsync();
            await _db.SaveChangesAsync();
        }
        catch { /* Zugriffs-Stempel ist nicht kritisch */ }
    }

    /// <summary>
    /// MIME aus der Datei-Endung (CompanyDokument speichert kein MimeType-Feld —
    /// die Endung des Originalnamens ist die verlässlichere Quelle als der vom
    /// Browser gemeldete Content-Type).
    /// </summary>
    private static string GuessMime(string filename)
    {
        var ext = (Path.GetExtension(filename) ?? "").ToLowerInvariant();
        return ext switch
        {
            ".pdf"            => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"            => "image/png",
            ".gif"            => "image/gif",
            ".tif" or ".tiff" => "image/tiff",
            ".doc"            => "application/msword",
            ".docx"           => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls"            => "application/vnd.ms-excel",
            ".xlsx"           => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt"            => "application/vnd.ms-powerpoint",
            ".pptx"           => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".odt"            => "application/vnd.oasis.opendocument.text",
            ".ods"            => "application/vnd.oasis.opendocument.spreadsheet",
            ".odp"            => "application/vnd.oasis.opendocument.presentation",
            ".rtf"            => "application/rtf",
            ".csv"            => "text/csv",
            ".txt"            => "text/plain",
            ".xml"            => "application/xml",
            _                 => "application/octet-stream",
        };
    }
}

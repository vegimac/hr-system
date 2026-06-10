using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using iText.Kernel.Pdf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HrSystem.Controllers;

[Authorize]
[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly string _storagePath;
    private readonly OfficeToPdfService _officePdf;

    /// <summary>
    /// Storage-Pfad wird aus appsettings.json (Documents:StoragePath) gelesen.
    /// Default: "data/documents" relativ zum Content-Root.
    /// Auf dem Server via systemd-Environment "Documents__StoragePath=/var/data/hr-system/documents".
    /// </summary>
    public DocumentsController(AppDbContext db, IConfiguration config, IWebHostEnvironment env, OfficeToPdfService officePdf)
    {
        _db = db;
        _officePdf = officePdf;
        var configured = config["Documents:StoragePath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine(env.ContentRootPath, "data", "documents");
        _storagePath = configured;
        Directory.CreateDirectory(_storagePath);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TAXONOMIE: Kategorien + Typen
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Liefert die komplette Taxonomie (Kategorien mit verschachtelten Typen).
    /// Wird vom Frontend einmal beim Öffnen des Dokumente-Tabs geladen.
    /// </summary>
    [HttpGet("taxonomie")]
    public async Task<IActionResult> GetTaxonomie()
    {
        var kategorien = await _db.DokumentKategorien
            .Where(k => k.Aktiv)
            .OrderBy(k => k.SortOrder).ThenBy(k => k.Name)
            .ToListAsync();

        var typen = await _db.DokumentTypen
            .Where(t => t.Aktiv)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync();

        var result = kategorien.Select(k => new {
            k.Id,
            k.Name,
            k.SortOrder,
            typen = typen.Where(t => t.KategorieId == k.Id)
                         .Select(t => new { t.Id, t.Name, t.SortOrder, t.LinkedFieldCode })
                         .ToList()
        });

        return Ok(result);
    }

    /// <summary>
    /// Liefert die Liste aller Field-Codes, für die ein MA mindestens ein
    /// Dokument hat. Wird beim Rendern der MA-Detail-Maske geladen, um zu
    /// entscheiden welche Stammdaten-Felder den 📎-Button bekommen.
    /// </summary>
    [HttpGet("linked-codes-for-employee")]
    public async Task<IActionResult> GetLinkedCodesForEmployee([FromQuery] int employeeId)
    {
        var codes = await _db.EmployeeDokumente
            .Where(d => d.EmployeeId == employeeId)
            .Join(_db.DokumentTypen.Where(t => t.LinkedFieldCode != null && t.Aktiv),
                  d => d.DokumentTypId, t => t.Id, (d, t) => t.LinkedFieldCode!)
            .Distinct()
            .ToListAsync();
        return Ok(codes);
    }

    /// <summary>
    /// Findet das neueste Dokument eines MA für einen bestimmten Field-Code
    /// (permit, passport, ahv_card, bank_card, etc.). Wird von der MA-Detail-
    /// Maske genutzt, um neben Stammdaten-Feldern den 📎-Button zu zeigen.
    /// </summary>
    [HttpGet("by-field")]
    public async Task<IActionResult> GetByField(
        [FromQuery] int employeeId,
        [FromQuery] string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest("Field-Code fehlt.");

        var doc = await _db.EmployeeDokumente
            .Where(d => d.EmployeeId == employeeId
                     && _db.DokumentTypen
                          .Where(t => t.LinkedFieldCode == code && t.Aktiv)
                          .Select(t => t.Id)
                          .Contains(d.DokumentTypId))
            .OrderByDescending(d => d.HochgeladenAm)
            .Select(d => new {
                d.Id,
                d.FilenameOriginal,
                d.MimeType,
                d.GroesseBytes,
                d.HochgeladenAm,
                d.Bemerkung,
                d.GueltigVon,
                d.GueltigBis
            })
            .FirstOrDefaultAsync();

        if (doc == null) return NotFound();
        return Ok(doc);
    }

    // ──────────────────────────────────────────────────────────────────────
    // LISTE pro MITARBEITER
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Alle Dokumente eines Mitarbeiters (mit Typ + Kategorie für Anzeige).
    /// </summary>
    [HttpGet("by-employee/{employeeId:int}")]
    public async Task<IActionResult> GetByEmployee(int employeeId)
    {
        var docs = await (
            from d in _db.EmployeeDokumente
            join t in _db.DokumentTypen on d.DokumentTypId equals t.Id
            join k in _db.DokumentKategorien on t.KategorieId equals k.Id
            where d.EmployeeId == employeeId
            orderby d.HochgeladenAm descending
            select new {
                d.Id,
                d.EmployeeId,
                dokumentTypId   = t.Id,
                dokumentTypName = t.Name,
                kategorieId     = k.Id,
                kategorieName   = k.Name,
                d.FilenameOriginal,
                d.MimeType,
                d.GroesseBytes,
                d.Bemerkung,
                d.GueltigVon,
                d.GueltigBis,
                d.HochgeladenAm,
                d.HochgeladenVon,
                d.ErstelltAm,
                d.GeaendertAm,
                d.DateiGeaendertAm,
                d.ZugriffAm,
                d.GeaendertVon,
                d.ZugriffVon,
                d.DvelopDokumentId
            }
        ).ToListAsync();

        return Ok(docs);
    }

    // ──────────────────────────────────────────────────────────────────────
    // UPLOAD
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dokument hochladen (multipart/form-data).
    /// Felder: file, employeeId, dokumentTypId, branchCode, bemerkung, gueltigVon, gueltigBis
    /// branchCode kommt aus der vom User gewählten Filiale (selectedCompanyProfile.restaurantCode)
    /// und wird zur Strukturierung des Storage-Pfads verwendet.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB pro Datei
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        [FromForm] int employeeId,
        [FromForm] int dokumentTypId,
        [FromForm] string? branchCode,
        [FromForm] string? bemerkung,
        [FromForm] DateOnly? gueltigVon,
        [FromForm] DateOnly? gueltigBis,
        // Walter-Vorgabe 06.06.2026: optionale d.velop-Metadaten — für den
        // Schnell-Upload aus der „fehlende Dokumente"-Liste. Frontend sendet
        // diese Felder als ISO-Strings; alle Sentinel-Werte (null/"") werden
        // ignoriert und der Default greift (HochgeladenAm = jetzt).
        [FromForm] DateTime? erstelltAm = null,
        [FromForm] DateTime? geaendertAm = null,
        [FromForm] DateTime? dateiGeaendertAm = null,
        [FromForm] DateTime? zugriffAm = null,
        [FromForm] string? geaendertVon = null,
        [FromForm] string? dvelopDokumentId = null)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Keine Datei hochgeladen.");
        if (string.IsNullOrWhiteSpace(branchCode))
            return BadRequest("Filiale-Code fehlt. Bitte zuerst eine Filiale wählen.");

        // Datei-Endungs-Whitelist (Walter-Vorgabe 09.06.2026): vorher nahm der
        // Endpunkt JEDE Endung an — inkl. .exe/.bat/.html/.js. Jetzt nur explizit
        // erlaubte HR-/Office-Typen. Endung wird gegen lowercase geprüft.
        var allowedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".pdf",
            ".jpg", ".jpeg", ".png", ".gif", ".tif", ".tiff",
            ".doc", ".docx",
            ".xls", ".xlsx",
            ".ppt", ".pptx",
            ".odt", ".ods", ".odp", ".rtf",
            ".csv", ".txt"
        };
        var uploadExt = (Path.GetExtension(file.FileName) ?? "").ToLowerInvariant();
        if (!allowedExt.Contains(uploadExt))
            return BadRequest(new {
                error = $"Dateityp '{uploadExt}' nicht erlaubt. Zugelassen: "
                      + string.Join(", ", allowedExt.OrderBy(x => x))
            });

        // Mitarbeiter + Typ existieren?
        var empExists = await _db.Employees.AnyAsync(e => e.Id == employeeId);
        if (!empExists) return BadRequest("Mitarbeiter nicht gefunden.");
        var typExists = await _db.DokumentTypen.AnyAsync(t => t.Id == dokumentTypId);
        if (!typExists) return BadRequest("Dokument-Typ nicht gefunden.");

        // Duplikat-Check: gleicher Mitarbeiter + gleicher Original-Dateiname
        var duplicate = await _db.EmployeeDokumente
            .Where(d => d.EmployeeId == employeeId && d.FilenameOriginal == file.FileName)
            .Select(d => new { d.Id, d.HochgeladenAm })
            .FirstOrDefaultAsync();
        if (duplicate != null)
        {
            return Conflict(new {
                message    = "Dokument mit diesem Dateinamen ist für diesen Mitarbeiter bereits vorhanden.",
                duplicateId = duplicate.Id,
                filename    = file.FileName,
                hochgeladenAm = duplicate.HochgeladenAm
            });
        }

        // Branch-Code säubern (nur sichere Zeichen für Pfad: Buchstaben, Zahlen, _, -)
        var safeBranchCode = SanitizePathSegment(branchCode);

        // Storage-Pfad: {storage}/{branch_code}/{employee_id}/
        var empDir = Path.Combine(_storagePath, safeBranchCode, employeeId.ToString());
        Directory.CreateDirectory(empDir);

        // UUID-basierter Dateiname (Original-Extension behalten)
        var ext = Path.GetExtension(file.FileName);
        var storageName = Guid.NewGuid().ToString("N") + ext;
        var fullPath = Path.Combine(empDir, storageName);

        // Datei schreiben
        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream);
        }

        var doc = new EmployeeDokument {
            EmployeeId       = employeeId,
            DokumentTypId    = dokumentTypId,
            BranchCode       = safeBranchCode,
            FilenameOriginal = file.FileName,
            FilenameStorage  = storageName,
            MimeType         = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
            GroesseBytes     = file.Length,
            Bemerkung        = string.IsNullOrWhiteSpace(bemerkung) ? null : bemerkung.Trim(),
            GueltigVon       = gueltigVon,
            GueltigBis       = gueltigBis,
            HochgeladenVon   = GetCurrentUserId(),
            HochgeladenAm    = DateTime.UtcNow,
            // Optionale d.velop-Metadaten (Walter 06.06.2026) — Kind=Unspecified
            // damit Postgres `timestamp without time zone` (siehe DbContext-Mapping
            // in der Backfill-Migration) sie ohne UTC-Konvertierung übernimmt.
            ErstelltAm       = erstelltAm.HasValue       ? DateTime.SpecifyKind(erstelltAm.Value,       DateTimeKind.Unspecified) : (DateTime?)null,
            GeaendertAm      = geaendertAm.HasValue      ? DateTime.SpecifyKind(geaendertAm.Value,      DateTimeKind.Unspecified) : (DateTime?)null,
            DateiGeaendertAm = dateiGeaendertAm.HasValue ? DateTime.SpecifyKind(dateiGeaendertAm.Value, DateTimeKind.Unspecified) : (DateTime?)null,
            ZugriffAm        = zugriffAm.HasValue        ? DateTime.SpecifyKind(zugriffAm.Value,        DateTimeKind.Unspecified) : (DateTime?)null,
            GeaendertVon     = string.IsNullOrWhiteSpace(geaendertVon) ? null : geaendertVon.Trim(),
            DvelopDokumentId = string.IsNullOrWhiteSpace(dvelopDokumentId) ? null : dvelopDokumentId.Trim()
        };
        _db.EmployeeDokumente.Add(doc);
        await _db.SaveChangesAsync();

        return Ok(new { doc.Id, doc.FilenameOriginal, doc.GroesseBytes, doc.HochgeladenAm });
    }

    /// <summary>
    /// Filtert Pfad-gefährliche Zeichen aus dem Branch-Code (verhindert
    /// Path-Traversal via "../" oder absolute Pfade).
    /// </summary>
    private static string SanitizePathSegment(string s)
    {
        var clean = new string(s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
        return string.IsNullOrEmpty(clean) ? "_unknown" : clean;
    }

    // ──────────────────────────────────────────────────────────────────────
    // DUPLIKAT-CHECK (für Massen-Import)
    // ──────────────────────────────────────────────────────────────────────

    public class CheckDuplicatesDto
    {
        public int EmployeeId { get; set; }
        public List<string> Filenames { get; set; } = new();
    }

    /// <summary>
    /// Prüft, welche Dateinamen für einen Mitarbeiter bereits existieren.
    /// Wird vom Massen-Import-Frontend vor dem Hochladen aufgerufen, damit
    /// Duplikate in der Review-Tabelle markiert werden können.
    /// </summary>
    [HttpPost("check-duplicates")]
    public async Task<IActionResult> CheckDuplicates([FromBody] CheckDuplicatesDto dto)
    {
        if (dto.Filenames.Count == 0) return Ok(new List<object>());

        var existing = await _db.EmployeeDokumente
            .Where(d => d.EmployeeId == dto.EmployeeId && dto.Filenames.Contains(d.FilenameOriginal))
            .Select(d => new {
                d.Id,
                filename = d.FilenameOriginal,
                d.HochgeladenAm
            })
            .ToListAsync();

        return Ok(existing);
    }

    // ──────────────────────────────────────────────────────────────────────
    // DOWNLOAD / PREVIEW
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Datei herunterladen (Content-Disposition: attachment).
    /// Nur Admin/Superuser — normale Benutzer können die Vorschau nutzen,
    /// aber keine Datei lokal speichern (Missbrauchsschutz).
    /// </summary>
    [HttpGet("download/{id:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Download(int id) => await ServeFile(id, asAttachment: true);

    /// <summary>Datei inline anzeigen (für PDF-Vorschau im Browser).</summary>
    [HttpGet("preview/{id:int}")]
    public async Task<IActionResult> Preview(int id) => await ServeFile(id, asAttachment: false);

    /// <summary>
    /// Vorschau als PDF (Walter-Vorgabe 24.05.2026): PDFs werden direkt inline
    /// ausgeliefert, Word/Office-Dokumente (.doc/.docx/.xls/.xlsx/.ppt/.pptx/
    /// .odt/.ods/.odp/.rtf) via LibreOffice serverseitig nach PDF gewandelt und
    /// inline geliefert — damit sie im Vorschaufenster der Dokumentenverwaltung
    /// angezeigt werden können (der Browser kann Word nicht direkt rendern).
    /// Andere Typen → 415 (Frontend zeigt „keine Vorschau möglich").
    /// </summary>
    [HttpGet("preview-pdf/{id:int}")]
    public async Task<IActionResult> PreviewPdf(int id)
    {
        var doc = await _db.EmployeeDokumente.FindAsync(id);
        if (doc is null) return NotFound();

        var fullPath = ResolveFilePath(doc);
        if (fullPath is null) return NotFound("Datei nicht im Storage gefunden.");

        await TouchZugriffAsync(doc);   // Zugriffsdatum + wer (best-effort)

        var ext = Path.GetExtension(doc.FilenameOriginal ?? "").ToLowerInvariant();

        // Schon PDF → unverändert inline ausliefern.
        if (ext == ".pdf" || doc.MimeType == "application/pdf")
        {
            var stream = System.IO.File.OpenRead(fullPath);
            Response.Headers["Content-Disposition"] =
                $"inline; filename=\"{Uri.EscapeDataString(doc.FilenameOriginal ?? "dokument.pdf")}\"";
            return File(stream, "application/pdf");
        }

        // Office-Dokument → via LibreOffice nach PDF wandeln.
        if (OfficeToPdfService.CanConvert(doc.FilenameOriginal))
        {
            byte[] input;
            try { input = await System.IO.File.ReadAllBytesAsync(fullPath); }
            catch { return NotFound("Datei konnte nicht gelesen werden."); }

            var pdf = await _officePdf.ConvertToPdfAsync(input, doc.FilenameOriginal ?? ("datei" + ext));
            if (pdf is null)
                return StatusCode(500, new { error = "PDF-Konvertierung fehlgeschlagen. Ist LibreOffice auf dem Server installiert?" });

            var name = Path.GetFileNameWithoutExtension(doc.FilenameOriginal ?? "dokument") + ".pdf";
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{Uri.EscapeDataString(name)}\"";
            return File(pdf, "application/pdf");
        }

        // Nicht konvertierbar (z.B. ZIP) → kein PDF möglich.
        return StatusCode(415, new { error = "Für diesen Dateityp ist keine PDF-Vorschau möglich." });
    }

    /// <summary>
    /// Dreht ein PDF in 90°-Schritten (deg = 90 / 180 / 270 / -90) und speichert
    /// die gedrehte Datei zurück (Walter-Vorgabe 24.05.2026). Setzt dabei
    /// datei_geaendert_am + geaendert_von. Nur für echte PDF-Dokumente.
    /// </summary>
    [HttpPost("{id:int}/rotate")]
    public async Task<IActionResult> Rotate(int id, [FromQuery] int deg = 90, [FromQuery] int page = 0)
    {
        var doc = await _db.EmployeeDokumente.FindAsync(id);
        if (doc is null) return NotFound();

        var ext = Path.GetExtension(doc.FilenameOriginal ?? "").ToLowerInvariant();
        if (ext != ".pdf" && doc.MimeType != "application/pdf")
            return BadRequest(new { error = "Drehen ist nur fuer PDF-Dateien moeglich." });

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
                    // page > 0 → nur diese eine Seite drehen; sonst alle Seiten.
                    if (page > 0 && page > n)
                        return BadRequest(new { error = $"Seite {page} existiert nicht (Dokument hat {n} Seiten)." });
                    int from = page > 0 ? page : 1;
                    int to   = page > 0 ? page : n;
                    for (int i = from; i <= to; i++)
                    {
                        var pg = pdf.GetPage(i);
                        int cur = pg.GetRotation();
                        pg.SetRotation(((cur + delta) % 360 + 360) % 360);
                    }
                }
                outBytes = ms.ToArray();
            }
            await System.IO.File.WriteAllBytesAsync(fullPath, outBytes);

            doc.DateiGeaendertAm = DateTime.Now;
            doc.GeaendertVon = await GetActorNameAsync();
            await _db.SaveChangesAsync();

            return Ok(new { ok = true, doc.DateiGeaendertAm, doc.GeaendertVon });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Drehen fehlgeschlagen: " + ex.Message });
        }
    }

    private async Task<IActionResult> ServeFile(int id, bool asAttachment)
    {
        var doc = await _db.EmployeeDokumente.FindAsync(id);
        if (doc is null) return NotFound();

        var fullPath = ResolveFilePath(doc);
        if (fullPath is null)
            return NotFound("Datei nicht im Storage gefunden.");

        await TouchZugriffAsync(doc);   // Zugriffsdatum + wer (best-effort)

        var stream = System.IO.File.OpenRead(fullPath);
        var contentDisposition = asAttachment ? "attachment" : "inline";
        Response.Headers["Content-Disposition"] =
            $"{contentDisposition}; filename=\"{Uri.EscapeDataString(doc.FilenameOriginal)}\"";
        return File(stream, doc.MimeType);
    }

    /// <summary>Anzeigename des eingeloggten Users (Vor+Nachname, Fallback Username).</summary>
    private async Task<string?> GetActorNameAsync()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(idStr, out var uid))
        {
            var u = await _db.AppUsers.Where(x => x.Id == uid)
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

    /// <summary>Setzt Zugriffsdatum + Zugriff-von. Best-effort — Fehler nie weiterreichen.</summary>
    private async Task TouchZugriffAsync(EmployeeDokument doc)
    {
        try
        {
            doc.ZugriffAm  = DateTime.Now;
            doc.ZugriffVon = await GetActorNameAsync();
            await _db.SaveChangesAsync();
        }
        catch { /* Zugriffs-Stempel ist nicht kritisch */ }
    }

    /// <summary>
    /// Findet die Datei. Priorität: neuer Pfad mit branch_code → alter Pfad ohne.
    /// Liefert null, wenn keine Variante existiert.
    /// </summary>
    private string? ResolveFilePath(EmployeeDokument doc)
    {
        if (!string.IsNullOrEmpty(doc.BranchCode))
        {
            var withBranch = Path.Combine(_storagePath, doc.BranchCode, doc.EmployeeId.ToString(), doc.FilenameStorage);
            if (System.IO.File.Exists(withBranch)) return withBranch;
        }
        // Fallback: alter Pfad (vor Branch-Migration)
        var legacy = Path.Combine(_storagePath, doc.EmployeeId.ToString(), doc.FilenameStorage);
        if (System.IO.File.Exists(legacy)) return legacy;
        return null;
    }

    // ──────────────────────────────────────────────────────────────────────
    // METADATEN AKTUALISIEREN
    // ──────────────────────────────────────────────────────────────────────

    public class UpdateDocDto
    {
        public int? DokumentTypId { get; set; }
        public string? Bemerkung { get; set; }
        public DateOnly? GueltigVon { get; set; }
        public DateOnly? GueltigBis { get; set; }
        /// <summary>
        /// Optional: Dokument einem anderen MA zuweisen. Beim Wechsel wird die
        /// Datei auch physisch in den neuen MA-Ordner verschoben. Filiale
        /// (BranchCode) wird automatisch nachgezogen wenn der neue MA in einer
        /// anderen Filiale ist (erster aktiver Vertrag).
        /// </summary>
        public int? EmployeeId { get; set; }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateDocDto dto)
    {
        var doc = await _db.EmployeeDokumente.FindAsync(id);
        if (doc is null) return NotFound();

        if (dto.DokumentTypId.HasValue)
        {
            var typExists = await _db.DokumentTypen.AnyAsync(t => t.Id == dto.DokumentTypId.Value);
            if (!typExists) return BadRequest("Dokument-Typ nicht gefunden.");
            doc.DokumentTypId = dto.DokumentTypId.Value;
        }

        // MA-Reassignment: nur wenn EmployeeId gesetzt UND anders als aktuell.
        // Datei wird physisch verschoben, BranchCode wird automatisch
        // angepasst (neuer MA → erster aktiver Vertrag → Filiale).
        if (dto.EmployeeId.HasValue && dto.EmployeeId.Value != doc.EmployeeId)
        {
            var newEmp = await _db.Employees
                .Include(e => e.Employments)
                .ThenInclude(em => em.CompanyProfile)
                .FirstOrDefaultAsync(e => e.Id == dto.EmployeeId.Value);
            if (newEmp == null) return BadRequest("Ziel-Mitarbeiter nicht gefunden.");

            var oldPath = ResolveFilePath(doc);
            var newBranchCode = newEmp.Employments
                .Where(em => em.IsActive && em.CompanyProfile != null)
                .Select(em => em.CompanyProfile!.RestaurantCode)
                .FirstOrDefault()
                ?? doc.BranchCode;   // Fallback: alten BranchCode behalten

            // Neuen Pfad bauen + Ordner sicherstellen
            var newDir = string.IsNullOrEmpty(newBranchCode)
                ? Path.Combine(_storagePath, newEmp.Id.ToString())
                : Path.Combine(_storagePath, newBranchCode, newEmp.Id.ToString());
            Directory.CreateDirectory(newDir);
            var newPath = Path.Combine(newDir, doc.FilenameStorage);

            // Datei physisch verschieben (wenn vorhanden)
            try
            {
                if (oldPath != null && System.IO.File.Exists(oldPath))
                {
                    if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (System.IO.File.Exists(newPath))
                            System.IO.File.Delete(newPath);
                        System.IO.File.Move(oldPath, newPath);
                    }
                }
            }
            catch (Exception ex)
            {
                return BadRequest($"Datei konnte nicht verschoben werden: {ex.Message}");
            }

            doc.EmployeeId = newEmp.Id;
            doc.BranchCode = newBranchCode;
        }

        doc.Bemerkung  = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        doc.GueltigVon = dto.GueltigVon;
        doc.GueltigBis = dto.GueltigBis;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ──────────────────────────────────────────────────────────────────────
    // LÖSCHEN
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dokument löschen — nur Admin/Superuser. Verhindert versehentlichen
    /// Datenverlust durch normale Benutzer.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Delete(int id)
    {
        var doc = await _db.EmployeeDokumente.FindAsync(id);
        if (doc is null) return NotFound();

        var fullPath = ResolveFilePath(doc);
        try
        {
            if (fullPath != null && System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }
        catch (Exception ex)
        {
            // DB-Eintrag trotzdem löschen, Datei-Reste sind kein Showstopper
            Console.Error.WriteLine($"Datei-Löschung fehlgeschlagen für Doc {id}: {ex.Message}");
        }

        _db.EmployeeDokumente.Remove(doc);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ──────────────────────────────────────────────────────────────────────
    // ABLAUFENDE DOKUMENTE (Bonus)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dokumente, die in den nächsten N Tagen ablaufen oder bereits abgelaufen sind.
    /// Für Dashboard-Erinnerungen (z.B. Aufenthaltsbewilligung).
    /// </summary>
    [HttpGet("expiring")]
    public async Task<IActionResult> Expiring([FromQuery] int withinDays = 30)
    {
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var ende  = heute.AddDays(withinDays);

        var docs = await (
            from d in _db.EmployeeDokumente
            join t in _db.DokumentTypen on d.DokumentTypId equals t.Id
            join k in _db.DokumentKategorien on t.KategorieId equals k.Id
            join e in _db.Employees on d.EmployeeId equals e.Id
            where d.GueltigBis != null && d.GueltigBis <= ende
            orderby d.GueltigBis
            select new {
                d.Id,
                d.EmployeeId,
                employeeName = e.FirstName + " " + e.LastName,
                kategorieName = k.Name,
                dokumentTypName = t.Name,
                d.FilenameOriginal,
                d.GueltigBis,
                tageVerbleibend = (d.GueltigBis!.Value.DayNumber - heute.DayNumber)
            }
        ).ToListAsync();

        return Ok(docs);
    }

    // ══════════════════════════════════════════════════════════════════════
    // ADMIN: TAXONOMIE-VERWALTUNG (Kategorien & Typen)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Komplette Taxonomie inkl. inaktiver Einträge & Nutzungszähler.
    /// Für die Admin-Seite "Dokument-Struktur".
    /// </summary>
    [HttpGet("admin/taxonomie")]
    public async Task<IActionResult> GetAdminTaxonomie()
    {
        var kategorien = await _db.DokumentKategorien
            .OrderBy(k => k.SortOrder).ThenBy(k => k.Name)
            .ToListAsync();
        var typen = await _db.DokumentTypen
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync();
        var usageByTyp = await _db.EmployeeDokumente
            .GroupBy(d => d.DokumentTypId)
            .Select(g => new { TypId = g.Key, Anzahl = g.Count() })
            .ToDictionaryAsync(x => x.TypId, x => x.Anzahl);

        var result = kategorien.Select(k => new {
            k.Id, k.Name, k.SortOrder, k.Aktiv,
            anzahlTypen = typen.Count(t => t.KategorieId == k.Id),
            anzahlDokumente = typen.Where(t => t.KategorieId == k.Id)
                                   .Sum(t => usageByTyp.GetValueOrDefault(t.Id, 0)),
            typen = typen.Where(t => t.KategorieId == k.Id).Select(t => new {
                t.Id, t.Name, t.SortOrder, t.Aktiv, t.LinkedFieldCode,
                anzahlDokumente = usageByTyp.GetValueOrDefault(t.Id, 0)
            }).ToList()
        });
        return Ok(result);
    }

    public class KategorieDto {
        public string Name { get; set; } = "";
        public int? SortOrder { get; set; }
        public bool? Aktiv { get; set; }
    }

    [Authorize(Roles = "admin")]
    [HttpPost("admin/kategorie")]
    public async Task<IActionResult> CreateKategorie([FromBody] KategorieDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name ist erforderlich.");
        var k = new DokumentKategorie {
            Name = dto.Name.Trim(),
            SortOrder = dto.SortOrder ?? 99,
            Aktiv = dto.Aktiv ?? true
        };
        _db.DokumentKategorien.Add(k);
        await _db.SaveChangesAsync();
        return Ok(new { k.Id });
    }

    [Authorize(Roles = "admin")]
    [HttpPut("admin/kategorie/{id:int}")]
    public async Task<IActionResult> UpdateKategorie(int id, [FromBody] KategorieDto dto)
    {
        var k = await _db.DokumentKategorien.FindAsync(id);
        if (k is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(dto.Name)) k.Name = dto.Name.Trim();
        if (dto.SortOrder.HasValue) k.SortOrder = dto.SortOrder.Value;
        if (dto.Aktiv.HasValue)     k.Aktiv     = dto.Aktiv.Value;
        await _db.SaveChangesAsync();
        return Ok();
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("admin/kategorie/{id:int}")]
    public async Task<IActionResult> DeleteKategorie(int id)
    {
        var k = await _db.DokumentKategorien.FindAsync(id);
        if (k is null) return NotFound();

        var hasTypen = await _db.DokumentTypen.AnyAsync(t => t.KategorieId == id);
        if (hasTypen) return BadRequest("Kategorie enthält noch Typen. Bitte zuerst Typen löschen oder verschieben.");

        _db.DokumentKategorien.Remove(k);
        await _db.SaveChangesAsync();
        return Ok();
    }

    public class TypDto {
        public int? KategorieId { get; set; }
        public string Name { get; set; } = "";
        public int? SortOrder { get; set; }
        public bool? Aktiv { get; set; }
        /// <summary>Optionaler Field-Code für Verknüpfung mit MA-Stammdaten.
        /// Leerstring oder null = keine Verknüpfung.</summary>
        public string? LinkedFieldCode { get; set; }
    }

    [Authorize(Roles = "admin")]
    [HttpPost("admin/typ")]
    public async Task<IActionResult> CreateTyp([FromBody] TypDto dto)
    {
        if (!dto.KategorieId.HasValue) return BadRequest("Kategorie ist erforderlich.");
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name ist erforderlich.");
        var katExists = await _db.DokumentKategorien.AnyAsync(k => k.Id == dto.KategorieId.Value);
        if (!katExists) return BadRequest("Kategorie nicht gefunden.");
        var t = new DokumentTyp {
            KategorieId = dto.KategorieId.Value,
            Name = dto.Name.Trim(),
            SortOrder = dto.SortOrder ?? 99,
            Aktiv = dto.Aktiv ?? true,
            LinkedFieldCode = string.IsNullOrWhiteSpace(dto.LinkedFieldCode) ? null : dto.LinkedFieldCode!.Trim()
        };
        _db.DokumentTypen.Add(t);
        await _db.SaveChangesAsync();
        return Ok(new { t.Id });
    }

    [Authorize(Roles = "admin")]
    [HttpPut("admin/typ/{id:int}")]
    public async Task<IActionResult> UpdateTyp(int id, [FromBody] TypDto dto)
    {
        var t = await _db.DokumentTypen.FindAsync(id);
        if (t is null) return NotFound();
        if (dto.KategorieId.HasValue) {
            var katExists = await _db.DokumentKategorien.AnyAsync(k => k.Id == dto.KategorieId.Value);
            if (!katExists) return BadRequest("Kategorie nicht gefunden.");
            t.KategorieId = dto.KategorieId.Value;
        }
        if (!string.IsNullOrWhiteSpace(dto.Name)) t.Name = dto.Name.Trim();
        if (dto.SortOrder.HasValue) t.SortOrder = dto.SortOrder.Value;
        if (dto.Aktiv.HasValue)     t.Aktiv     = dto.Aktiv.Value;
        // LinkedFieldCode: leerer String (vom UI Dropdown "— keine —") = bewusst auf null setzen.
        // Property nicht im DTO = unverändert lassen (haben wir nicht: DTO ist immer voll geschickt).
        // Hier setzen wir bei JEDEM PUT, weil das Frontend immer den aktuellen Wert mitschickt.
        t.LinkedFieldCode = string.IsNullOrWhiteSpace(dto.LinkedFieldCode) ? null : dto.LinkedFieldCode!.Trim();
        await _db.SaveChangesAsync();
        return Ok();
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("admin/typ/{id:int}")]
    public async Task<IActionResult> DeleteTyp(int id)
    {
        var t = await _db.DokumentTypen.FindAsync(id);
        if (t is null) return NotFound();

        var inUse = await _db.EmployeeDokumente.AnyAsync(d => d.DokumentTypId == id);
        if (inUse) return BadRequest("Typ wird von hochgeladenen Dokumenten verwendet. Bitte zuerst diese verschieben oder löschen.");

        _db.DokumentTypen.Remove(t);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ──────────────────────────────────────────────────────────────────────
    // AUDIT: Filial-Mismatch zwischen Dateiname und BranchCode
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verdachtsfälle: Dateiname enthält den Namen einer ANDEREN Filiale als
    /// die, in der das Dokument abgelegt ist. Walter-Anwendungsfall: aus
    /// d.velop archivierte Dokumente wo der Mandant nicht zum Inhalt passt
    /// (Dokument wurde unter falscher Filiale abgelegt).
    ///
    /// Match-Logik: Filial-Name (z.B. „Hendschiken") als ganzes Wort,
    /// case-insensitive, im FilenameOriginal — und der BranchCode des Doku
    /// gehört NICHT zu dieser Filiale. Standard-Wörter wie „McDonald" werden
    /// ignoriert (kommen in vielen Dateinamen vor).
    /// </summary>
    public class AuditMismatch
    {
        public int     DocId { get; set; }
        public int     EmployeeId { get; set; }
        public string  EmployeeName { get; set; } = "";
        public string  EmployeeFirstName { get; set; } = "";
        public string? EmployeeNumber { get; set; }
        public string? CurrentBranchCode { get; set; }
        public string? CurrentBranchName { get; set; }
        public List<string> SuspectedBranchCodes { get; set; } = new();
        public string? Filename { get; set; }
        public string? Kategorie { get; set; }
        public string? Typ { get; set; }
    }

    [HttpGet("audit/branch-mismatch")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> AuditBranchMismatch()
    {
        // Branches mit Name + Code laden
        var branches = await _db.CompanyProfiles
            .Where(c => !string.IsNullOrEmpty(c.RestaurantCode) && !string.IsNullOrEmpty(c.BranchName))
            .Select(c => new { Code = c.RestaurantCode!, Name = c.BranchName! })
            .ToListAsync();
        if (branches.Count == 0)
            return Ok(new { total = 0, mismatches = new List<AuditMismatch>() });

        // Lowercase-Form für Vergleich vorbereiten
        var branchKeys = branches
            .Select(b => new { b.Code, NameLower = b.Name.ToLowerInvariant().Trim() })
            .ToList();

        // Lookup-Maps (kein .Include, da EmployeeDokument keine Navigations
        // zu Employee/DokumentTyp hat — wir machen die Joins manuell)
        var empById = await _db.Employees
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber })
            .ToDictionaryAsync(e => e.Id);
        var typById = await _db.DokumentTypen
            .Select(t => new { t.Id, t.Name, t.KategorieId })
            .ToDictionaryAsync(t => t.Id);
        var katById = await _db.DokumentKategorien
            .Select(k => new { k.Id, k.Name })
            .ToDictionaryAsync(k => k.Id);

        // Statistik: alle Dokumente in der DB zählen, dann nur die mit
        // Branch+Filename untersuchen (alle anderen können wir mangels
        // Vergleichsdaten nicht prüfen).
        var totalDocs    = await _db.EmployeeDokumente.CountAsync();
        var docsNoBranch = await _db.EmployeeDokumente
            .CountAsync(d => string.IsNullOrEmpty(d.BranchCode));
        var docsNoFile   = await _db.EmployeeDokumente
            .CountAsync(d => string.IsNullOrEmpty(d.FilenameOriginal));

        var docs = await _db.EmployeeDokumente
            .Where(d => !string.IsNullOrEmpty(d.BranchCode)
                     && !string.IsNullOrEmpty(d.FilenameOriginal))
            .Select(d => new {
                d.Id, d.EmployeeId, d.DokumentTypId, d.BranchCode, d.FilenameOriginal
            })
            .ToListAsync();
        var examined = docs.Count;

        var mismatches = new List<AuditMismatch>();
        foreach (var d in docs)
        {
            var fnLower = (d.FilenameOriginal ?? "").ToLowerInvariant();
            var currentBranchName = branches.FirstOrDefault(b => b.Code == d.BranchCode)?.Name;
            var currentLower = (currentBranchName ?? "").ToLowerInvariant().Trim();

            var hits = branchKeys
                .Where(bk => !string.Equals(bk.Code, d.BranchCode, StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(bk.NameLower, currentLower, StringComparison.OrdinalIgnoreCase)
                          && bk.NameLower.Length >= 4   // zu kurz = false-positive risk
                          && fnLower.Contains(bk.NameLower))
                .Select(bk => bk.Code)
                .Distinct()
                .ToList();

            if (hits.Count == 0) continue;

            empById.TryGetValue(d.EmployeeId, out var emp);
            typById.TryGetValue(d.DokumentTypId, out var typ);
            string? katName = null;
            if (typ != null && katById.TryGetValue(typ.KategorieId, out var kat)) katName = kat.Name;

            mismatches.Add(new AuditMismatch {
                DocId = d.Id,
                EmployeeId = d.EmployeeId,
                EmployeeFirstName = emp?.FirstName ?? "",
                EmployeeName = $"{emp?.FirstName} {emp?.LastName}".Trim(),
                EmployeeNumber = emp?.EmployeeNumber,
                CurrentBranchCode = d.BranchCode,
                CurrentBranchName = currentBranchName,
                SuspectedBranchCodes = hits,
                Filename = d.FilenameOriginal,
                Kategorie = katName,
                Typ = typ?.Name
            });
        }

        // Vorname-Sortierung (Konvention im System)
        var sorted = mismatches
            .OrderBy(m => m.EmployeeFirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(new {
            total            = sorted.Count,
            totalDocs        = totalDocs,
            examined         = examined,
            skippedNoBranch  = docsNoBranch,
            skippedNoFile    = docsNoFile,
            branchesScanned  = branches.Count,
            mismatches       = sorted
        });
    }

    // ──────────────────────────────────────────────────────────────────────
    // HELPER
    // ──────────────────────────────────────────────────────────────────────

    private int? GetCurrentUserId()
    {
        var sub = User.FindFirstValue("sub")
               ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }
}

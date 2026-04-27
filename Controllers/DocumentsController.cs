using HrSystem.Data;
using HrSystem.Models;
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

    /// <summary>
    /// Storage-Pfad wird aus appsettings.json (Documents:StoragePath) gelesen.
    /// Default: "data/documents" relativ zum Content-Root.
    /// Auf dem Server via systemd-Environment "Documents__StoragePath=/var/data/hr-system/documents".
    /// </summary>
    public DocumentsController(AppDbContext db, IConfiguration config, IWebHostEnvironment env)
    {
        _db = db;
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
                         .Select(t => new { t.Id, t.Name, t.SortOrder })
                         .ToList()
        });

        return Ok(result);
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
                d.HochgeladenVon
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
        [FromForm] DateOnly? gueltigBis)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Keine Datei hochgeladen.");
        if (string.IsNullOrWhiteSpace(branchCode))
            return BadRequest("Filiale-Code fehlt. Bitte zuerst eine Filiale wählen.");

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
            HochgeladenAm    = DateTime.UtcNow
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

    private async Task<IActionResult> ServeFile(int id, bool asAttachment)
    {
        var doc = await _db.EmployeeDokumente.FindAsync(id);
        if (doc is null) return NotFound();

        var fullPath = ResolveFilePath(doc);
        if (fullPath is null)
            return NotFound("Datei nicht im Storage gefunden.");

        var stream = System.IO.File.OpenRead(fullPath);
        var contentDisposition = asAttachment ? "attachment" : "inline";
        Response.Headers["Content-Disposition"] =
            $"{contentDisposition}; filename=\"{Uri.EscapeDataString(doc.FilenameOriginal)}\"";
        return File(stream, doc.MimeType);
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
                t.Id, t.Name, t.SortOrder, t.Aktiv,
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
    }

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
            Aktiv = dto.Aktiv ?? true
        };
        _db.DokumentTypen.Add(t);
        await _db.SaveChangesAsync();
        return Ok(new { t.Id });
    }

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
        await _db.SaveChangesAsync();
        return Ok();
    }

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
    // HELPER
    // ──────────────────────────────────────────────────────────────────────

    private int? GetCurrentUserId()
    {
        var sub = User.FindFirstValue("sub")
               ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }
}

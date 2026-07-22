using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using iText.Kernel.Pdf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.RegularExpressions;

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
        // Walter 14.06.2026: AsNoTracking — reine Lese-Liste, kein Change-Tracking nötig.
        var kategorien = await _db.DokumentKategorien
            .AsNoTracking()
            .Where(k => k.Aktiv)
            .OrderBy(k => k.SortOrder).ThenBy(k => k.Name)
            .ToListAsync();

        var typen = await _db.DokumentTypen
            .AsNoTracking()
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
        [FromQuery] string code,
        [FromQuery] bool all = false)
    {
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest("Field-Code fehlt.");

        var q = _db.EmployeeDokumente
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
            });

        // all=true → ALLE passenden Dokumente (neueste zuerst) — z.B. für die
        // Ausweis-Auswahl im Bewilligungs-Modal (Walter 12.07.2026: ein MA kann
        // mehrere Ausweis-Scans haben, der richtige muss wählbar sein).
        if (all) return Ok(await q.ToListAsync());

        var doc = await q.FirstOrDefaultAsync();
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
        // Walter 14.06.2026: AsNoTracking auf allen drei Quellen — der Endpoint
        // wird beim jedem MA-Wechsel im Doku-Tab gerufen, kein Change-Tracking nötig.
        var docs = await (
            from d in _db.EmployeeDokumente.AsNoTracking()
            join t in _db.DokumentTypen.AsNoTracking() on d.DokumentTypId equals t.Id
            join k in _db.DokumentKategorien.AsNoTracking() on t.KategorieId equals k.Id
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

        // Walter-Vorgabe 20.06.2026: pro Dokument melden, ob es an einer der fünf
        // wirksamen FK-Stellen verknüpft ist (Pass/ID, C-Ausweis, QST-Befreiung,
        // Bewilligung, Ehepartner-Beleg) — IDENTISCH zur Lösch-Sperre in Delete().
        // Solche Dokumente bewirken etwas (z.B. QST-Befreiung) und dürfen nicht
        // gelöscht werden → das Frontend blendet die „Löschen"-Option aus.
        var emp = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new {
                e.IdPassDokumentId, e.CAusweisDokumentId, e.QstBefreiungDokumentId,
                e.NightWorkExamDokumentId, e.NightWorkAusnahmeDokumentId,
                e.ProbezeitGespraech1DokumentId, e.ProbezeitGespraech2DokumentId
            })
            .FirstOrDefaultAsync();
        var permitDocIds = await _db.EmployeePermitHistories.AsNoTracking()
            .Where(h => h.EmployeeId == employeeId && h.DokumentId != null)
            .Select(h => h.DokumentId!.Value).ToListAsync();
        var familyDocIds = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(f => f.EmployeeId == employeeId && f.DokumentId != null)
            .Select(f => f.DokumentId!.Value).ToListAsync();
        var pregnancyDokIds = await _db.EmployeePregnancies.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId && p.ArztbestaetigungDokumentId != null)
            .Select(p => p.ArztbestaetigungDokumentId!.Value).ToListAsync();

        var linkedMap = new Dictionary<int, List<string>>();
        void AddLink(int? docId, string label)
        {
            if (!docId.HasValue) return;
            if (!linkedMap.TryGetValue(docId.Value, out var l)) { l = new List<string>(); linkedMap[docId.Value] = l; }
            if (!l.Contains(label)) l.Add(label);
        }
        AddLink(emp?.IdPassDokumentId,        "Pass / ID-Karte");
        AddLink(emp?.CAusweisDokumentId,      "C-Ausweis");
        AddLink(emp?.QstBefreiungDokumentId,  "QST-Behörden-Befreiung");
        AddLink(emp?.NightWorkExamDokumentId, "Nachtarbeit: Arztbericht / Verzicht");
        AddLink(emp?.NightWorkAusnahmeDokumentId, "Nachtarbeit: Ausnahmeregelung");
        AddLink(emp?.ProbezeitGespraech1DokumentId, "Probezeitgespräch 1");
        AddLink(emp?.ProbezeitGespraech2DokumentId, "Probezeitgespräch 2");
        foreach (var pid in permitDocIds) AddLink(pid, "Bewilligung (Aufenthalt)");
        foreach (var fid in familyDocIds) AddLink(fid, "Ehepartner-Beleg");
        foreach (var mid in pregnancyDokIds) AddLink(mid, "Arztbestätigung errechneter Termin");

        var result = docs.Select(d => new {
            d.Id, d.EmployeeId, d.dokumentTypId, d.dokumentTypName, d.kategorieId, d.kategorieName,
            d.FilenameOriginal, d.MimeType, d.GroesseBytes, d.Bemerkung, d.GueltigVon, d.GueltigBis,
            d.HochgeladenAm, d.HochgeladenVon, d.ErstelltAm, d.GeaendertAm, d.DateiGeaendertAm,
            d.ZugriffAm, d.GeaendertVon, d.ZugriffVon, d.DvelopDokumentId,
            linked   = linkedMap.ContainsKey(d.Id),
            linkedAs = linkedMap.TryGetValue(d.Id, out var lbls) ? lbls : null
        });

        return Ok(result);
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
    [Authorize(Roles = "admin,superuser,user")]
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
    // ──────────────────────────────────────────────────────────────────
    // AUSWEIS-OCR (Walter-Vorgabe 12.07.2026): liest den Schweizer
    // Aufenthaltstitel (Kreditkarten-Format) aus dem hinterlegten Scan und
    // liefert Bewilligungs-Typ (L/B/C/G/N/F) + «Gültig bis» als VORSCHLAG für
    // das Bewilligungs-Modal — der Benutzer prüft und speichert selbst.
    // SERVER-VORAUSSETZUNG: sudo apt install -y tesseract-ocr tesseract-ocr-deu poppler-utils
    // Fehlt tesseract → 501 mit Klartext-Hinweis (Feature degradiert sauber).
    // ──────────────────────────────────────────────────────────────────

    /// <summary>ICAO-9303-Prüfziffer (Gewichte 7-3-1) für MRZ-Felder.</summary>
    private static int IcaoCheck(string s)
    {
        int[] w = { 7, 3, 1 }; var sum = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var v = char.IsDigit(c) ? c - '0' : c == '<' ? 0 : c - 'A' + 10;
            sum += v * w[i % 3];
        }
        return sum % 10;
    }

    /// <summary>Ablaufdatum aus MRZ-Text: [Geschlecht](JJMMTT)(Prüfziffer) — nur
    /// mit korrekter Prüfziffer. NULL wenn kein valider Treffer.</summary>
    private static DateOnly? ParseMrzExpiry(string mrz)
    {
        foreach (Match m in Regex.Matches(mrz, @"[MF<](\d{6})(\d)"))
        {
            var e6 = m.Groups[1].Value;
            if (IcaoCheck(e6) != m.Groups[2].Value[0] - '0') continue;
            if (!int.TryParse(e6[..2], out var ey) || !int.TryParse(e6[2..4], out var em)
                || !int.TryParse(e6[4..], out var ed) || em is < 1 or > 12 || ed is < 1 or > 31) continue;
            try { return new DateOnly(2000 + ey, em, ed); } catch { }
        }
        return null;
    }

    /// <summary>Bewilligungs-Typ aus OCR-Text: «CHE L …» (Kartenkopf, auch
    /// OCR-Doppler «CHE LL») oder «Ausweis B». BEWUSST kein Standalone-
    /// Buchstabe mehr — zu rauschanfällig (Walter-Bug 12.07.2026: falsches N).</summary>
    private static string? ParsePermitType(string txt)
    {
        var m = Regex.Match(txt, @"\bCHE\s+([LBCGNF])\1?\b");
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(txt, @"Ausweis\s+([LBCGNF])\b", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        // Neueres Karten-Layout (Walter 12.07.2026, B-Ausweis Tomova):
        // kein «AUFENTHALTSTITEL CHE B» mehr, sondern Block
        // «ART DES TITELS … Bewilligung B». Der Buchstabe steht direkt
        // hinter «Bewilligung» — kein Standalone-Treffer (Rausch-Falle).
        m = Regex.Match(txt, @"Bewilligung\s+([LBCGNF])\b", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        // OCR-tolerante Kopf-Variante (Walter 12.07.2026, B-Ausweis Aurelio):
        // «CHE» wird bei kleinen Scans gern verstümmelt («cH B AUFENTHALTS-
        // TITEL») — der Buchstabe DIREKT vor dem AUFENTHALTSTITEL-Wort ist
        // trotzdem eindeutig (gleiche ENTHALTSTITE-Toleranz wie das Kopf-Band).
        m = Regex.Match(txt, @"\b([LBCGNF])\s+\w*ENTHALTSTITE", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        return null;
    }

    private static string? FindBinary(string name)
    {
        foreach (var p in new[] { $"/usr/bin/{name}", $"/usr/local/bin/{name}", $"/opt/homebrew/bin/{name}" })
            if (System.IO.File.Exists(p)) return p;
        return null;
    }

    private static async Task<(int code, string stdout, string stderr)> RunProcessAsync(string bin, string args, int timeoutMs = 30000)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(bin, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // tesseract startet sonst pro Prozess ~4 OpenMP-Threads — bei parallelen
        // Seiten auf einem kleinen VPS führt das zu Thrash statt Tempo
        // (Walter-Bug 12.07.2026: «dauert länger + HTTP 500»).
        psi.EnvironmentVariables["OMP_THREAD_LIMIT"] = "1";
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var so = proc.StandardOutput.ReadToEndAsync();
        var se = proc.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeoutMs);
        try { await proc.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { proc.Kill(true); } catch { } throw new TimeoutException($"{Path.GetFileName(bin)} Timeout."); }
        return (proc.ExitCode, await so, await se);
    }

    [HttpPost("{id:int}/ocr-permit")]
    public async Task<IActionResult> OcrPermit(int id)
    {
        var doc = await _db.EmployeeDokumente.FindAsync(id);
        if (doc is null) return NotFound();
        var fullPath = ResolveFilePath(doc);
        if (fullPath is null) return NotFound("Datei nicht im Storage gefunden.");

        var tesseract = FindBinary("tesseract");
        if (tesseract == null)
            return StatusCode(501, new { error = "OCR_NOT_INSTALLED", message = "OCR nicht verfügbar — auf dem Server installieren: sudo apt install -y tesseract-ocr tesseract-ocr-deu poppler-utils" });

        var tmpDir = Path.Combine(Path.GetTempPath(), "ocr_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var imgPath = fullPath;
            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            if (ext == ".pdf" || doc.MimeType == "application/pdf")
            {
                var ppm = FindBinary("pdftoppm");
                if (ppm == null)
                    return StatusCode(501, new { error = "OCR_NOT_INSTALLED", message = "poppler-utils fehlt (pdftoppm) — sudo apt install -y poppler-utils" });
                // ALLE Seiten (Rückseite mit MRZ/Ausstellungsdatum kann Seite 3+
                // sein — Walter-Scan 12.07.2026 hatte sie auf Seite 3); Cap bei 5.
                // 200 dpi als Basis (Walter 12.07.2026, Performance): reicht fürs
                // Lokalisieren der Bänder + Label-Fallbacks; die präzise Lesung
                // passiert ohnehin in den hochauflösenden Band-Crops.
                await RunProcessAsync(ppm, $"-png -r 200 -f 1 -l 5 \"{fullPath}\" \"{Path.Combine(tmpDir, "page")}\"", timeoutMs: 60000);
                imgPath = Directory.GetFiles(tmpDir, "page*.png").OrderBy(x => x).FirstOrDefault()
                          ?? throw new InvalidOperationException("PDF-Seite konnte nicht gerendert werden.");
            }
            var imgPaths = Directory.Exists(tmpDir) && Directory.GetFiles(tmpDir, "page*.png").Length > 0
                ? Directory.GetFiles(tmpDir, "page*.png").OrderBy(x => x).ToArray()
                : new[] { imgPath };

            // Performance-Umbau (Walter 12.07.2026, «40 Sekunden»): pro Seite
            // genau EIN tesseract-Lauf (psm 6, tsv) — daraus entstehen BEIDES:
            // der Volltext (Wörter je Zeile zusammensetzen) UND die Band-
            // Koordinaten für die MRZ-Crops. Alle Seiten laufen PARALLEL.
            var ocrSem = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount - 1, 1, 4));
            async Task<(string text, int top, int bottom, int hdrTop, int hdrBottom)> TsvPass(string img, int pageNo)
            {
                await ocrSem.WaitAsync();
                try
                {
                var ob = Path.Combine(tmpDir, $"tsv{pageNo}");
                int c;
                try { (c, _, _) = await RunProcessAsync(tesseract, $"\"{img}\" \"{ob}\" -l deu --psm 6 tsv", timeoutMs: 40000); }
                catch (TimeoutException) { return ("", int.MaxValue, 0, int.MaxValue, 0); }
                if (c != 0)
                {
                    try { (c, _, _) = await RunProcessAsync(tesseract, $"\"{img}\" \"{ob}\" --psm 6 tsv", timeoutMs: 40000); }
                    catch (TimeoutException) { return ("", int.MaxValue, 0, int.MaxValue, 0); }
                }
                if (c != 0 || !System.IO.File.Exists(ob + ".tsv")) return ("", int.MaxValue, 0, int.MaxValue, 0);

                var sb = new System.Text.StringBuilder();
                string lastKey = "";
                int top = int.MaxValue, bottom = 0, hdrTop = int.MaxValue, hdrBottom = 0;
                foreach (var line in await System.IO.File.ReadAllLinesAsync(ob + ".tsv"))
                {
                    var cols = line.Split('\t');
                    if (cols.Length < 12) continue;
                    var word = cols[11].Trim();
                    if (word.Length == 0 || cols[0] == "level") continue;
                    var key = cols[1] + "/" + cols[2] + "/" + cols[3] + "/" + cols[4]; // page/block/par/line
                    if (key != lastKey) { if (sb.Length > 0) sb.Append('\n'); lastKey = key; }
                    else sb.Append(' ');
                    sb.Append(word);
                    // MRZ-Kandidat: lange Kette MIT Ziffern oder «<» — reine
                    // Buchstaben-Wörter (GESCHLECHTNATIONALITAT …) zählen NICHT,
                    // sonst wird die halbe Karte zum Band (Walter-Bug 12.07.2026).
                    if (word.Length >= 15 && Regex.IsMatch(word, @"^[A-Z0-9<]{15,}$")
                        && (word.Contains('<') || word.Any(char.IsDigit))
                        && int.TryParse(cols[7], out var y) && int.TryParse(cols[9], out var hh))
                    {
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y + hh);
                    }
                    // Kopf-Band für den TYP: enge Zone um «AUFENTHALTSTITEL»
                    // (dort steht links davor «CHE L»).
                    if (word.Contains("ENTHALTSTITE", StringComparison.Ordinal)
                        && int.TryParse(cols[7], out var hy) && int.TryParse(cols[9], out var hhh))
                    {
                        hdrTop = Math.Min(hdrTop, hy);
                        hdrBottom = Math.Max(hdrBottom, hy + hhh);
                    }
                }
                return (sb.ToString(), top, bottom, hdrTop, hdrBottom);
                }
                finally { ocrSem.Release(); }
            }
            var pageResults = await Task.WhenAll(imgPaths.Select((img, i) => TsvPass(img, i + 1)));
            var texts = pageResults.Select(p => p.text).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (texts.Count == 0)
                return StatusCode(500, new { error = "OCR_FAILED", message = "tesseract lieferte keinen Text (Sprachpaket deu installiert? Scan lesbar?)." });
            var text = string.Join("\n----\n", texts);

            // ── MRZ (Maschinenlese-Zone, Karten-Rückseite) — zuverlässigste
            //    Quelle für Ablaufdatum + ZEMIS-Nr (Walter-Vorgabe 12.07.2026).
            //    Vorgehen: Position der MRZ per TSV-Lauf finden, dann NUR dieses
            //    Band in doppelter Auflösung neu rendern (pdftoppm -x/-y/-W/-H)
            //    und mit Zeichen-Whitelist A–Z/0–9/< lesen. ──
            string mrzText = "";
            try
            {
                if ((ext == ".pdf" || doc.MimeType == "application/pdf") && FindBinary("pdftoppm") is string ppm2)
                {
                    // 1) Band-Koordinaten kommen bereits aus dem TSV-Lauf oben —
                    //    getrennt: MRZ-Bänder (mit 1-Zoll-Label-Zone darüber) und
                    //    enge Kopf-Bänder für den Typ («CHE L AUFENTHALTSTITEL»).
                    var bands = pageResults
                        .Select((p, i) => (pageNo: i + 1, p.top, p.bottom, wide: true))
                        .Where(b => b.top != int.MaxValue)
                        .Concat(pageResults
                            .Select((p, i) => (pageNo: i + 1, top: p.hdrTop, bottom: p.hdrBottom, wide: false))
                            .Where(b => b.top != int.MaxValue))
                        .ToList();

                    // 2) Auflösungs-Kaskade mit FRÜH-ABBRUCH (Walter 12.07.2026,
                    //    Performance): sobald ein Ablaufdatum die ICAO-Prüfziffer
                    //    besteht, keine weiteren Renderings mehr.
                    foreach (var res in new[] { 1000, 600, 800 })
                    {
                        foreach (var (pageNo, top, bottom, wide) in bands)
                        {
                            // MRZ-Band («wide»): ~1 Zoll nach oben — dort stehen die
                            // Rückseiten-Labels (AUSSTELLUNGSDATUM). Kopf-Band: eng.
                            var up   = wide ? res : res * 3 / 20;
                            var down = wide ? res / 5 : res * 3 / 20;
                            var y0 = Math.Max(0, (top * res / 200) - up);
                            var hBand = (bottom - top) * res / 200 + up + down;
                            var cropBase = Path.Combine(tmpDir, $"mrz{pageNo}_{res}_{(wide ? "w" : "h")}");
                            await RunProcessAsync(ppm2,
                                $"-png -r {res} -f {pageNo} -l {pageNo} -y {y0} -H {hBand} -gray \"{fullPath}\" \"{cropBase}\"");
                            var cropImg = Directory.GetFiles(tmpDir, $"mrz{pageNo}_{res}_{(wide ? "w" : "h")}*.png").OrderBy(x => x).FirstOrDefault();
                            if (cropImg == null) continue;
                            var mrzBase = Path.Combine(tmpDir, $"mrzout{pageNo}_{res}_{(wide ? "w" : "h")}");
                            await RunProcessAsync(tesseract,
                                $"\"{cropImg}\" \"{mrzBase}\" --psm 6 -c tessedit_char_whitelist=ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789<");
                            if (System.IO.File.Exists(mrzBase + ".txt"))
                                mrzText += await System.IO.File.ReadAllTextAsync(mrzBase + ".txt") + "\n";
                        }
                        // Früh-Abbruch erst, wenn BEIDES sitzt: Ablaufdatum mit
                        // gültiger Prüfziffer UND der Typ aus dem Kartenkopf
                        // (Walter-Bug 12.07.2026: L ging in der 1000er-Runde verloren).
                        // Der Typ darf auch aus dem VOLLTEXT kommen (neueres
                        // B-Karten-Layout «Bewilligung B» steht nicht im Band).
                        if (ParseMrzExpiry(mrzText) != null
                            && (ParsePermitType(mrzText) != null || ParsePermitType(text) != null)) break;
                    }
                }
            }
            catch { /* MRZ ist best-effort — die Label-Erkennung unten bleibt */ }

            // ── Bewilligungs-Typ: Kartenkopf «CHE L» (Band-Text zuerst, dann
            //    Volltext); kein Standalone-Buchstabe (Rausch-Falle). ──
            var permitCode = ParsePermitType(mrzText) ?? ParsePermitType(text);

            // ── Eskalation (Walter 12.07.2026, B-Ausweis Aurelio): kleine Karte
            //    auf grossem A4-Scan → der 200-dpi-Basistext ist unlesbar, das
            //    Kopf-Band («ENTHALTSTITE») wird nie gefunden und der Typ steht
            //    bei diesem Layout NUR im Kopf («CHE B AUFENTHALTSTITEL» — der
            //    Titel-Block sagt bloss «Ausweis EU/EFTA», ohne Buchstabe).
            //    NUR wenn der Typ fehlt: Seiten einzeln in 400 dpi nachlesen,
            //    Abbruch sobald er sitzt. Der Zusatztext hilft anschliessend
            //    auch den Datums-/Label-Regexen weiter unten. ──
            if (permitCode == null && (ext == ".pdf" || doc.MimeType == "application/pdf")
                && FindBinary("pdftoppm") is string ppm3)
            {
                try
                {
                    for (var pageNo = 1; pageNo <= imgPaths.Length; pageNo++)
                    {
                        var hiBase = Path.Combine(tmpDir, $"hi{pageNo}");
                        await RunProcessAsync(ppm3, $"-png -r 400 -f {pageNo} -l {pageNo} \"{fullPath}\" \"{hiBase}\"", timeoutMs: 60000);
                        var hiImg = Directory.GetFiles(tmpDir, $"hi{pageNo}*.png").OrderBy(x => x).FirstOrDefault();
                        if (hiImg == null) continue;
                        var hiOut = Path.Combine(tmpDir, $"hiout{pageNo}");
                        int hc;
                        try { (hc, _, _) = await RunProcessAsync(tesseract, $"\"{hiImg}\" \"{hiOut}\" -l deu --psm 6", timeoutMs: 60000); }
                        catch (TimeoutException) { continue; }
                        if (hc != 0)
                        {
                            try { (hc, _, _) = await RunProcessAsync(tesseract, $"\"{hiImg}\" \"{hiOut}\" --psm 6", timeoutMs: 60000); }
                            catch (TimeoutException) { continue; }
                        }
                        if (hc != 0 || !System.IO.File.Exists(hiOut + ".txt")) continue;
                        var hiText = await System.IO.File.ReadAllTextAsync(hiOut + ".txt");
                        if (string.IsNullOrWhiteSpace(hiText)) continue;
                        text += "\n----\n" + hiText;
                        permitCode ??= ParsePermitType(hiText);
                        if (permitCode != null) break;
                    }
                }
                catch { /* Eskalation ist best-effort — Teil-Resultat bleibt */ }
            }

            // ── «Gültig bis»-Datum (OCR-tolerant: GULTIG/GÜLTIG/G0LTIG …) ──
            DateOnly? validUntil = null;
            var gm = Regex.Match(text, @"G.{0,2}LTIG\s*BIS\D{0,90}?(\d{2})[\s./-]?(\d{2})[\s./-]?(\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (gm.Success
                && int.TryParse(gm.Groups[1].Value, out var d1)
                && int.TryParse(gm.Groups[2].Value, out var m1)
                && int.TryParse(gm.Groups[3].Value, out var y1)
                && m1 is >= 1 and <= 12 && d1 is >= 1 and <= 31)
            {
                try { validUntil = new DateOnly(y1, m1, d1); } catch { }
            }
            // Fallback: spätestes Datum auf der Karte (Geburtsdatum liegt in der
            // Vergangenheit, das Ablaufdatum ist das späteste).
            if (validUntil == null)
            {
                foreach (Match m in Regex.Matches(text, @"(\d{2})[\s./-](\d{2})[\s./-](\d{4})"))
                {
                    if (!int.TryParse(m.Groups[1].Value, out var dd)) continue;
                    if (!int.TryParse(m.Groups[2].Value, out var mm)) continue;
                    if (!int.TryParse(m.Groups[3].Value, out var yy)) continue;
                    if (mm is < 1 or > 12 || dd is < 1 or > 31 || yy < 2000 || yy > 2100) continue;
                    try
                    {
                        var dt = new DateOnly(yy, mm, dd);
                        if (validUntil == null || dt > validUntil) validUntil = dt;
                    }
                    catch { }
                }
            }

            // ── Ausstellungsdatum (Rückseite: «Ausstellungsdatum» / «Ausgestellt am»
            //    / franz. «Date de délivrance») → Vorschlag für «Gültig ab». ──
            DateOnly? issued = null;
            // Auch im MRZ-Band suchen — dort liest das Label oft am saubersten
            // («AUSSTELLUNGSDATUMORTBEHORDE 24062026», Walter 12.07.2026).
            // Die Whitelist-OCR liest führende Nullen gern als Buchstabe «O»
            // («O7112024», Walter-Bug 12.07.2026 Aurelio) — deshalb vor der
            // Suche jedes O in Ziffern-Nachbarschaft zu 0 normalisieren
            // (Schleife, damit auch «OO7…» vollständig kippt).
            var issuedSearch = mrzText + "\n" + text;
            for (var prev = ""; prev != issuedSearch;)
            {
                prev = issuedSearch;
                issuedSearch = Regex.Replace(issuedSearch, @"[Oo](?=\d)|(?<=\d)[Oo]", "0");
            }
            var im = Regex.Match(issuedSearch, @"(AUSSTELLUNG\w*|AUSGESTELLT\s*AM|D.{0,2}LIVRANCE)\D{0,90}?(\d{2})[\s./-]?(\d{2})[\s./-]?(\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (im.Success
                && int.TryParse(im.Groups[2].Value, out var d2)
                && int.TryParse(im.Groups[3].Value, out var m2)
                && int.TryParse(im.Groups[4].Value, out var y2)
                && m2 is >= 1 and <= 12 && d2 is >= 1 and <= 31
                && y2 is >= 2000 and <= 2100)
            {
                try { issued = new DateOnly(y2, m2, d2); } catch { }
            }

            // ── MRZ auswerten: Zeile 2 = Geb(6) Prüf(1) Geschlecht(1) Ablauf(6)
            //    Prüf(1) Nationalität; Zeile 1 endet mit der ZEMIS-Nr (9 Ziffern,
            //    Format 12345678.9). MRZ hat VORRANG vor der Label-Erkennung. ──
            string? zemisNr = null;
            if (mrzText.Length > 0)
            {
                // Ablaufdatum aus der MRZ (prüfziffern-validiert) hat VORRANG.
                var mrzExp = ParseMrzExpiry(mrzText);
                if (mrzExp.HasValue) validUntil = mrzExp;
                foreach (var line in mrzText.Split('\n'))
                {
                    // Zeile 1: beginnt mit Dok-Code+CHE; ZEMIS = letzte 9 Ziffern
                    // des Ziffern-Schwanzes (robust gegen OCR-Einschübe).
                    var clean = line.Trim().Replace("<", "");
                    if (clean.Length < 15 || !clean.Contains("CHE", StringComparison.Ordinal)) continue;
                    var digits = new string(clean.Where(char.IsDigit).ToArray());
                    if (digits.Length >= 9)
                    {
                        var z = digits[^9..];
                        zemisNr = z[..8] + "." + z[8..];
                        break;
                    }
                }
            }
            // ZEMIS-Fallback (Walter 12.07.2026): die Nummer steht auch VORNE auf
            // der Karte (klein, «12345678.4») und neben «ZEMIS NR» auf der Rück-
            // seite — standalone 9-Ziffern-Block (mit oder ohne Punkt) im Gesamttext.
            if (zemisNr == null)
            {
                var zm = Regex.Match(mrzText + "\n" + text, @"\b(\d{8})[.]?(\d)\b");
                if (zm.Success) zemisNr = zm.Groups[1].Value + "." + zm.Groups[2].Value;
            }

            // ── Ausstellungsdatum-Fallback (Walter 12.07.2026, Martina S.):
            //    auf manchen Rückseiten ist das AUSSTELLUNG-Label unleserlich
            //    («SESBARESASA»), das Datum selbst aber klar («20042026»).
            //    Dann: alle FREISTEHENDEN 8-stelligen Datums-Token im O→0-
            //    normalisierten Text einsammeln, plausibel filtern (echtes
            //    Datum, Vergangenheit, nicht Geburts-/Ablaufdatum) und das
            //    JÜNGSTE nehmen — die Ausstellung liegt stets NACH dem
            //    Einreisedatum, das als zweiter Vergangenheits-Kandidat
            //    auf der Karte steht. ──
            if (issued == null)
            {
                var heuteD = DateOnly.FromDateTime(DateTime.Today);
                // Geburtsdatum aus MRZ Zeile 2 (yymmdd) — beide Jahrhundert-
                // Varianten ausschliessen, damit es nie als Ausstellung gilt.
                var geb = new List<DateOnly>();
                var gm2 = Regex.Match(mrzText, @"^(\d{2})(\d{2})(\d{2})\d[MF<]", RegexOptions.Multiline);
                if (gm2.Success
                    && int.TryParse(gm2.Groups[1].Value, out var gy)
                    && int.TryParse(gm2.Groups[2].Value, out var gmo)
                    && int.TryParse(gm2.Groups[3].Value, out var gd)
                    && gmo is >= 1 and <= 12 && gd is >= 1 and <= 31)
                {
                    try { geb.Add(new DateOnly(1900 + gy, gmo, gd)); } catch { }
                    try { geb.Add(new DateOnly(2000 + gy, gmo, gd)); } catch { }
                }
                DateOnly? best = null;
                foreach (Match dm in Regex.Matches(issuedSearch, @"\b(\d{2})(\d{2})(\d{4})\b"))
                {
                    if (!int.TryParse(dm.Groups[1].Value, out var fd)
                        || !int.TryParse(dm.Groups[2].Value, out var fm)
                        || !int.TryParse(dm.Groups[3].Value, out var fy)) continue;
                    if (fm is < 1 or > 12 || fd is < 1 or > 31 || fy is < 2000 or > 2100) continue;
                    DateOnly cand;
                    try { cand = new DateOnly(fy, fm, fd); } catch { continue; }
                    if (cand > heuteD) continue;                                  // Zukunft = Ablaufdatum o.ä.
                    if (validUntil.HasValue && cand == validUntil.Value) continue;
                    if (geb.Contains(cand)) continue;
                    if (best == null || cand > best.Value) best = cand;
                }
                issued = best;
            }

            return Ok(new
            {
                permitCode,
                validUntil = validUntil?.ToString("yyyy-MM-dd"),
                issued = issued?.ToString("yyyy-MM-dd"),
                zemisNr,
                mrzGelesen = mrzText.Length > 0,
                excerpt = ((mrzText.Length > 0 ? "── MRZ ──\n" + mrzText + "\n" : "") + text) is var full && full.Length > 2500 ? full[..2500] : full,
            });
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

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
    [Authorize(Roles = "admin,superuser,user")]
    public async Task<IActionResult> Delete(int id)
    {
        var doc = await _db.EmployeeDokumente.FindAsync(id);
        if (doc is null) return NotFound();

        // ── Walter-Vorgabe 14.06.2026: Lösch-Schutz ──
        // Wenn dieses Dokument an einer der fünf FK-Stellen verknüpft ist
        // (Pass/ID, alter C-Ausweis-Slot, QST-Befreiung, Permit-History
        // oder Ehepartner-Ausweis), darf es nicht gelöscht werden — sonst
        // verlieren wir QST-Belege. Walter muss die Verknüpfung zuerst lösen.
        var blockers = new List<string>();
        if (await _db.Employees.AnyAsync(e => e.IdPassDokumentId == id))
            blockers.Add("Pass / ID-Karte am MA");
        if (await _db.Employees.AnyAsync(e => e.CAusweisDokumentId == id))
            blockers.Add("C-Ausweis am MA (alte Verknüpfung)");
        if (await _db.Employees.AnyAsync(e => e.QstBefreiungDokumentId == id))
            blockers.Add("QST-Behörden-Befreiung");
        if (await _db.Employees.AnyAsync(e => e.NightWorkExamDokumentId == id))
            blockers.Add("Nachtarbeit: Arztbericht / Verzicht");
        if (await _db.Employees.AnyAsync(e => e.NightWorkAusnahmeDokumentId == id))
            blockers.Add("Nachtarbeit: Ausnahmeregelung");
        if (await _db.Employees.AnyAsync(e => e.ProbezeitGespraech1DokumentId == id))
            blockers.Add("Probezeitgespräch 1");
        if (await _db.Employees.AnyAsync(e => e.ProbezeitGespraech2DokumentId == id))
            blockers.Add("Probezeitgespräch 2");
        if (await _db.EmployeePermitHistories.AnyAsync(h => h.DokumentId == id))
            blockers.Add("Bewilligungs-Eintrag (Aufenthalt)");
        if (await _db.EmployeeFamilyMembers.AnyAsync(f => f.DokumentId == id))
            blockers.Add("Ehepartner-Ausweis");
        if (await _db.EmployeePregnancies.AnyAsync(p => p.ArztbestaetigungDokumentId == id))
            blockers.Add("Arztbestätigung errechneter Termin (Mutterschaft)");

        if (blockers.Count > 0)
        {
            var betroffenerMa = await _db.Employees
                .Where(e => e.Id == doc.EmployeeId)
                .Select(e => new { e.FirstName, e.LastName, e.EmployeeNumber })
                .FirstOrDefaultAsync();
            var maName = betroffenerMa != null
                ? $"{betroffenerMa.FirstName} {betroffenerMa.LastName} (Nr. {betroffenerMa.EmployeeNumber})"
                : $"MA-ID {doc.EmployeeId}";
            return Conflict(new
            {
                error = "DOKUMENT_VERKNUEPFT",
                message = $"Das Dokument ist als {string.Join(" + ", blockers)} bei {maName} verknüpft und kann nicht gelöscht werden. Bitte erst die Verknüpfung lösen.",
                verknuepfungen = blockers
            });
        }

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
    // UPLOAD-PROTOKOLL (Walter 22.07.2026 — Bommer/Buchhaltung)
    // Wer hat wann welches Dokument in die MA-Akte gelegt?
    // Ersetzt die frühere BommerBox-Sicht, seit Unterlagen direkt in OneCrew
    // (dieses System) landen.
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Liste der hochgeladenen MA-Dokumente (Akte) mit Uploader, Datum, MA, Filiale.
    /// Rollen: admin, superuser, buchhaltung. Buchhaltung ist filial-beschränkt.
    /// </summary>
    [HttpGet("upload-protocol")]
    [Authorize(Roles = "admin,superuser,buchhaltung")]
    public async Task<IActionResult> UploadProtocol(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int? companyProfileId = null,
        [FromQuery] string? q = null,
        [FromQuery] int limit = 500)
    {
        try
        {
            if (limit > 5000) limit = 5000;
            var built = await BuildUploadProtocolAsync(from, to, companyProfileId, q, limit);
            if (built.ErrorResult != null) return built.ErrorResult;
            return Ok(new {
                total = built.Items.Count,
                limit,
                items = built.Items.Select(x => new {
                    id = x.Id,
                    hochgeladenAm = x.HochgeladenAm,
                    hochgeladenVonId = x.HochgeladenVonId,
                    hochgeladenVon = x.HochgeladenVon,
                    filename = x.Filename,
                    groesseBytes = x.GroesseBytes,
                    mimeType = x.MimeType,
                    bemerkung = x.Bemerkung,
                    kategorie = x.Kategorie,
                    dokumentTyp = x.DokumentTyp,
                    branchCode = x.BranchCode,
                    employeeId = x.EmployeeId,
                    employeeNumber = x.EmployeeNumber,
                    firstName = x.FirstName,
                    lastName = x.LastName
                })
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new {
                error = "UPLOAD_PROTOCOL_FAILED",
                message = "Upload-Protokoll konnte nicht geladen werden: " + ex.Message
            });
        }
    }

    /// <summary>CSV-Export desselben Upload-Protokolls (Excel-tauglich mit BOM).</summary>
    [HttpGet("upload-protocol/export")]
    [Authorize(Roles = "admin,superuser,buchhaltung")]
    public async Task<IActionResult> UploadProtocolExport(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int? companyProfileId = null,
        [FromQuery] string? q = null)
    {
        var built = await BuildUploadProtocolAsync(from, to, companyProfileId, q, 10000);
        if (built.ErrorResult != null) return built.ErrorResult;

        static string Csv(string? s)
        {
            var v = (s ?? "").Replace("\"", "\"\"");
            return "\"" + v + "\"";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Datum;Uploader;Personalnr;Mitarbeiter;Filiale;Kategorie;Typ;Dateiname;GroesseBytes;Bemerkung");
        foreach (var it in built.Items)
        {
            sb.Append(Csv(it.HochgeladenAm.ToString("dd.MM.yyyy HH:mm"))).Append(';')
              .Append(Csv(it.HochgeladenVon)).Append(';')
              .Append(Csv(it.EmployeeNumber)).Append(';')
              .Append(Csv(((it.FirstName ?? "") + " " + (it.LastName ?? "")).Trim())).Append(';')
              .Append(Csv(it.BranchCode)).Append(';')
              .Append(Csv(it.Kategorie)).Append(';')
              .Append(Csv(it.DokumentTyp)).Append(';')
              .Append(Csv(it.Filename)).Append(';')
              .Append(it.GroesseBytes.ToString()).Append(';')
              .Append(Csv(it.Bemerkung))
              .AppendLine();
        }

        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var name = $"dokument-upload-protokoll_{DateTime.Now:yyyy-MM-dd}.csv";
        return File(bytes, "text/csv; charset=utf-8", name);
    }

    private sealed record UploadProtocolRow(
        int Id, DateTime HochgeladenAm, int? HochgeladenVonId, string? HochgeladenVon,
        string Filename, long GroesseBytes, string? MimeType, string? Bemerkung,
        string? Kategorie, string? DokumentTyp, string? BranchCode,
        int EmployeeId, string? EmployeeNumber, string? FirstName, string? LastName);

    private sealed record UploadProtocolBuild(List<UploadProtocolRow> Items, IActionResult? ErrorResult);

    private async Task<UploadProtocolBuild> BuildUploadProtocolAsync(
        DateOnly? from, DateOnly? to, int? companyProfileId, string? q, int limit)
    {
        if (limit < 1) limit = 1;
        if (limit > 10000) limit = 10000;

        var allowedCodes = await GetAllowedBranchCodesAsync();
        if (allowedCodes is null)
            return new UploadProtocolBuild(new(), StatusCode(403, new { error = "Kein Filial-Zugriff." }));

        string? filterCode = null;
        if (companyProfileId.HasValue)
        {
            var cp = await _db.CompanyProfiles.AsNoTracking()
                .Where(c => c.Id == companyProfileId.Value)
                .Select(c => new { c.Id, c.RestaurantCode })
                .FirstOrDefaultAsync();
            if (cp is null)
                return new UploadProtocolBuild(new(), BadRequest(new { error = "Filiale nicht gefunden." }));
            if (allowedCodes.Count > 0 && (string.IsNullOrEmpty(cp.RestaurantCode)
                || !allowedCodes.Contains(cp.RestaurantCode)))
                return new UploadProtocolBuild(new(), StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." }));
            filterCode = cp.RestaurantCode;
        }

        var fromDt = from?.ToDateTime(TimeOnly.MinValue);
        var toDt   = to?.ToDateTime(new TimeOnly(23, 59, 59));
        var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim().ToLowerInvariant();

        var query =
            from d in _db.EmployeeDokumente.AsNoTracking()
            join e in _db.Employees.AsNoTracking() on d.EmployeeId equals e.Id
            join t in _db.DokumentTypen.AsNoTracking() on d.DokumentTypId equals t.Id
            join k in _db.DokumentKategorien.AsNoTracking() on t.KategorieId equals k.Id
            join u in _db.AppUsers.AsNoTracking() on d.HochgeladenVon equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            select new { d, e, t, k, u };

        if (fromDt.HasValue)
            query = query.Where(x => x.d.HochgeladenAm >= fromDt.Value);
        if (toDt.HasValue)
            query = query.Where(x => x.d.HochgeladenAm <= toDt.Value);

        if (!string.IsNullOrEmpty(filterCode))
            query = query.Where(x => x.d.BranchCode == filterCode);
        else if (allowedCodes.Count > 0)
            query = query.Where(x => x.d.BranchCode != null && allowedCodes.Contains(x.d.BranchCode));

        if (search != null)
        {
            // Keine null-Checks auf Left-Join-User in SQL — Uploader-Suche
            // nachladen wir best-effort über Username/Name-Felder (EF-sicher).
            query = query.Where(x =>
                x.d.FilenameOriginal.ToLower().Contains(search)
                || (x.e.FirstName != null && x.e.FirstName.ToLower().Contains(search))
                || (x.e.LastName != null && x.e.LastName.ToLower().Contains(search))
                || (x.e.EmployeeNumber != null && x.e.EmployeeNumber.ToLower().Contains(search))
                || x.t.Name.ToLower().Contains(search)
                || x.k.Name.ToLower().Contains(search)
                || (x.u != null && x.u.Username.ToLower().Contains(search))
                || (x.u != null && x.u.FirstName != null && x.u.FirstName.ToLower().Contains(search))
                || (x.u != null && x.u.LastName != null && x.u.LastName.ToLower().Contains(search)));
        }

        // Flache Projektion (EF-übersetzbar) — Display-Name des Uploaders
        // danach in Memory zusammenbauen (sonst 500 durch Trim/Ternary in SQL).
        var raw = await query
            .OrderByDescending(x => x.d.HochgeladenAm)
            .Take(limit)
            .Select(x => new {
                x.d.Id,
                x.d.HochgeladenAm,
                x.d.HochgeladenVon,
                UploaderFirst = x.u != null ? x.u.FirstName : null,
                UploaderLast  = x.u != null ? x.u.LastName : null,
                UploaderUser  = x.u != null ? x.u.Username : null,
                x.d.FilenameOriginal,
                x.d.GroesseBytes,
                x.d.MimeType,
                x.d.Bemerkung,
                Kategorie = x.k.Name,
                DokumentTyp = x.t.Name,
                x.d.BranchCode,
                EmployeeId = x.e.Id,
                x.e.EmployeeNumber,
                x.e.FirstName,
                x.e.LastName
            })
            .ToListAsync();

        static string? UploaderDisplay(string? first, string? last, string? username)
        {
            var name = ((first ?? "") + " " + (last ?? "")).Trim();
            return string.IsNullOrEmpty(name) ? username : name;
        }

        var rows = raw.Select(x => new UploadProtocolRow(
            x.Id,
            x.HochgeladenAm,
            x.HochgeladenVon,
            UploaderDisplay(x.UploaderFirst, x.UploaderLast, x.UploaderUser),
            x.FilenameOriginal,
            x.GroesseBytes,
            x.MimeType,
            x.Bemerkung,
            x.Kategorie,
            x.DokumentTyp,
            x.BranchCode,
            x.EmployeeId,
            x.EmployeeNumber,
            x.FirstName,
            x.LastName
        )).ToList();

        return new UploadProtocolBuild(rows, null);
    }

    /// <summary>
    /// Erlaubte Filial-Codes für den Aufrufer.
    /// null = kein Zugriff; leere Liste = Admin (alle); sonst Positiv-Liste.
    /// Buchhaltung wird ZUERST geprüft (Doppel-Claim superuser, CLAUDE.md).
    /// </summary>
    private async Task<List<string>?> GetAllowedBranchCodesAsync()
    {
        if (User.IsInRole("admin"))
            return new List<string>(); // leer = alle

        var uid = GetCurrentUserId();
        if (uid is null) return null;

        // buchhaltung und normale User/Superuser: nur user_branch_access
        var codes = await (
            from a in _db.UserBranchAccesses.AsNoTracking()
            join c in _db.CompanyProfiles.AsNoTracking() on a.CompanyProfileId equals c.Id
            where a.UserId == uid && c.RestaurantCode != null && c.RestaurantCode != ""
            select c.RestaurantCode!
        ).Distinct().ToListAsync();

        // Superuser OHNE buchhaltung-Claim und OHNE Filial-Zuordnung: alle
        // (wie historische HR-Praxis). Buchhaltung ohne Zuordnung → leer/gesperrt.
        if (codes.Count == 0 && User.IsInRole("superuser") && !User.IsInRole("buchhaltung"))
            return new List<string>();

        return codes;
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

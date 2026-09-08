using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.EasyAtWork;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HrSystem.Controllers;

/// <summary>
/// easy@work HR-Dateien (Dokumente an Mitarbeitende), Walter-Vorgabe 08.09.2026:
/// OneCrew legt ein PDF ins easy@work-Dossier des MA, easy@work benachrichtigt
/// den MA in seiner App. Dritter Kanal ZUSÄTZLICH zu E-Mail und SMS.
///
/// Stufe 1 (Entwicklung): Testkarte auf der Seite «easy@work API» — Dateitypen
/// anzeigen, EIN Dokument an EINEN MA senden, Dossier des MA anzeigen.
/// Nur Admin. Alles wird als Roh-JSON zurückgegeben, damit Walter sieht, was
/// easy@work wirklich antwortet.
/// </summary>
[ApiController]
[Route("api/easywork/hr-files")]
[Authorize(Roles = "admin")]
public class EasyAtWorkHrFilesController : ControllerBase
{
    private readonly EasyAtWorkClient _client;
    private readonly AppDbContext _db;
    private readonly ILogger<EasyAtWorkHrFilesController> _log;
    private readonly MitteilungPdfService _mitteilungPdf;
    private readonly string _storagePath;

    public EasyAtWorkHrFilesController(EasyAtWorkClient client, AppDbContext db,
                                       ILogger<EasyAtWorkHrFilesController> log,
                                       MitteilungPdfService mitteilungPdf,
                                       IConfiguration config, IWebHostEnvironment env)
    {
        _client = client;
        _db = db;
        _log = log;
        _mitteilungPdf = mitteilungPdf;
        // Gleicher Ablageort wie MailboxController (Documents:StoragePath / data/documents).
        var configured = config["Documents:StoragePath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine(env.ContentRootPath, "data", "documents");
        _storagePath = configured;
    }

    // ───────────────────────── Hilfen ─────────────────────────────

    private sealed record EmpTarget(int Id, string Number, string Name, int EawEmployeeId,
                                    int? CompanyProfileId, string? BranchName);

    /// <summary>
    /// MA nach Personalnummer auflösen: braucht eine easy@work-ID (kommt vom
    /// MA-Sync) und die Filiale der jüngsten Anstellung (→ Customer).
    /// </summary>
    private async Task<(EmpTarget? emp, IActionResult? error)> ResolveEmployeeAsync(string? number, CancellationToken ct)
    {
        var nr = (number ?? "").Trim();
        if (nr.Length == 0)
            return (null, BadRequest(new { error = "NUMBER_REQUIRED", message = "Bitte eine Personalnummer angeben." }));

        var e = await _db.Employees.AsNoTracking()
            .Where(x => !x.IsHidden && x.EmployeeNumber == nr)
            .Select(x => new { x.Id, x.EmployeeNumber, x.FirstName, x.LastName, x.EasyAtWorkEmployeeId })
            .FirstOrDefaultAsync(ct);
        if (e == null)
            return (null, NotFound(new { error = "EMPLOYEE_NOT_FOUND", message = $"Personalnummer {nr} ist in OneCrew nicht vorhanden." }));
        if (e.EasyAtWorkEmployeeId is not int eawId || eawId <= 0)
            return (null, Conflict(new { error = "NO_EAW_ID", message = $"{e.FirstName} {e.LastName} ({nr}) hat keine easy@work-ID — zuerst den MA-Sync laufen lassen." }));

        var cp = await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == e.Id && em.CompanyProfileId != null)
            .OrderByDescending(em => em.ContractStartDate)
            .Select(em => em.CompanyProfileId)
            .FirstOrDefaultAsync(ct);
        string? branchName = null;
        if (cp is int cpId)
            branchName = await _db.CompanyProfiles.AsNoTracking()
                .Where(c => c.Id == cpId).Select(c => c.BranchName ?? c.CompanyName).FirstOrDefaultAsync(ct);

        return (new EmpTarget(e.Id, e.EmployeeNumber, $"{e.FirstName} {e.LastName}".Trim(), eawId, cp, branchName), null);
    }

    /// <summary>
    /// Customer-Kandidaten: zuerst die Filiale des MA, dann alle übrigen
    /// gemappten Customers (falls der MA in easy@work bei einem anderen
    /// Customer hängt — wie beim API-Dump nach ID).
    /// </summary>
    private async Task<List<int>> CustomerCandidatesAsync(int? companyProfileId, CancellationToken ct)
    {
        var mappings = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .Select(m => new { m.CompanyProfileId, m.EasyAtWorkCustomerId })
            .ToListAsync(ct);
        var ids = new List<int>();
        if (companyProfileId.HasValue)
        {
            var sel = mappings.FirstOrDefault(m => m.CompanyProfileId == companyProfileId.Value);
            if (sel != null) ids.Add(sel.EasyAtWorkCustomerId);
        }
        foreach (var cid in mappings.Select(m => m.EasyAtWorkCustomerId).Distinct())
            if (!ids.Contains(cid)) ids.Add(cid);
        return ids;
    }

    /// <summary>Findet den Customer, bei dem /employees/{eawId} 2xx liefert.</summary>
    private async Task<int?> FindCustomerForEmployeeAsync(EmpTarget emp, CancellationToken ct)
    {
        foreach (var cid in await CustomerCandidatesAsync(emp.CompanyProfileId, ct))
        {
            try
            {
                var (status, _) = await _client.GetRawAsync($"customers/{cid}/employees/{emp.EawEmployeeId}", ct);
                if (status >= 200 && status < 300) return cid;
            }
            catch (Exception ex) { _log.LogWarning(ex, "easy@work HR-Files: Customer-Probe {Cid} fehlgeschlagen", cid); }
        }
        return null;
    }

    private static object ParseOrRaw(string body)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(body); }
        catch { return body; }
    }

    // ───────────────────────── Endpoints ──────────────────────────

    /// <summary>Dateitypen (Kategorien) des Customers der gewählten Filiale.</summary>
    [HttpGet("types")]
    public async Task<IActionResult> Types([FromQuery] int companyProfileId, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var mapping = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyProfileId == companyProfileId, ct);
        if (mapping == null)
            return NotFound(new { error = "NO_MAPPING", message = "Diese Filiale ist keinem easy@work-Customer zugeordnet." });

        var (status, body) = await _client.GetHrFileTypesRawAsync(mapping.EasyAtWorkCustomerId, ct);
        return StatusCode(status >= 200 && status < 300 ? 200 : status, new
        {
            customerId = mapping.EasyAtWorkCustomerId,
            customerName = mapping.EasyAtWorkCustomerName,
            status,
            response = ParseOrRaw(body),
        });
    }

    public sealed record CreateTypeDto(int CompanyProfileId, string Name, bool Mandatory = false, bool OnlyPdf = true);

    /// <summary>
    /// Dateityp (Kategorie) beim Customer der Filiale anlegen
    /// (POST /customers/{c}/hr_file_types). Typen gelten nur pro Customer —
    /// jede Filiale braucht ihren eigenen.
    /// </summary>
    [HttpPost("types")]
    public async Task<IActionResult> CreateType([FromBody] CreateTypeDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        if (dto == null || string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { error = "NAME_REQUIRED", message = "Bitte einen Namen für den Dateityp angeben." });
        var mapping = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyProfileId == dto.CompanyProfileId, ct);
        if (mapping == null)
            return NotFound(new { error = "NO_MAPPING", message = "Diese Filiale ist keinem easy@work-Customer zugeordnet." });

        var payload = new Dictionary<string, object?>
        {
            ["name"] = dto.Name.Trim(),
            ["mandatory"] = dto.Mandatory,
            ["accept_file_types"] = dto.OnlyPdf ? new[] { "application/pdf" } : null,
        };
        var json = JsonSerializer.Serialize(payload);
        var (status, body) = await _client.SendRawAsync(HttpMethod.Post,
            $"customers/{mapping.EasyAtWorkCustomerId}/hr_file_types",
            () => new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
        var ok = status >= 200 && status < 300;
        _log.LogInformation("easy@work HR-Files: Dateityp «{Name}» bei Customer {Cid} angelegt → {Status}", dto.Name, mapping.EasyAtWorkCustomerId, status);
        return StatusCode(ok ? 200 : status, new
        {
            ok, status, customerId = mapping.EasyAtWorkCustomerId, sent = payload, response = ParseOrRaw(body),
        });
    }

    /// <summary>Dossier (HR-Dateien) eines MA nach Personalnummer.</summary>
    [HttpGet("list")]
    public async Task<IActionResult> List([FromQuery] string number, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var (emp, err) = await ResolveEmployeeAsync(number, ct);
        if (err != null) return err;

        var cid = await FindCustomerForEmployeeAsync(emp!, ct);
        if (cid == null)
            return NotFound(new { error = "EAW_EMPLOYEE_NOT_FOUND", message = $"easy@work-ID {emp!.EawEmployeeId} wurde bei keinem gemappten Customer gefunden." });

        var (status, body, via) = await _client.GetHrFilesRawAsync(cid.Value, emp!.EawEmployeeId, ct);
        return StatusCode(status >= 200 && status < 300 ? 200 : status, new
        {
            employee = emp, customerId = cid, status, via, response = ParseOrRaw(body),
        });
    }

    /// <summary>
    /// Lese-Probe (Walter 08.09.2026): Wo landet eine Datei, die der MA aus der
    /// App hochlädt? Testet read-only plausible Endpoints durch und zeigt Status
    /// + Trefferzahl + Anfang der Antwort. Schreibt nichts.
    /// </summary>
    [HttpGet("probe")]
    public async Task<IActionResult> Probe([FromQuery] string number, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var (emp, err) = await ResolveEmployeeAsync(number, ct);
        if (err != null) return err;
        var cid = await FindCustomerForEmployeeAsync(emp!, ct);
        if (cid == null)
            return NotFound(new { error = "EAW_EMPLOYEE_NOT_FOUND", message = $"easy@work-ID {emp!.EawEmployeeId} wurde bei keinem gemappten Customer gefunden." });
        var e = emp!.EawEmployeeId;
        var c = cid.Value;
        var paths = new[]
        {
            $"customers/{c}/employees/{e}/hr_files?per_page=100",
            $"customers/{c}/employees/{e}/hr_files?per_page=100&include_expired=1",
            $"customers/{c}/employees/{e}/hr_overview?include_expired=1",
            $"customers/{c}/hr_overview?all_types=1",
            $"customers/{c}/hr_files?per_page=100",
            $"customers/{c}/employees/{e}/documents?per_page=100",
            $"customers/{c}/employees/{e}/files?per_page=100",
            $"customers/{c}/employees/{e}/uploads?per_page=100",
            $"customers/{c}/employees/{e}/attachments?per_page=100",
            $"customers/{c}/employees/{e}/hr_file_requests?per_page=100",
            $"customers/{c}/employees/{e}/requested_files?per_page=100",
            $"customers/{c}/employees/{e}/absences?per_page=50",
            $"customers/{c}/employees/{e}/sick_notes?per_page=50",
            $"customers/{c}/employees/{e}/messages?per_page=50",
            $"customers/{c}/documents?per_page=100",
            $"customers/{c}/files?per_page=100",
            $"customers/{c}/hr_file_requests?per_page=100",
            $"customers/{c}/messages?per_page=50",
            $"customers/{c}/notifications?per_page=50",
        };
        var results = new List<object>();
        foreach (var path in paths)
        {
            try
            {
                var (status, body) = await _client.GetRawAsync(path, ct);
                int? count = null;
                try
                {
                    var el = JsonSerializer.Deserialize<JsonElement>(body);
                    if (el.ValueKind == JsonValueKind.Array) count = el.GetArrayLength();
                    else if (el.ValueKind == JsonValueKind.Object)
                    {
                        if (el.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number) count = t.GetInt32();
                        else if (el.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array) count = d.GetArrayLength();
                        else if (el.TryGetProperty("files", out var f) && f.ValueKind == JsonValueKind.Array) count = f.GetArrayLength();
                    }
                }
                catch { /* kein JSON */ }
                results.Add(new { path, status, count, preview = body.Length > 600 ? body[..600] + " …" : body });
            }
            catch (Exception ex) { results.Add(new { path, status = -1, count = (int?)null, preview = ex.Message }); }
        }
        return Ok(new { employee = emp, customerId = c, results });
    }

    /// <summary>
    /// Eingang aus easy@work (Walter-Vorgabe 08.09.2026): Dateien, die der MA in
    /// der App «an HR» hochgeladen hat (Anhang mit user_id ≠ null), ins
    /// HR-Postfach holen — NIE direkt ins Dossier. Schon geholte Anhänge werden
    /// übersprungen (easyatwork_hr_file_eingang). Stufe 1: ein MA per
    /// Personalnummer (Testbutton).
    /// </summary>
    [HttpPost("eingang")]
    public async Task<IActionResult> Eingang([FromQuery] string number, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var (emp, err) = await ResolveEmployeeAsync(number, ct);
        if (err != null) return err;
        var cid = await FindCustomerForEmployeeAsync(emp!, ct);
        if (cid == null)
            return NotFound(new { error = "EAW_EMPLOYEE_NOT_FOUND", message = $"easy@work-ID {emp!.EawEmployeeId} wurde bei keinem gemappten Customer gefunden." });

        var (status, body, via) = await _client.GetHrFilesRawAsync(cid.Value, emp!.EawEmployeeId, ct);
        if (status < 200 || status >= 300)
            return StatusCode(status, new { error = "EAW_LIST_FAILED", status, via, response = ParseOrRaw(body) });

        // Dateien aus hr_overview (files) oder hr_files (data) lesen.
        var root = JsonSerializer.Deserialize<JsonElement>(body);
        JsonElement files = default;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (!root.TryGetProperty("files", out files) || files.ValueKind != JsonValueKind.Array)
                root.TryGetProperty("data", out files);
        }
        if (files.ValueKind != JsonValueKind.Array)
            return Ok(new { employee = emp, customerId = cid, via, geholt = 0, uebersprungen = 0, ohneAnhang = 0, hinweis = "Keine Dateiliste in der Antwort.", eintraege = Array.Empty<object>() });

        // Filiale für das Postfach: Filiale des MA, sonst die Filiale des Mappings.
        var cpId = emp.CompanyProfileId
                   ?? await _db.EasyAtWorkBranchMappings.AsNoTracking()
                          .Where(m => m.EasyAtWorkCustomerId == cid.Value)
                          .Select(m => (int?)m.CompanyProfileId).FirstOrDefaultAsync(ct)
                   ?? 0;
        if (cpId <= 0)
            return Conflict(new { error = "NO_BRANCH", message = "Keine Filiale für das HR-Postfach gefunden." });

        var schon = await _db.EasyAtWorkHrFileEingaenge.AsNoTracking()
            .Where(x => x.EmployeeId == emp.Id)
            .Select(x => x.EasyAtWorkAttachmentId)
            .ToListAsync(ct);
        var schonSet = new HashSet<long>(schon);

        int geholt = 0, uebersprungen = 0, ohneAnhang = 0, vonOneCrew = 0;
        var eintraege = new List<object>();

        foreach (var f in files.EnumerateArray())
        {
            var fileId = f.TryGetProperty("id", out var fid) && fid.ValueKind == JsonValueKind.Number ? fid.GetInt64() : 0;
            var dokName = f.TryGetProperty("name", out var fn) && fn.ValueKind == JsonValueKind.String ? fn.GetString() ?? "" : "";
            if (!f.TryGetProperty("attachments", out var atts) || atts.ValueKind != JsonValueKind.Array || atts.GetArrayLength() == 0)
            { ohneAnhang++; continue; }

            // Neuester Anhang = aktuelle Version (Doku) — wir nehmen den mit dem höchsten id.
            JsonElement att = default; long attId = -1;
            foreach (var a in atts.EnumerateArray())
            {
                var id = a.TryGetProperty("id", out var aid) && aid.ValueKind == JsonValueKind.Number ? aid.GetInt64() : 0;
                if (id > attId) { attId = id; att = a; }
            }
            if (attId <= 0) { ohneAnhang++; continue; }

            // user_id null = per API (OneCrew) hochgeladen → gehört nicht in den Eingang.
            long? eawUserId = att.TryGetProperty("user_id", out var uid) && uid.ValueKind == JsonValueKind.Number ? uid.GetInt64() : null;
            if (eawUserId == null) { vonOneCrew++; continue; }

            if (schonSet.Contains(attId)) { uebersprungen++; continue; }

            var attName = att.TryGetProperty("name", out var an) && an.ValueKind == JsonValueKind.String ? an.GetString() ?? "" : "";
            var mime    = att.TryGetProperty("mime", out var am) && am.ValueKind == JsonValueKind.String ? am.GetString() : null;
            long? size  = att.TryGetProperty("size", out var asz) && asz.ValueKind == JsonValueKind.Number ? asz.GetInt64() : null;
            DateTime? hochgeladenAm = null;
            if (att.TryGetProperty("created_at", out var ac) && ac.ValueKind == JsonValueKind.String
                && DateTime.TryParse(ac.GetString(), out var dtc)) hochgeladenAm = dtc;

            // Binär holen.
            var (dstatus, bytes, ctype, dlName) = await _client.DownloadHrFileAsync(cid.Value, emp.EawEmployeeId, fileId, ct);
            if (dstatus < 200 || dstatus >= 300)
            {
                eintraege.Add(new { fileId, attachmentId = attId, dokName, ok = false, status = dstatus, message = System.Text.Encoding.UTF8.GetString(bytes) });
                continue;
            }

            // Ablegen wie ein Postfach-Upload: mailbox/{filiale}/{guid}{ext}
            var origName = !string.IsNullOrWhiteSpace(attName) ? attName : (!string.IsNullOrWhiteSpace(dlName) ? dlName! : $"easyatwork-{fileId}");
            var ext = Path.GetExtension(origName);
            if (string.IsNullOrWhiteSpace(ext) && (mime ?? ctype) == "application/pdf") ext = ".pdf";
            var storageName = Guid.NewGuid().ToString("N") + ext;
            var dir = Path.Combine(_storagePath, "mailbox", cpId.ToString());
            Directory.CreateDirectory(dir);
            await System.IO.File.WriteAllBytesAsync(Path.Combine(dir, storageName), bytes, ct);

            // Dokumentname (mit Umlauten) ist der schönere Anzeigename als der Anhangname.
            var anzeige = !string.IsNullOrWhiteSpace(dokName) ? dokName : origName;
            if (!string.IsNullOrWhiteSpace(ext) && !anzeige.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) anzeige += ext;

            var wann = hochgeladenAm?.ToString("dd.MM.yyyy HH:mm") ?? "unbekannt";
            var mbox = new MailboxDocument
            {
                CompanyProfileId = cpId,
                UploadedBy = null,
                UploadedAt = DateTime.Now,
                OriginalFilename = anzeige,
                StorageFilename = storageName,
                MimeType = mime ?? ctype,
                FileSizeBytes = bytes.LongLength,
                Bemerkung = "Aus der easy@work-App hochgeladen (an HR)",
                MessageBody = $"{emp.Name} ({emp.Number}, {emp.BranchName ?? "Filiale"}) hat am {wann} in der easy@work-App ein Dokument an HR gesendet: «{dokName}». "
                              + "Bitte prüfen und bei Bedarf ins Dossier des MA übernehmen — es wurde bewusst NICHT automatisch abgelegt.",
                EmployeeId = emp.Id,
                NotifyUserId = null,
                TargetType = "HR",
            };
            _db.MailboxDocuments.Add(mbox);
            await _db.SaveChangesAsync(ct);

            _db.EasyAtWorkHrFileEingaenge.Add(new EasyAtWorkHrFileEingang
            {
                EmployeeId = emp.Id,
                EasyAtWorkCustomerId = cid.Value,
                EasyAtWorkEmployeeId = emp.EawEmployeeId,
                EasyAtWorkFileId = fileId,
                EasyAtWorkAttachmentId = attId,
                DokumentName = dokName,
                DateiName = origName,
                MimeType = mime ?? ctype,
                FileSizeBytes = size ?? bytes.LongLength,
                HochgeladenVonEawUserId = eawUserId,
                HochgeladenAm = hochgeladenAm,
                MailboxDocumentId = mbox.Id,
                GeholtAm = DateTime.Now,
            });
            await _db.SaveChangesAsync(ct);
            schonSet.Add(attId);
            geholt++;
            eintraege.Add(new { fileId, attachmentId = attId, dokName, dateiName = origName, size = bytes.LongLength, ok = true, mailboxDocumentId = mbox.Id });
            _log.LogInformation("easy@work Eingang: «{Dok}» ({Size} B) von {Emp} ({Nr}) ins HR-Postfach (mailbox {Mid})", dokName, bytes.LongLength, emp.Name, emp.Number, mbox.Id);
        }

        return Ok(new { employee = emp, customerId = cid, via, geholt, uebersprungen, vonOneCrew, ohneAnhang, eintraege });
    }

    /// <summary>
    /// Datei aus dem easy@work-Dossier holen (z.B. was der MA aus der App
    /// hochgeladen hat) — streamt die neueste Version. Jeder Abruf landet im
    /// Download-Log von easy@work.
    /// </summary>
    [HttpGet("download")]
    public async Task<IActionResult> Download([FromQuery] string number, [FromQuery] long fileId, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        if (fileId <= 0) return BadRequest(new { error = "FILE_ID_REQUIRED", message = "Bitte eine Datei-ID angeben." });
        var (emp, err) = await ResolveEmployeeAsync(number, ct);
        if (err != null) return err;
        var cid = await FindCustomerForEmployeeAsync(emp!, ct);
        if (cid == null)
            return NotFound(new { error = "EAW_EMPLOYEE_NOT_FOUND", message = $"easy@work-ID {emp!.EawEmployeeId} wurde bei keinem gemappten Customer gefunden." });

        var (status, bytes, contentType, fileName) = await _client.DownloadHrFileAsync(cid.Value, emp!.EawEmployeeId, fileId, ct);
        if (status < 200 || status >= 300)
            return StatusCode(status, new { error = "EAW_DOWNLOAD_FAILED", status, message = System.Text.Encoding.UTF8.GetString(bytes) });
        return File(bytes, string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                    string.IsNullOrWhiteSpace(fileName) ? $"easyatwork-{fileId}" : fileName);
    }

    /// <summary>
    /// EIN Dokument ODER eine Mitteilung an EINEN MA senden (Testkarte). Multipart:
    /// number, typeId, name (= Betreff), file (optional), text (optional — wird zum
    /// PDF, wenn keine Datei), notify (1/0), expiresAt (yyyy-MM-dd, optional),
    /// warnDays (optional), setExistingAsExpired (1/0).
    /// easy@work kennt keine reinen Textnachrichten → Mitteilung = PDF im Haus-Stil.
    /// </summary>
    [HttpPost("send")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Send(
        [FromForm] string number,
        [FromForm] int typeId,
        [FromForm] string name,
        [FromForm] IFormFile? file = null,
        [FromForm] string? text = null,
        [FromForm] bool notify = true,
        [FromForm] string? expiresAt = null,
        [FromForm] int? warnDays = null,
        [FromForm] bool setExistingAsExpired = false,
        CancellationToken ct = default)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var hasFile = file != null && file.Length > 0;
        var hasText = !string.IsNullOrWhiteSpace(text);
        if (!hasFile && !hasText)
            return BadRequest(new { error = "FILE_OR_TEXT_REQUIRED", message = "Bitte eine Datei auswählen oder einen Mitteilungstext eingeben." });
        if (typeId <= 0)
            return BadRequest(new { error = "TYPE_REQUIRED", message = "Bitte einen Dateityp (type_id) wählen." });
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "NAME_REQUIRED", message = "Bitte einen Dokumentnamen angeben." });

        var (emp, err) = await ResolveEmployeeAsync(number, ct);
        if (err != null) return err;

        var cid = await FindCustomerForEmployeeAsync(emp!, ct);
        if (cid == null)
            return NotFound(new { error = "EAW_EMPLOYEE_NOT_FOUND", message = $"easy@work-ID {emp!.EawEmployeeId} wurde bei keinem gemappten Customer gefunden." });

        DateOnly? exp = null;
        if (!string.IsNullOrWhiteSpace(expiresAt))
        {
            if (!DateOnly.TryParse(expiresAt, out var d))
                return BadRequest(new { error = "EXPIRES_INVALID", message = "Ablaufdatum bitte als JJJJ-MM-TT." });
            exp = d;
        }

        byte[] bytes;
        string fileName, mime;
        var alsMitteilung = false;
        if (hasFile)
        {
            using var ms = new MemoryStream();
            await file!.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
            fileName = file.FileName;
            mime = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        }
        else
        {
            // Mitteilung → PDF im Haus-Stil (Filiale des MA, Absender = eingeloggter Benutzer).
            alsMitteilung = true;
            var filiale = emp!.CompanyProfileId is int cpId
                ? await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cpId, ct)
                : null;
            var uidStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            // DisplayName ist berechnet (nicht in SQL übersetzbar) → erst laden, dann lesen.
            var absenderUser = int.TryParse(uidStr, out var uid)
                ? await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid, ct)
                : null;
            var absender = absenderUser?.DisplayName;
            bytes = _mitteilungPdf.Generate(new MitteilungPdfService.MitteilungData(
                FirmaName: filiale?.CompanyName,
                RestaurantName: filiale?.BranchName,
                EmpfaengerName: emp.Name,
                Betreff: name.Trim(),
                Text: text!.Trim(),
                Datum: DateOnly.FromDateTime(DateTime.Now),
                Ort: filiale?.City,
                AbsenderName: absender));
            var safe = string.Concat(name.Trim().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-');
            if (safe.Length > 60) safe = safe[..60];
            fileName = $"Mitteilung-{DateTime.Now:yyyy-MM-dd}-{(safe.Length > 0 ? safe : "OneCrew")}.pdf";
            mime = "application/pdf";
        }

        _log.LogInformation("easy@work HR-Files: sende «{Name}» ({Size} B, {Mime}, Mitteilung={Mitteilung}) an {Emp} ({Nr}, eaw {EawId}, customer {Cid}), type {TypeId}, notify={Notify}",
            name, bytes.Length, mime, alsMitteilung, emp!.Name, emp.Number, emp.EawEmployeeId, cid, typeId, notify);

        var (status, body) = await _client.UploadHrFileRawAsync(
            cid.Value, emp.EawEmployeeId, bytes, fileName, mime,
            typeId, name.Trim(), notify, exp, warnDays, setExistingAsExpired, ct);

        var ok = status >= 200 && status < 300;
        if (!ok)
            _log.LogWarning("easy@work HR-Files: Upload fehlgeschlagen ({Status}): {Body}", status, body);

        return StatusCode(ok ? 200 : status, new
        {
            ok,
            status,
            employee = emp,
            customerId = cid,
            sent = new { typeId, name = name.Trim(), fileName, mime, size = bytes.Length, alsMitteilung, notify, expiresAt = exp, warnDays, setExistingAsExpired },
            response = ParseOrRaw(body),
        });
    }
}

using HrSystem.Data;
using HrSystem.Services.EasyAtWork;
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

    public EasyAtWorkHrFilesController(EasyAtWorkClient client, AppDbContext db,
                                       ILogger<EasyAtWorkHrFilesController> log)
    {
        _client = client;
        _db = db;
        _log = log;
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

        var (status, body) = await _client.GetHrFilesRawAsync(cid.Value, emp!.EawEmployeeId, ct);
        return StatusCode(status >= 200 && status < 300 ? 200 : status, new
        {
            employee = emp, customerId = cid, status, response = ParseOrRaw(body),
        });
    }

    /// <summary>
    /// EIN Dokument an EINEN MA senden (Testkarte). Multipart:
    /// number, typeId, name, file, notify (1/0), expiresAt (yyyy-MM-dd, optional),
    /// warnDays (optional), setExistingAsExpired (1/0).
    /// </summary>
    [HttpPost("send")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Send(
        [FromForm] string number,
        [FromForm] int typeId,
        [FromForm] string name,
        [FromForm] IFormFile file,
        [FromForm] bool notify = true,
        [FromForm] string? expiresAt = null,
        [FromForm] int? warnDays = null,
        [FromForm] bool setExistingAsExpired = false,
        CancellationToken ct = default)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "FILE_REQUIRED", message = "Bitte eine Datei auswählen." });
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
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        _log.LogInformation("easy@work HR-Files: sende «{Name}» ({Size} B, {Mime}) an {Emp} ({Nr}, eaw {EawId}, customer {Cid}), type {TypeId}, notify={Notify}",
            name, bytes.Length, file.ContentType, emp!.Name, emp.Number, emp.EawEmployeeId, cid, typeId, notify);

        var (status, body) = await _client.UploadHrFileRawAsync(
            cid.Value, emp.EawEmployeeId, bytes, file.FileName, file.ContentType ?? "application/octet-stream",
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
            sent = new { typeId, name = name.Trim(), fileName = file.FileName, mime = file.ContentType, size = bytes.Length, notify, expiresAt = exp, warnDays, setExistingAsExpired },
            response = ParseOrRaw(body),
        });
    }
}

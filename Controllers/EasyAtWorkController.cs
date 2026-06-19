using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.EasyAtWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Endpoints für die easy@work-API-Integration. Phase 1 = Foundation:
/// Status anzeigen, Verbindung testen, Branch-Mapping pflegen.
/// Phase 2+ (Sync) baut auf diesen Endpoints auf.
///
/// Zugriff nur admin/superuser — der Connector enthält OAuth-Secrets und
/// hängt direkt am System-Setup.
/// </summary>
[ApiController]
[Route("api/easywork")]
[Authorize(Roles = "admin,superuser")]
public class EasyAtWorkController : ControllerBase
{
    private readonly EasyAtWorkClient _client;
    private readonly AppDbContext _db;
    private readonly ILogger<EasyAtWorkController> _log;
    private readonly Services.EasyAtWork.EasyAtWorkTimepunchSyncService _tpSync;
    private readonly Services.EasyAtWork.EasyAtWorkEmployeeSyncService  _empSync;

    public EasyAtWorkController(
        EasyAtWorkClient client,
        AppDbContext db,
        ILogger<EasyAtWorkController> log,
        Services.EasyAtWork.EasyAtWorkTimepunchSyncService tpSync,
        Services.EasyAtWork.EasyAtWorkEmployeeSyncService empSync)
    {
        _client = client;
        _db = db;
        _log = log;
        _tpSync = tpSync;
        _empSync = empSync;
    }

    // ─────────────────────────── Status ─────────────────────────────

    /// <summary>
    /// Zeigt, ob die Integration konfiguriert ist (kein Secret-Leak — nur
    /// `configured: true/false` + die nicht-geheimen Felder BaseUrl/ClientId).
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            configured = _client.IsConfigured,
            baseUrl    = _client.IsConfigured ? _client.BaseUrl  : null,
            clientId   = _client.IsConfigured ? _client.ClientId : null,
        });
    }

    /// <summary>
    /// Testet die Verbindung: holt einen Token + ruft GET /customers. Liefert
    /// bei Erfolg die Liste der für unseren API-Client sichtbaren Customers
    /// (Filialen) — daraus baut der Admin im Frontend die Branch-Mappings.
    /// </summary>
    [HttpGet("test-connection")]
    public async Task<IActionResult> TestConnection(CancellationToken ct)
    {
        if (!_client.IsConfigured)
            return StatusCode(503, new
            {
                error   = "EAW_NOT_CONFIGURED",
                message = "easy@work nicht konfiguriert. Bitte EASYATWORK_CLIENT_ID, "
                        + "EASYATWORK_CLIENT_SECRET und EASYATWORK_BASE_URL setzen (ENV "
                        + "oder appsettings.json Section 'EasyAtWork')."
            });

        try
        {
            var customers = await _client.GetCustomersAsync(ct);
            return Ok(new
            {
                ok       = true,
                baseUrl  = _client.BaseUrl,
                customers = customers.Data.Select(c => new
                {
                    id        = c.Id,
                    number    = c.Number,
                    name      = c.Name,
                    updatedAt = c.UpdatedAt
                }).ToList(),
                total    = customers.Total ?? customers.Data.Count
            });
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "easy@work test-connection fehlgeschlagen");
            return StatusCode(502, new
            {
                error   = "EAW_REQUEST_FAILED",
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "easy@work test-connection: unerwarteter Fehler");
            return StatusCode(500, new { error = "EAW_UNKNOWN", message = ex.Message });
        }
    }

    // ───────────────────── Branch-Mappings ──────────────────────────

    public record BranchMappingDto(
        int     Id,
        int     CompanyProfileId,
        string? CompanyProfileName,
        string? RestaurantCode,
        int     EasyAtWorkCustomerId,
        string? EasyAtWorkCustomerNumber,
        string? EasyAtWorkCustomerName,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    public record UpsertMappingDto(
        int     CompanyProfileId,
        int     EasyAtWorkCustomerId,
        string? EasyAtWorkCustomerNumber,
        string? EasyAtWorkCustomerName);

    /// <summary>Liste aller Filial-Mappings.</summary>
    [HttpGet("mappings")]
    public async Task<IActionResult> GetMappings(CancellationToken ct)
    {
        var rows = await (
            from m in _db.EasyAtWorkBranchMappings.AsNoTracking()
            join cp in _db.CompanyProfiles.AsNoTracking() on m.CompanyProfileId equals cp.Id
            orderby (cp.BranchName ?? cp.CompanyName)
            select new BranchMappingDto(
                m.Id,
                m.CompanyProfileId,
                cp.BranchName ?? cp.CompanyName,
                cp.RestaurantCode,
                m.EasyAtWorkCustomerId,
                m.EasyAtWorkCustomerNumber,
                m.EasyAtWorkCustomerName,
                m.CreatedAt,
                m.UpdatedAt)
        ).ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>Mapping neu anlegen oder vorhandenes updaten (per CompanyProfileId).</summary>
    [HttpPost("mappings")]
    public async Task<IActionResult> UpsertMapping([FromBody] UpsertMappingDto dto, CancellationToken ct)
    {
        if (dto.CompanyProfileId <= 0)
            return BadRequest(new { error = "BAD_COMPANY_PROFILE" });
        if (dto.EasyAtWorkCustomerId <= 0)
            return BadRequest(new { error = "BAD_CUSTOMER_ID" });

        var cpExists = await _db.CompanyProfiles.AnyAsync(c => c.Id == dto.CompanyProfileId, ct);
        if (!cpExists) return NotFound(new { error = "COMPANY_PROFILE_NOT_FOUND" });

        // Duplikat-Schutz: Customer-ID darf nur einmal vergeben sein.
        var dup = await _db.EasyAtWorkBranchMappings
            .FirstOrDefaultAsync(m => m.EasyAtWorkCustomerId == dto.EasyAtWorkCustomerId
                                   && m.CompanyProfileId != dto.CompanyProfileId, ct);
        if (dup != null)
            return Conflict(new
            {
                error   = "EAW_CUSTOMER_ALREADY_MAPPED",
                message = $"Customer-ID {dto.EasyAtWorkCustomerId} ist bereits Filiale {dup.CompanyProfileId} zugeordnet."
            });

        var existing = await _db.EasyAtWorkBranchMappings
            .FirstOrDefaultAsync(m => m.CompanyProfileId == dto.CompanyProfileId, ct);

        if (existing == null)
        {
            existing = new EasyAtWorkBranchMapping
            {
                CompanyProfileId          = dto.CompanyProfileId,
                EasyAtWorkCustomerId      = dto.EasyAtWorkCustomerId,
                EasyAtWorkCustomerNumber  = dto.EasyAtWorkCustomerNumber,
                EasyAtWorkCustomerName    = dto.EasyAtWorkCustomerName,
                CreatedAt                 = DateTime.UtcNow,
                UpdatedAt                 = DateTime.UtcNow,
            };
            _db.EasyAtWorkBranchMappings.Add(existing);
        }
        else
        {
            existing.EasyAtWorkCustomerId      = dto.EasyAtWorkCustomerId;
            existing.EasyAtWorkCustomerNumber  = dto.EasyAtWorkCustomerNumber;
            existing.EasyAtWorkCustomerName    = dto.EasyAtWorkCustomerName;
            existing.UpdatedAt                 = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = existing.Id });
    }

    /// <summary>Mapping entfernen (per CompanyProfileId).</summary>
    [HttpDelete("mappings/{companyProfileId:int}")]
    public async Task<IActionResult> DeleteMapping(int companyProfileId, CancellationToken ct)
    {
        var row = await _db.EasyAtWorkBranchMappings
            .FirstOrDefaultAsync(m => m.CompanyProfileId == companyProfileId, ct);
        if (row == null) return NotFound();
        _db.EasyAtWorkBranchMappings.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ────────────────── Stempelzeit-Sync (Phase 2) ──────────────────

    public record SyncRequestDto(int CompanyProfileId, DateOnly From, DateOnly To);

    /// <summary>Dry-Run: zeigt, was importiert/dedupliziert/unmatched wäre. Schreibt nichts.</summary>
    [HttpPost("sync/timepunches/preview")]
    public async Task<IActionResult> SyncTimepunchesPreview([FromBody] SyncRequestDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var res = await _tpSync.PreviewAsync(new Services.EasyAtWork.EasyAtWorkTimepunchSyncService.SyncRequest
        {
            CompanyProfileId = dto.CompanyProfileId,
            From = dto.From,
            To = dto.To
        }, ct);
        return Ok(res);
    }

    /// <summary>Commit: schreibt die NEW-Zeilen in employee_time_entry.</summary>
    [HttpPost("sync/timepunches/commit")]
    public async Task<IActionResult> SyncTimepunchesCommit([FromBody] SyncRequestDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var res = await _tpSync.CommitAsync(new Services.EasyAtWork.EasyAtWorkTimepunchSyncService.SyncRequest
        {
            CompanyProfileId = dto.CompanyProfileId,
            From = dto.From,
            To = dto.To
        }, ct);
        return Ok(res);
    }

    // ────────────────── Mitarbeiter-Sync (Phase 3.1) ─────────────────

    public record EmpSyncRequestDto(int CompanyProfileId, DateOnly? ActiveAt, DateOnly? ExitedAfter, bool? IncludeAllInactive, List<string>? SelectedNumbers);

    /// <summary>Dry-Run für MA-Stammdaten — zeigt NEW/UPDATE/UNCHANGED/CONFLICT.</summary>
    [HttpPost("sync/employees/preview")]
    public async Task<IActionResult> SyncEmployeesPreview([FromBody] EmpSyncRequestDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var res = await _empSync.PreviewAsync(new Services.EasyAtWork.EasyAtWorkEmployeeSyncService.SyncRequest
        {
            CompanyProfileId = dto.CompanyProfileId,
            ActiveAt = dto.ActiveAt,
            ExitedAfter = dto.ExitedAfter,
            IncludeAllInactive = dto.IncludeAllInactive ?? false,
        }, ct);
        return Ok(res);
    }

    /// <summary>Commit: INSERTet NEW-MA + UPDATEt ausgewählte UPDATE-MA in employee.</summary>
    [HttpPost("sync/employees/commit")]
    public async Task<IActionResult> SyncEmployeesCommit([FromBody] EmpSyncRequestDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var res = await _empSync.CommitAsync(new Services.EasyAtWork.EasyAtWorkEmployeeSyncService.SyncRequest
        {
            CompanyProfileId = dto.CompanyProfileId,
            ActiveAt = dto.ActiveAt,
            ExitedAfter = dto.ExitedAfter,
            IncludeAllInactive = dto.IncludeAllInactive ?? false,
            SelectedNumbers = dto.SelectedNumbers,
        }, ct);
        return Ok(res);
    }

    // ──────────── easy@work-ID-Aliase (alte/zweite IDs pro MA) ───────────
    // Walter-Vorgabe 18.06.2026: Wenn die easy@work-employee_id eines MA
    // mittendrin wechselt, hängen alte Stempel an der alten ID. Hier hinterlegen
    // wir diese alten IDs, damit der Stempel-Sync sie auflösen kann.

    public record AliasCreateDto(int EasyAtWorkId, int CoworkEmployeeId, string? Note);

    /// <summary>Liste aller hinterlegten Alias-IDs (mit MA-Name).</summary>
    [HttpGet("aliases")]
    public async Task<IActionResult> GetAliases(CancellationToken ct)
    {
        var raw = await _db.EasyAtWorkEmployeeAliases.AsNoTracking()
            .OrderByDescending(a => a.Id)
            .Select(a => new
            {
                a.Id,
                a.EasyAtWorkId,
                a.EmployeeId,
                FirstName = a.Employee!.FirstName,
                LastName  = a.Employee.LastName,
                EmployeeNumber = a.Employee.EmployeeNumber,
                a.Note,
                a.CreatedAt,
                a.CreatedBy,
            })
            .ToListAsync(ct);
        var rows = raw.Select(a => new
        {
            a.Id,
            a.EasyAtWorkId,
            a.EmployeeId,
            employeeNumber = a.EmployeeNumber,
            employeeName   = ((a.FirstName ?? "") + " " + (a.LastName ?? "")).Trim(),
            a.Note,
            a.CreatedAt,
            a.CreatedBy,
        });
        return Ok(rows);
    }

    /// <summary>
    /// Hinterlegt eine alte/zweite easy@work-ID für einen MA (Upsert: dieselbe
    /// easy@work-ID kann nur EINEM MA gehören → vorhandener Eintrag wird umgehängt).
    /// </summary>
    [HttpPost("aliases")]
    public async Task<IActionResult> CreateAlias([FromBody] AliasCreateDto dto, CancellationToken ct)
    {
        if (dto.EasyAtWorkId <= 0) return BadRequest(new { error = "EAW_ID_INVALID", message = "Ungültige easy@work-ID." });
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == dto.CoworkEmployeeId, ct);
        if (emp == null) return NotFound(new { error = "EMPLOYEE_NOT_FOUND", message = "Mitarbeiter nicht gefunden." });

        var actor = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                 ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var existing = await _db.EasyAtWorkEmployeeAliases
            .FirstOrDefaultAsync(a => a.EasyAtWorkId == dto.EasyAtWorkId, ct);
        if (existing != null)
        {
            existing.EmployeeId = dto.CoworkEmployeeId;
            existing.Note       = dto.Note;
            existing.CreatedAt  = DateTime.UtcNow;
            existing.CreatedBy  = actor;
        }
        else
        {
            _db.EasyAtWorkEmployeeAliases.Add(new EasyAtWorkEmployeeAlias
            {
                EasyAtWorkId = dto.EasyAtWorkId,
                EmployeeId   = dto.CoworkEmployeeId,
                Note         = dto.Note,
                CreatedAt    = DateTime.UtcNow,
                CreatedBy    = actor,
            });
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    /// <summary>Entfernt einen Alias wieder.</summary>
    [HttpDelete("aliases/{id:int}")]
    public async Task<IActionResult> DeleteAlias(int id, CancellationToken ct)
    {
        var row = await _db.EasyAtWorkEmployeeAliases.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row == null) return NotFound();
        _db.EasyAtWorkEmployeeAliases.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

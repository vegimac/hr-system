using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.EasyAtWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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
    private readonly LohnEditLockService _editLock;

    public EasyAtWorkController(
        EasyAtWorkClient client,
        AppDbContext db,
        ILogger<EasyAtWorkController> log,
        Services.EasyAtWork.EasyAtWorkTimepunchSyncService tpSync,
        Services.EasyAtWork.EasyAtWorkEmployeeSyncService empSync,
        LohnEditLockService editLock)
    {
        _client = client;
        _db = db;
        _log = log;
        _tpSync = tpSync;
        _empSync = empSync;
        _editLock = editLock;
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
        bool    AutoSyncEnabled,
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
                m.AutoSyncEnabled,
                m.CreatedAt,
                m.UpdatedAt)
        ).ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>Mapping einer einzelnen Filiale (für den Filial-Einstellungen-Tab).</summary>
    [HttpGet("mappings/by-branch/{companyProfileId:int}")]
    public async Task<IActionResult> GetMappingByBranch(int companyProfileId, CancellationToken ct)
    {
        var dto = await (
            from m in _db.EasyAtWorkBranchMappings.AsNoTracking()
            join cp in _db.CompanyProfiles.AsNoTracking() on m.CompanyProfileId equals cp.Id
            where m.CompanyProfileId == companyProfileId
            select new BranchMappingDto(
                m.Id, m.CompanyProfileId, cp.BranchName ?? cp.CompanyName, cp.RestaurantCode,
                m.EasyAtWorkCustomerId, m.EasyAtWorkCustomerNumber, m.EasyAtWorkCustomerName,
                m.AutoSyncEnabled, m.CreatedAt, m.UpdatedAt)
        ).FirstOrDefaultAsync(ct);
        if (dto == null) return NotFound(new { error = "NOT_MAPPED" });

        // Sync-Status (Resource TIMEPUNCH) mitliefern — für die Anzeige beim
        // Auto-Sync-Schalter (Walter-Vorgabe 19.06.2026).
        var st = await _db.EasyAtWorkSyncStates.AsNoTracking()
            .Where(s => s.CompanyProfileId == companyProfileId && s.Resource == "TIMEPUNCH")
            .Select(s => new { s.LastSyncAt, s.LastSeenUpdatedAt, s.LastRowCount, s.LastError })
            .FirstOrDefaultAsync(ct);

        return Ok(new
        {
            dto.Id, dto.CompanyProfileId, dto.CompanyProfileName, dto.RestaurantCode,
            dto.EasyAtWorkCustomerId, dto.EasyAtWorkCustomerNumber, dto.EasyAtWorkCustomerName,
            dto.AutoSyncEnabled, dto.CreatedAt, dto.UpdatedAt,
            syncState = st == null ? null : new
            {
                lastSyncAt        = st.LastSyncAt,
                lastSeenUpdatedAt = st.LastSeenUpdatedAt,
                lastRowCount      = st.LastRowCount,
                lastError         = st.LastError,
            }
        });
    }

    public record AutoSyncToggleDto(bool Enabled);

    /// <summary>Auto-Sync für eine Filiale ein-/ausschalten (Filial-Einstellungen-Tab).</summary>
    [HttpPatch("mappings/{companyProfileId:int}/auto-sync")]
    public async Task<IActionResult> SetAutoSync(int companyProfileId, [FromBody] AutoSyncToggleDto dto, CancellationToken ct)
    {
        var row = await _db.EasyAtWorkBranchMappings
            .FirstOrDefaultAsync(m => m.CompanyProfileId == companyProfileId, ct);
        if (row == null) return NotFound(new { error = "NOT_MAPPED", message = "Filiale ist nicht mit easy@work verknüpft." });
        row.AutoSyncEnabled = dto.Enabled;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true, autoSyncEnabled = row.AutoSyncEnabled });
    }

    // ──────────── Auto-Sync-Protokoll (Admin-Ansicht) ────────────────
    public record SyncLogDto(
        int Id, int CompanyProfileId, string? CompanyProfileName, DateTime RunAt,
        string Status, DateOnly? PeriodFrom, DateOnly? PeriodTo, bool UsedUpdatesFeed,
        int Inserted, int Updated, int Deleted, int LockedSkipped, int Skipped,
        int MissingCount, string? Message, bool HasDetail);

    /// <summary>Protokoll des automatischen Sync (neueste zuerst), optional pro Filiale.</summary>
    [HttpGet("sync-log")]
    public async Task<IActionResult> GetSyncLog(
        [FromQuery] int? branchId, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (limit <= 0 || limit > 500) limit = 100;

        var logs = await _db.EasyAtWorkSyncLogs.AsNoTracking()
            .Where(l => !branchId.HasValue || l.CompanyProfileId == branchId.Value)
            .OrderByDescending(l => l.RunAt)
            .Take(limit)
            .ToListAsync(ct);

        var cpIds = logs.Select(l => l.CompanyProfileId).Distinct().ToList();
        var names = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => cpIds.Contains(c.Id))
            .Select(c => new { c.Id, Name = c.BranchName ?? c.CompanyName })
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var rows = logs.Select(l => new SyncLogDto(
            l.Id, l.CompanyProfileId,
            names.TryGetValue(l.CompanyProfileId, out var n) ? n : null,
            l.RunAt, l.Status, l.PeriodFrom, l.PeriodTo, l.UsedUpdatesFeed,
            l.Inserted, l.Updated, l.Deleted, l.LockedSkipped, l.Skipped,
            l.MissingCount, l.Message, !string.IsNullOrEmpty(l.DetailJson))).ToList();
        return Ok(rows);
    }

    /// <summary>Detail der echten Änderungen eines Sync-Laufs (Variante A) —
    /// reichert die gespeicherten Zeilen mit MA-Name/-Nummer an.</summary>
    [HttpGet("sync-log/{id:int}/detail")]
    public async Task<IActionResult> GetSyncLogDetail(int id, CancellationToken ct = default)
    {
        var log = await _db.EasyAtWorkSyncLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (log == null) return NotFound();
        if (string.IsNullOrEmpty(log.DetailJson))
            return Ok(new { totalChanges = 0, capped = false, changes = Array.Empty<object>() });

        JsonElement root;
        try { root = JsonDocument.Parse(log.DetailJson).RootElement; }
        catch { return Ok(new { totalChanges = 0, capped = false, changes = Array.Empty<object>() }); }

        var rawChanges = root.TryGetProperty("changes", out var ch) && ch.ValueKind == JsonValueKind.Array
            ? ch.EnumerateArray().ToList() : new List<JsonElement>();
        var empIds = rawChanges
            .Where(c => c.TryGetProperty("empId", out _))
            .Select(c => c.GetProperty("empId").GetInt32()).Distinct().ToList();
        var emps = await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber })
            .ToDictionaryAsync(e => e.Id, ct);

        decimal? Dec(JsonElement c, string k) => c.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : (decimal?)null;
        var changes = rawChanges.Select(c =>
        {
            int eid = c.GetProperty("empId").GetInt32();
            emps.TryGetValue(eid, out var e);
            return new {
                employeeId = eid,
                name       = e != null ? $"{e.FirstName} {e.LastName}".Trim() : $"MA-{eid}",
                number     = e?.EmployeeNumber,
                date       = c.TryGetProperty("date", out var d) ? d.GetString() : null,
                action     = c.TryGetProperty("action", out var a) ? a.GetString() : null,
                oldTotal   = Dec(c, "oldTotal"), newTotal = Dec(c, "newTotal"),
                oldNight   = Dec(c, "oldNight"), newNight = Dec(c, "newNight")
            };
        })
        .OrderBy(x => x.name).ThenBy(x => x.date).ToList();

        return Ok(new {
            totalChanges = root.TryGetProperty("totalChanges", out var tc) ? tc.GetInt32() : changes.Count,
            capped       = root.TryGetProperty("capped", out var cp) && cp.GetBoolean(),
            runAt        = log.RunAt,
            changes
        });
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

    public record SyncRequestDto(int CompanyProfileId, DateOnly From, DateOnly To, List<int>? SkipEawEmployeeIds = null, DateOnly? EmployeeCutoffOverride = null, bool? IgnoreMissing = null);

    /// <summary>Dry-Run: zeigt, was importiert/dedupliziert/unmatched wäre. Schreibt nichts.</summary>
    [HttpPost("sync/timepunches/preview")]
    public async Task<IActionResult> SyncTimepunchesPreview([FromBody] SyncRequestDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var res = await _tpSync.PreviewAsync(new Services.EasyAtWork.EasyAtWorkTimepunchSyncService.SyncRequest
        {
            CompanyProfileId = dto.CompanyProfileId,
            From = dto.From,
            To = dto.To,
            SkipEawEmployeeIds = dto.SkipEawEmployeeIds ?? new(),
            EmployeeCutoffOverride = dto.EmployeeCutoffOverride
        }, ct);
        return Ok(res);
    }

    /// <summary>
    /// Direkt-Nachschlag eines easy@work-MA per ID (Walter-Vorgabe 20.06.2026) —
    /// auch wenn er nicht mehr in der MA-Liste steht. Liefert Name/Nummer (falls
    /// die API ihn noch hergibt) und schlägt — bei vorhandener Nummer — den
    /// passenden Cowork-MA zum Zuordnen vor.
    /// </summary>
    [HttpGet("employee-lookup")]
    public async Task<IActionResult> EmployeeLookup([FromQuery] int companyProfileId, [FromQuery] int eawId, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var mapping = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyProfileId == companyProfileId, ct);
        if (mapping == null) return NotFound(new { error = "NO_MAPPING" });

        var emp = await _client.GetEmployeeByIdAsync(mapping.EasyAtWorkCustomerId, eawId, ct);
        if (emp == null)
            return Ok(new { found = false, eawId });

        var name = $"{emp.FirstName} {emp.LastName}".Trim();
        int? coworkId = null; string? coworkName = null;
        if (!string.IsNullOrWhiteSpace(emp.Number))
        {
            var nr = emp.Number!.Trim();
            var co = await _db.Employees.AsNoTracking()
                .Where(e => e.EmployeeNumber == nr)
                .Select(e => new { e.Id, e.FirstName, e.LastName }).FirstOrDefaultAsync(ct);
            if (co != null) { coworkId = co.Id; coworkName = $"{co.FirstName} {co.LastName}".Trim(); }
        }
        return Ok(new
        {
            found = true, eawId, number = emp.Number, name,
            coworkEmployeeId = coworkId, coworkName
        });
    }

    /// <summary>Commit: schreibt die NEW-Zeilen in employee_time_entry.</summary>
    [HttpPost("sync/timepunches/commit")]
    public async Task<IActionResult> SyncTimepunchesCommit([FromBody] SyncRequestDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });

        // Import-Sperre läuft seit Walter-Vorgabe 20.06.2026 PRO PERIODE im Sync-
        // Service (IsImportable gegen ABGESCHLOSSENE Lohnperioden) statt über den
        // monotonen LohnEditLockService — sonst hätte eine laufende 2026-Periode
        // den ganzen historischen 2025-Import gesperrt. Davor/danach (inkl. 2025)
        // ist erlaubt, nur innerhalb abgeschlossener Perioden nicht. Der hier
        // übergebene Wert wird vom Service ignoriert (per-Periode-Logik gilt).
        DateOnly? firstAllowed = null;
        var res = await _tpSync.CommitAsync(new Services.EasyAtWork.EasyAtWorkTimepunchSyncService.SyncRequest
        {
            CompanyProfileId = dto.CompanyProfileId,
            From = dto.From,
            To = dto.To,
            SkipEawEmployeeIds = dto.SkipEawEmployeeIds ?? new(),
            EmployeeCutoffOverride = dto.EmployeeCutoffOverride,
            IgnoreMissing = dto.IgnoreMissing ?? false
        }, firstAllowed, ct);
        return Ok(res);
    }

    // Liefert die gemappten Filialen (Id + Name) — fürs Frontend, das den
    // historischen Batch-Import Filiale-für-Filiale + Fenster-für-Fenster fährt
    // (kurze Requests, kein Gateway-Timeout). Walter 21.06.2026.
    [HttpGet("mapped-branches")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> MappedBranches(CancellationToken ct)
    {
        var ids = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .Select(m => m.CompanyProfileId).Distinct().ToListAsync(ct);
        var rows = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { id = c.Id, name = string.IsNullOrWhiteSpace(c.BranchName) ? c.CompanyName : c.BranchName })
            .ToListAsync(ct);
        return Ok(rows.OrderBy(r => r.name));
    }

    // ────────────────── Mitarbeiter-Sync (Phase 3.1) ─────────────────

    // OnlyActive ersetzt das frühere „Austritt nach"-Datumsfeld (Walter 19.06.2026):
    // true = nur aktive, false/null = alle (inkl. ausgetretene, ohne Pre-2025).
    public record EmpSyncRequestDto(int CompanyProfileId, DateOnly? ActiveAt, DateOnly? ExitedAfter, bool? IncludeAllInactive, bool? OnlyActive, List<string>? SelectedNumbers);

    /// <summary>Dry-Run für MA-Stammdaten — zeigt NEW/UPDATE/UNCHANGED/CONFLICT.</summary>
    [HttpPost("sync/employees/preview")]
    public async Task<IActionResult> SyncEmployeesPreview([FromBody] EmpSyncRequestDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var res = await _empSync.PreviewAsync(new Services.EasyAtWork.EasyAtWorkEmployeeSyncService.SyncRequest
        {
            CompanyProfileId = dto.CompanyProfileId,
            OnlyActive = dto.OnlyActive ?? false,
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
            OnlyActive = dto.OnlyActive ?? false,
            SelectedNumbers = dto.SelectedNumbers,
        }, ct);
        return Ok(res);
    }

    public record InitialImportDto(DateOnly? Since);
    public record InitialImportBranchDto(int CompanyProfileId, DateOnly? Since);

    /// <summary>
    /// Tief-Import für EINE Filiale (Walter-Vorgabe 21.06.2026) — damit das
    /// Frontend den Lauf Filiale-für-Filiale fahren und live anzeigen kann, an
    /// welcher es gerade arbeitet (kein „hängt es?"-Eindruck, kein Gateway-
    /// Timeout). Gleiche Logik wie initial-import, nur pro Filiale.
    /// </summary>
    [HttpPost("sync/employees/initial-import-branch")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> SyncEmployeesInitialImportBranch([FromBody] InitialImportBranchDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var since = dto.Since ?? new DateOnly(2021, 1, 1);
        var res = await _empSync.CommitAsync(new Services.EasyAtWork.EasyAtWorkEmployeeSyncService.SyncRequest
        {
            CompanyProfileId          = dto.CompanyProfileId,
            OnlyActive                = false,
            EmployeeCutoffOverride    = since,
            AltSuffixForPreMirusExits = true,
        }, ct);
        return Ok(new { companyProfileId = dto.CompanyProfileId, inserted = res.CountInserted, updated = res.CountUpdated, total = res.CountTotal });
    }

    /// <summary>
    /// EINMALIGER Tief-Import (Walter-Vorgabe 21.06.2026): importiert ALLE
    /// inaktiven MA ALLER gemappten Filialen zurück bis zum Stichtag (Default
    /// 1.1.2021), AUTOMATISCH ohne Vorschau. Pre-Mirus-Austritte (Austritt vor
    /// 1.1.2025) bekommen den „alt"-Suffix an die Personalnummer, damit sie
    /// nicht mit den aktuellen Mirus-Nummern kollidieren. Danach lassen sich die
    /// alten Stempelzeiten dieser MA importieren.
    /// </summary>
    [HttpPost("sync/employees/initial-import")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> SyncEmployeesInitialImport([FromBody] InitialImportDto? dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var since = dto?.Since ?? new DateOnly(2021, 1, 1);

        var branchIds = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .Select(m => m.CompanyProfileId).Distinct().ToListAsync(ct);
        var branchNames = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => branchIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => string.IsNullOrWhiteSpace(c.BranchName) ? c.CompanyName : c.BranchName, ct);

        int totalInserted = 0, totalUpdated = 0;
        var perBranch = new List<object>();
        foreach (var cpId in branchIds)
        {
            var branchName = branchNames.TryGetValue(cpId, out var nm) ? nm : $"#{cpId}";
            try
            {
                var res = await _empSync.CommitAsync(new Services.EasyAtWork.EasyAtWorkEmployeeSyncService.SyncRequest
                {
                    CompanyProfileId          = cpId,
                    OnlyActive                = false,
                    EmployeeCutoffOverride    = since,
                    AltSuffixForPreMirusExits = true,
                }, ct);
                totalInserted += res.CountInserted;
                totalUpdated  += res.CountUpdated;
                perBranch.Add(new { companyProfileId = cpId, branch = branchName, inserted = res.CountInserted, updated = res.CountUpdated, total = res.CountTotal });
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Initial-Import Filiale {Cp} fehlgeschlagen", cpId);
                perBranch.Add(new { companyProfileId = cpId, branch = branchName, error = ex.Message });
            }
        }
        return Ok(new { since, branches = branchIds.Count, totalInserted, totalUpdated, perBranch });
    }

    /// <summary>
    /// On-Demand: liefert für EINEN easy@work-MA die fiscal_info (Bewilligung,
    /// Bank, Ehepartner-Permit) + ALLE Custom Fields/Properties (key+value) —
    /// read-only, zur Anzeige/Validierung in der MA-Sync-Vorschau (Walter
    /// 19.06.2026). Schreibt nichts. Pro Klick nur 2 API-Aufrufe (nicht N×2).
    /// </summary>
    [HttpGet("employees/{companyProfileId:int}/{eawEmployeeId:int}/detail")]
    public async Task<IActionResult> GetEmployeeEasyworkDetail(int companyProfileId, int eawEmployeeId, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var mapping = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyProfileId == companyProfileId, ct);
        if (mapping == null) return NotFound(new { error = "NOT_MAPPED" });

        Services.EasyAtWork.EawFiscalInfo? fiscal = null;
        var props  = new List<Services.EasyAtWork.EawProperty>();
        var notes  = new List<string>();
        try { fiscal = await _client.GetFiscalInfoAsync(mapping.EasyAtWorkCustomerId, eawEmployeeId, ct); }
        catch (Exception ex) { notes.Add($"fiscal_info nicht abrufbar: {ex.Message}"); }
        try { props = await _client.GetAllPropertiesAsync(mapping.EasyAtWorkCustomerId, eawEmployeeId, ct); }
        catch (Exception ex) { notes.Add($"properties nicht abrufbar: {ex.Message}"); }

        return Ok(new
        {
            fiscal,
            properties = props
                .OrderBy(p => p.Key)
                .Select(p => new { key = p.Key, value = p.Value, from = p.From, to = p.To }),
            notes
        });
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

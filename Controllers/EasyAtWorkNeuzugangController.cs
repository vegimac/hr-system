using HrSystem.Data;
using HrSystem.Services;
using HrSystem.Services.EasyAtWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Eingeschränkter Einzelimport aus easy@work für die Mitarbeiter-Verwaltung
/// (Walter-Vorgabe 08.07.2026). Anwendungsfall: der GF erfasst einen
/// Neueintritt in easy@work und holt ihn danach hier ins System, um weitere
/// Angaben zu erfassen.
///
/// Unterschiede zum Admin-Massen-Sync (EasyAtWorkController, bleibt unverändert):
///   - IMMER OnlyActive=true → inaktive MA (Austritt in easy@work vor heute)
///     werden gar nicht erst geholt und NIE angefasst.
///   - Die Vorschau liefert NUR NEW- (noch nicht vorhanden) und UPDATE-Zeilen
///     (aktive MA mit Änderungen) — UNCHANGED wird weggelassen; CONFLICT nur
///     als Warn-Info.
///   - Commit verlangt eine explizite Auswahl (SelectedNumbers) — kein
///     versehentlicher Massen-Write.
///   - Zugänglich für admin/superuser/user/buchhaltung; user + buchhaltung
///     nur für ihre zugeteilten Filialen (CanAccessBranchAsync).
///   - NEW-Personalnummern: harte Folge max+1…max+N (Walter 03.08.2026).
///
/// Schreibpfad = derselbe EasyAtWorkEmployeeSyncService wie der Admin-Sync
/// (inkl. Perioden-Schutz: Verträge in abgeschlossenen Perioden landen in
/// SkippedContracts statt geschrieben zu werden).
/// </summary>
[ApiController]
[Route("api/easywork/neuzugang")]
[Authorize(Roles = "admin,superuser,user,buchhaltung")]
public class EasyAtWorkNeuzugangController : HrControllerBase
{
    private readonly EasyAtWorkClient _client;
    private readonly EasyAtWorkEmployeeSyncService _empSync;

    public EasyAtWorkNeuzugangController(AppDbContext db, EasyAtWorkClient client,
                                         EasyAtWorkEmployeeSyncService empSync) : base(db)
    {
        _client = client;
        _empSync = empSync;
    }

    public record NeuzugangDto(int CompanyProfileId, List<string>? SelectedNumbers);

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] NeuzugangDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        if (dto == null || dto.CompanyProfileId <= 0)
            return BadRequest(new { error = "Bitte companyProfileId angeben." });
        if (!await CanAccessBranchAsync(dto.CompanyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });

        var res = await _empSync.PreviewAsync(new EasyAtWorkEmployeeSyncService.SyncRequest
        {
            CompanyProfileId = dto.CompanyProfileId,
            OnlyActive       = true,   // ABSOLUT: inaktive MA werden nie angefasst
        }, ct);

        // Nur die relevanten Zeilen: neue MA + Updates aktiver MA.
        var rows = res.Rows
            .Where(r => r.Status == "NEW" || r.Status == "UPDATE")
            .Select(r => new
            {
                r.Number,
                r.FirstName,
                r.LastName,
                r.Status,
                r.Reason,
                changedFields = r.Diffs.Where(d => d.WillSet).Select(d => d.Field).ToList(),
                r.EmploymentInfo,
                r.PossibleReentry,
                r.ReentryEmployeeNumber,
            })
            .OrderBy(r => r.Status == "NEW" ? 0 : 1)
            .ThenBy(r => r.FirstName).ThenBy(r => r.LastName)
            .ToList();

        var conflicts = res.Rows.Where(r => r.Status == "CONFLICT")
            .Select(r => $"{r.FirstName} {r.LastName} ({r.Number}): {r.Reason}".Trim())
            .ToList();

        var seq = await BuildNumberSequenceInfoAsync(dto.CompanyProfileId, ct);

        return Ok(new
        {
            rows,
            countNew    = res.CountNew,
            countUpdate = res.CountUpdate,
            conflicts,
            notes = res.Notes,
            numberSequence = seq,
        });
    }

    [HttpPost("commit")]
    public async Task<IActionResult> Commit([FromBody] NeuzugangDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        if (dto == null || dto.CompanyProfileId <= 0)
            return BadRequest(new { error = "Bitte companyProfileId angeben." });
        if (!await CanAccessBranchAsync(dto.CompanyProfileId))
            return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        if (dto.SelectedNumbers == null || dto.SelectedNumbers.Count == 0)
            return BadRequest(new { error = "Bitte mindestens einen Mitarbeitenden auswählen." });

        // Harte Nummernfolge nur für NEW (noch nicht in OneCrew). UPDATE bleibt frei.
        var selected = dto.SelectedNumbers
            .Select(n => (n ?? "").Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingNums = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden && e.EmployeeNumber != null && e.EmployeeNumber != "")
            .Select(e => e.EmployeeNumber!)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(
            existingNums.Select(n => n.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var newSelected = selected.Where(n => !existingSet.Contains(n)).ToList();
        if (newSelected.Count > 0)
        {
            var seq = await BuildNumberSequenceInfoAsync(dto.CompanyProfileId, ct);
            if (!EmployeeNumberSequenceGuard.TryValidate(
                    newSelected, seq.MaxExisting, out var msg, out var expected, out var received))
            {
                return Conflict(new
                {
                    error = "NUMBER_SEQUENCE_INVALID",
                    message = msg,
                    maxExisting = seq.MaxExisting,
                    prefix = seq.Prefix,
                    expected = expected.Select(x => x.ToString()).ToList(),
                    received = received.Select(x => x.ToString()).ToList(),
                });
            }
        }

        var res = await _empSync.CommitAsync(new EasyAtWorkEmployeeSyncService.SyncRequest
        {
            CompanyProfileId = dto.CompanyProfileId,
            OnlyActive       = true,   // ABSOLUT: inaktive MA werden nie angefasst
            SelectedNumbers  = dto.SelectedNumbers,
        }, ct: ct);

        return Ok(new
        {
            inserted = res.CountInserted,
            updated  = res.CountUpdated,
            blocked  = res.Blocked,
            numberConflicts = res.NumberConflicts,
            skippedContracts = res.SkippedContracts,
            notes = res.Notes,
        });
    }

    private async Task<NumberSequenceInfo> BuildNumberSequenceInfoAsync(int companyProfileId, CancellationToken ct)
    {
        var restaurantCode = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => c.Id == companyProfileId)
            .Select(c => c.RestaurantCode)
            .FirstOrDefaultAsync(ct);
        var prefix = EmployeeNumberSequenceGuard.NormalizeRestaurantPrefix(restaurantCode);
        var nums = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden && e.EmployeeNumber != null && e.EmployeeNumber != "")
            .Select(e => e.EmployeeNumber!)
            .ToListAsync(ct);
        var max = EmployeeNumberSequenceGuard.FindMaxExisting(nums, prefix);
        return new NumberSequenceInfo(prefix, max, restaurantCode);
    }

    private sealed record NumberSequenceInfo(string Prefix, long? MaxExisting, string? RestaurantCode);
}

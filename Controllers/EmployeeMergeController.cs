using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Einmalige Bereinigung: mehrfache Employee-Einträge zur selben easy@work-ID
/// zusammenführen (Walter-Vorgabe 21.06.2026). Ein Mensch = ein Employee.
/// Reine Stammdaten-/Aufräum-Operation → im Edit-Lock-Audit whitelisted.
/// </summary>
[ApiController]
[Route("api/employee-merge")]
[Authorize(Roles = "admin")]
public class EmployeeMergeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<EmployeeMergeController> _log;
    public EmployeeMergeController(AppDbContext db, ILogger<EmployeeMergeController> log) { _db = db; _log = log; }

    // Alle Tabellen mit employee_id, deren Zeilen beim Merge auf den Haupt-MA
    // umgehängt werden. Wird zur Laufzeit gegen information_schema gefiltert,
    // damit nicht-existierende Tabellen/Spalten den Merge nicht sprengen.
    private static readonly string[] LinkedTables = new[]
    {
        "employment",
        "employee_time_entry",
        "absence",
        "employee_quellensteuer",
        "employee_bank_account",
        "employee_family_member",
        "employee_dokument",
        "employee_pregnancy",
        "employee_bvg_zusatz_member",
        "employee_education_history",
        "employee_import_snapshot",
        "employee_lohn_durchschnitt",
        "employee_recurring_wage",
        "lohn_zulage",
        "payroll_saldo",
        "payroll_snapshot",
        "krankheit_karenz_saldo",
        "employee_arbeitslosigkeit",
        "employee_lohn_assignment",
        "employee_permit_history",
        "easyatwork_employee_alias",
        "employee_number_alias",
    };

    private static string StripAlt(string n)
    {
        var t = (n ?? "").Trim();
        return t.Length >= 3 && t[^3..].Equals("alt", StringComparison.OrdinalIgnoreCase) ? t[..^3].Trim() : t;
    }

    private async Task<List<string>> ExistingLinkedTablesAsync(CancellationToken ct)
    {
        var rows = await _db.Database.SqlQueryRaw<string>(
            "SELECT table_name AS \"Value\" FROM information_schema.columns " +
            "WHERE column_name = 'employee_id' AND table_name = ANY({0})", new object[] { LinkedTables })
            .ToListAsync(ct);
        return rows;
    }

    // ─────────────────────────── Duplikate auflisten ───────────────────────────

    [HttpGet("duplicates")]
    public async Task<IActionResult> Duplicates(CancellationToken ct)
    {
        var emps = await _db.Employees.AsNoTracking()
            .Where(e => e.EasyAtWorkEmployeeId != null && !e.IsHidden)
            .Select(e => new
            {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
                e.EntryDate, e.ExitDate, e.IsActive, e.IsPayrollExcluded,
                EawId = e.EasyAtWorkEmployeeId!.Value,
                BranchIds = e.Employments.Where(em => em.CompanyProfileId != null)
                    .Select(em => em.CompanyProfileId!.Value).ToList()
            })
            .ToListAsync(ct);

        var branchNames = await _db.CompanyProfiles.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => string.IsNullOrWhiteSpace(c.BranchName) ? c.CompanyName : c.BranchName, ct);

        var groups = emps.GroupBy(e => e.EawId).Where(g => g.Count() > 1)
            .Select(g =>
            {
                // Haupt-MA-Vorschlag: aktiv > neuester Eintritt > niedrigste Id.
                var suggested = g.OrderByDescending(e => e.IsActive)
                                 .ThenByDescending(e => e.EntryDate ?? DateTime.MinValue)
                                 .ThenBy(e => e.Id).First().Id;
                return new
                {
                    easyAtWorkId = g.Key,
                    name = $"{g.First().FirstName} {g.First().LastName}".Trim(),
                    employees = g.OrderBy(e => e.Id).Select(e => new
                    {
                        e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
                        e.EntryDate, e.ExitDate, e.IsActive, e.IsPayrollExcluded,
                        branches = e.BranchIds.Distinct().Select(b => branchNames.TryGetValue(b, out var n) ? n : $"#{b}").ToList(),
                        isSuggestedMain = e.Id == suggested
                    }).ToList()
                };
            })
            .OrderByDescending(x => x.employees.Count).ThenBy(x => x.name)
            .ToList();

        return Ok(new { count = groups.Count, groups });
    }

    // ─────────────────────────── Vorschau + Merge ───────────────────────────

    public record MergeDto(int MainEmployeeId, List<int> DuplicateEmployeeIds, bool DryRun = false);

    [HttpPost]
    public async Task<IActionResult> Merge([FromBody] MergeDto dto, CancellationToken ct)
    {
        if (dto.DuplicateEmployeeIds == null || dto.DuplicateEmployeeIds.Count == 0)
            return BadRequest(new { error = "NO_DUPLICATES", message = "Keine Duplikate angegeben." });
        var dupIds = dto.DuplicateEmployeeIds.Where(id => id != dto.MainEmployeeId).Distinct().ToList();
        if (dupIds.Count == 0)
            return BadRequest(new { error = "NO_DUPLICATES", message = "Keine zu mergenden Duplikate (nur Haupt-MA)." });

        var main = await _db.Employees.FirstOrDefaultAsync(e => e.Id == dto.MainEmployeeId, ct);
        if (main == null) return NotFound(new { error = "MAIN_NOT_FOUND" });
        var dups = await _db.Employees.Where(e => dupIds.Contains(e.Id)).ToListAsync(ct);
        if (dups.Count != dupIds.Count) return NotFound(new { error = "DUP_NOT_FOUND", message = "Mind. ein Duplikat nicht gefunden." });

        // Sicherheit: alle müssen dieselbe easy@work-ID teilen.
        var eawId = main.EasyAtWorkEmployeeId;
        if (eawId == null || dups.Any(d => d.EasyAtWorkEmployeeId != eawId))
            return Conflict(new { error = "EAW_ID_MISMATCH", message = "Haupt-MA und Duplikate teilen nicht dieselbe easy@work-ID." });

        var tables = await ExistingLinkedTablesAsync(ct);

        // Welche Nummern werden zu Aliasen (alt-Suffix entfernt, dedupliziert,
        // nicht die Haupt-Nummer, noch nicht vorhanden).
        var mainNum = (main.EmployeeNumber ?? "").Trim();
        var existingAliasNums = (await _db.EmployeeNumberAliases.AsNoTracking()
                .Where(a => a.EmployeeId == main.Id).Select(a => a.Number).ToListAsync(ct))
            .Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var aliasNumbers = new List<string>();
        foreach (var d in dups)
        {
            var n = StripAlt(d.EmployeeNumber ?? "");
            if (string.IsNullOrWhiteSpace(n)) continue;
            if (string.Equals(n, mainNum, StringComparison.OrdinalIgnoreCase)) continue;
            if (existingAliasNums.Contains(n)) continue;
            if (aliasNumbers.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase))) continue;
            aliasNumbers.Add(n);
        }

        var dupArr = dupIds.ToArray();

        // ── Vorschau (dry run): pro Tabelle zählen, was umgehängt würde ──────────
        if (dto.DryRun)
        {
            var moves = new List<object>();
            foreach (var t in tables)
            {
                // {t} = Tabellenname aus der fest verdrahteten LinkedTables-Whitelist,
                // zusätzlich gegen information_schema gefiltert → KEIN Benutzer-Input,
                // SQL-Injection ausgeschlossen. Walter 21.06.2026.
#pragma warning disable EF1002
                var cnt = await _db.Database.SqlQueryRaw<int>(
                    $"SELECT count(*)::int AS \"Value\" FROM {t} WHERE employee_id = ANY({{0}})", new object[] { dupArr })
                    .FirstAsync(ct);
#pragma warning restore EF1002
                if (cnt > 0) moves.Add(new { table = t, rows = cnt });
            }
            return Ok(new
            {
                dryRun = true,
                main = new { main.Id, main.EmployeeNumber, name = $"{main.FirstName} {main.LastName}".Trim() },
                duplicateIds = dupIds,
                aliasNumbers,
                moves
            });
        }

        // ── Merge ausführen (in einer Transaktion) ──────────────────────────────
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // {t} = Whitelist-Tabellenname (siehe DryRun-Kommentar) → kein Injection-Risiko.
#pragma warning disable EF1002
            foreach (var t in tables)
                await _db.Database.ExecuteSqlRawAsync(
                    $"UPDATE {t} SET employee_id = {{0}} WHERE employee_id = ANY({{1}})",
                    new object[] { main.Id, dupArr }, ct);
#pragma warning restore EF1002

            // Alte Personalnummern als Alias am Haupt-MA sichern.
            foreach (var n in aliasNumbers)
                _db.EmployeeNumberAliases.Add(new EmployeeNumberAlias
                {
                    EmployeeId = main.Id, Number = n, ValidTo = DateOnly.FromDateTime(DateTime.Today),
                    Source = "merge", CreatedAt = DateTime.UtcNow,
                });
            await _db.SaveChangesAsync(ct);

            // Duplikat-Employee-Zeilen löschen.
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM employee WHERE id = ANY({0})", new object[] { dupArr }, ct);

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _log.LogError(ex, "Merge fehlgeschlagen: main={Main} dups={Dups}", main.Id, string.Join(",", dupIds));
            return Conflict(new { error = "MERGE_FAILED", message = "Zusammenführung fehlgeschlagen (zurückgerollt): " + ex.Message });
        }

        _log.LogInformation("Merge OK: easy@work-ID {Eaw} → Haupt-MA {Main} ({Num}); zusammengeführt: {Dups}; Aliase: {Aliases}",
            eawId, main.Id, mainNum, string.Join(",", dupIds), string.Join(",", aliasNumbers));

        return Ok(new
        {
            ok = true,
            mainEmployeeId = main.Id,
            mergedCount = dupIds.Count,
            aliasNumbers,
            message = $"{dupIds.Count} Duplikat(e) auf {mainNum} zusammengeführt. {aliasNumbers.Count} alte Nummer(n) als Alias gesichert."
        });
    }
}

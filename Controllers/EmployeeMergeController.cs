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

    // Normalisierter Namensteil (getrimmt, klein, Mehrfach-Leerzeichen kollabiert)
    // für den Personen-Vergleich Vorname+Nachname+Geburtsdatum.
    private static string NameKey(string? s) => string.Join(' ',
        (s ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? PersonKey(string? first, string? last, DateTime? dob) =>
        dob.HasValue && !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(last)
            ? NameKey(first) + "|" + NameKey(last) + "|" + dob.Value.ToString("yyyyMMdd")
            : null;

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
        var raw = await _db.Employees.AsNoTracking()
            .Where(e => e.EasyAtWorkEmployeeId != null && !e.IsHidden)
            .Select(e => new
            {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.DateOfBirth,
                e.EntryDate, e.ExitDate, e.IsActive, e.IsPayrollExcluded,
                EawId = e.EasyAtWorkEmployeeId!.Value,
                LatestContractStart = e.Employments.Max(em => (DateTime?)em.ContractStartDate),
                BranchIds = e.Employments.Where(em => em.CompanyProfileId != null)
                    .Select(em => em.CompanyProfileId!.Value).ToList()
            })
            .ToListAsync(ct);

        var emps = raw.Select(e => new DupEmp(e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
            e.DateOfBirth, e.EntryDate, e.ExitDate, e.IsActive, e.IsPayrollExcluded, e.EawId,
            e.LatestContractStart, e.BranchIds)).ToList();

        var branchNames = await _db.CompanyProfiles.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => string.IsNullOrWhiteSpace(c.BranchName) ? c.CompanyName : c.BranchName, ct);

        // Haupt-MA-Vorschlag = die aktuelle Personalnummer, d.h. der Eintrag mit dem
        // NEUESTEN Vertrag (Walter-Vorgabe 05.07.2026: bei Filialwechsel gilt die Nummer,
        // hinter der der jüngste Vertrag liegt). Tie-Break: aktiv > neuester Eintritt > Id.
        DupGroup BuildGroup(IEnumerable<DupEmp> src, string reason, int? eawKey)
        {
            var list = src.ToList();
            var suggested = list.OrderByDescending(e => e.LatestContractStart ?? DateTime.MinValue)
                                .ThenByDescending(e => e.IsActive)
                                .ThenByDescending(e => e.EntryDate ?? DateTime.MinValue)
                                .ThenBy(e => e.Id).First().Id;
            var first = list[0];
            var employees = list.OrderBy(e => e.Id).Select(e => new
            {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.DateOfBirth,
                e.EntryDate, e.ExitDate, e.IsActive, e.IsPayrollExcluded,
                easyAtWorkId = e.EawId,
                latestContractStart = e.LatestContractStart,
                branches = e.BranchIds.Distinct().Select(b => branchNames.TryGetValue(b, out var n) ? n : $"#{b}").ToList(),
                isSuggestedMain = e.Id == suggested
            }).ToList();
            return new DupGroup(reason, eawKey, $"{first.FirstName} {first.LastName}".Trim(),
                first.DateOfBirth, employees, employees.Count);
        }

        // (1) Gleiche Person: Vorname + Nachname + Geburtsdatum identisch — egal welche
        //     easy@work-ID (deckt Wiedereintritt mit neuer easy@work-ID ab).
        var personRaw = emps
            .Where(e => PersonKey(e.FirstName, e.LastName, e.DateOfBirth) != null)
            .GroupBy(e => PersonKey(e.FirstName, e.LastName, e.DateOfBirth)!)
            .Where(g => g.Count() > 1)
            .ToList();
        var inPerson = personRaw.SelectMany(g => g).Select(e => e.Id).ToHashSet();
        var personGroups = personRaw.Select(g => BuildGroup(g, "person", null)).ToList();

        // (2) Gleiche easy@work-ID (klassischer Doppel-Import) — ohne die schon
        //     über Name+Geburtsdatum erfassten MA.
        var idGroups = emps.Where(e => !inPerson.Contains(e.Id))
            .GroupBy(e => e.EawId).Where(g => g.Count() > 1)
            .Select(g => BuildGroup(g, "easyId", g.Key))
            .ToList();

        var groups = personGroups.Concat(idGroups)
            .OrderByDescending(x => x.Size).ThenBy(x => x.Name)
            .ToList();

        return Ok(new { count = groups.Count, groups });
    }

    // ─────────────────────────── Vorschau + Merge ───────────────────────────

    // In-Memory-Projektion eines MA für die Duplikat-Erkennung.
    private sealed record DupEmp(int Id, string? EmployeeNumber, string? FirstName, string? LastName,
        DateTime? DateOfBirth, DateTime? EntryDate, DateTime? ExitDate, bool IsActive, bool IsPayrollExcluded,
        int EawId, DateTime? LatestContractStart, List<int> BranchIds);

    // Ein erkannter Duplikat-Fall für die UI. MatchReason: "person" (Name+Geb) | "easyId".
    private sealed record DupGroup(string MatchReason, int? EasyAtWorkId, string Name,
        DateTime? BirthDate, object Employees, int Size);

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

        // Sicherheit: Merge nur, wenn plausibel dieselbe Person — entweder identische
        // easy@work-ID (klassischer Doppel-Import) ODER identischer Name + Geburtsdatum
        // (Wiedereintritt mit NEUER easy@work-ID). Sonst blockieren.
        var eawId = main.EasyAtWorkEmployeeId;
        var mainPersonKey = PersonKey(main.FirstName, main.LastName, main.DateOfBirth);
        bool SamePerson(Employee d) =>
            (eawId != null && d.EasyAtWorkEmployeeId == eawId) ||
            (mainPersonKey != null && PersonKey(d.FirstName, d.LastName, d.DateOfBirth) == mainPersonKey);
        var mismatch = dups.Where(d => !SamePerson(d)).ToList();
        if (mismatch.Count > 0)
            return Conflict(new
            {
                error = "PERSON_MISMATCH",
                message = "Haupt-MA und mind. ein Duplikat sind weder über die easy@work-ID noch über Name + Geburtsdatum als dieselbe Person erkennbar: "
                    + string.Join(", ", mismatch.Select(d => $"{d.FirstName} {d.LastName} (Nr. {d.EmployeeNumber})"))
            });

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

        // Abweichende easy@work-IDs der Duplikate als Alias am Haupt-MA sichern —
        // damit künftige Stempel-/MA-Syncs die alte ID weiter dieser Person zuordnen.
        var existingEawAliases = (await _db.EasyAtWorkEmployeeAliases.AsNoTracking()
                .Where(a => a.EmployeeId == main.Id).Select(a => a.EasyAtWorkId).ToListAsync(ct))
            .ToHashSet();
        var aliasEawIds = new List<int>();
        foreach (var d in dups)
        {
            var did = d.EasyAtWorkEmployeeId;
            if (did == null) continue;
            if (eawId != null && did == eawId) continue;        // gleiche ID → kein Alias nötig
            if (existingEawAliases.Contains(did.Value)) continue;
            if (aliasEawIds.Contains(did.Value)) continue;
            aliasEawIds.Add(did.Value);
        }

        // Stale Austrittsdatum am aktiven Haupt-MA (Wiedereintritt) wird beim Merge entfernt.
        var clearExitDate = main.IsActive && main.ExitDate.HasValue;

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
                aliasEawIds,
                clearExitDate,
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
                    Source = "merge", CreatedAt = DateTime.Now,
                });

            // Abweichende easy@work-IDs als Alias am Haupt-MA sichern.
            foreach (var aeaw in aliasEawIds)
                _db.EasyAtWorkEmployeeAliases.Add(new EasyAtWorkEmployeeAlias
                {
                    EmployeeId = main.Id, EasyAtWorkId = aeaw,
                    Note = "merge", CreatedBy = "merge", CreatedAt = DateTime.Now,
                });

            // Stale Austrittsdatum am aktiven Haupt-MA (Wiedereintritt) entfernen.
            if (clearExitDate) main.ExitDate = null;

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

        _log.LogInformation("Merge OK: Haupt-MA {Main} ({Num}), easy@work-ID {Eaw}; zusammengeführt: {Dups}; Nr-Aliase: {Aliases}; easy@work-ID-Aliase: {EawAliases}; Austritt gelöscht: {ClearExit}",
            main.Id, mainNum, eawId, string.Join(",", dupIds), string.Join(",", aliasNumbers), string.Join(",", aliasEawIds), clearExitDate);

        var msg = $"{dupIds.Count} Duplikat(e) auf {mainNum} zusammengeführt. {aliasNumbers.Count} alte Nummer(n) als Alias gesichert.";
        if (aliasEawIds.Count > 0) msg += $" {aliasEawIds.Count} alte easy@work-ID(s) als Alias gesichert.";
        if (clearExitDate) msg += " Austrittsdatum am Haupt-MA entfernt (Wiedereintritt).";

        return Ok(new
        {
            ok = true,
            mainEmployeeId = main.Id,
            mergedCount = dupIds.Count,
            aliasNumbers,
            aliasEawIds,
            clearExitDate,
            message = msg
        });
    }
}

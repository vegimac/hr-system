using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace HrSystem.Controllers;

[ApiController]
[Route("api/absences")]
public class AbsencesController : ControllerBase
{
    private readonly AppDbContext        _db;
    private readonly KarenzService       _karenz;
    private readonly SperrfristService   _sperrfrist;
    private readonly LohnEditLockService _editLock;
    public AbsencesController(AppDbContext db, KarenzService karenz, SperrfristService sperrfrist, LohnEditLockService editLock)
    {
        _db         = db;
        _karenz     = karenz;
        _sperrfrist = sperrfrist;
        _editLock   = editLock;
    }

    /// <summary>
    /// Prüft ob für (employeeId, dateRange) eine Lohnlauf-bedingte Sperre greift.
    /// Resolved den ersten aktiven Vertrag des MA, um die richtige Filiale zu
    /// finden. Liefert null wenn frei, sonst eine 409-Antwort.
    /// </summary>
    private async Task<IActionResult?> CheckLohnLockAsync(int employeeId, DateOnly from, DateOnly to)
    {
        var emp = await _db.Employees
            .Where(e => e.Id == employeeId)
            .Select(e => new
            {
                e.Id,
                BranchId = e.Employments
                    .Where(x => x.IsActive)
                    .OrderByDescending(x => x.ContractStartDate)
                    .Select(x => (int?)x.CompanyProfileId)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();
        if (emp?.BranchId is null) return null; // keine Filial-Zuordnung → kein Lock

        // Per-Periode (Walter-Vorgabe 27.06.2026): nur sperren, wenn GENAU die
        // Periode der Absenz abgeschlossen/in Verarbeitung ist — rückwirkende
        // Einträge in offene/nie verarbeitete Perioden bleiben erlaubt.
        var r = await _editLock.CheckRangePeriodAsync(User, emp.BranchId.Value, from, to);
        if (!r.Locked) return null;

        return Conflict(new { error = "LOHN_EDIT_LOCKED", message = r.Reason, firstAllowedDate = r.FirstAllowedDate?.ToString("yyyy-MM-dd") });
    }

    // ── GET /api/absences/employee/{employeeId} ───────────────────────────
    [HttpGet("employee/{employeeId:int}")]
    public async Task<IActionResult> GetByEmployee(int employeeId)
    {
        var list = await _db.Absences
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.DateFrom)
            .Select(a => MapToDto(a))
            .ToListAsync();

        return Ok(list);
    }

    /// <summary>
    /// MA-IDs mit heute laufender Absenz KRANK / UNFALL / MUTT_VATER
    /// (Listen-Filter «Krank / Unfall / Mutterschaft (aktuell)», Walter 21.07.2026).
    /// </summary>
    [HttpGet("employee-ids-current")]
    public async Task<IActionResult> GetEmployeeIdsCurrentlyAbsent()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var types = new[] { "KRANK", "UNFALL", "MUTT_VATER" };
        var ids = await _db.Absences.AsNoTracking()
            .Where(a => types.Contains(a.AbsenceType)
                        && a.DateFrom <= today
                        && a.DateTo >= today)
            .Select(a => a.EmployeeId)
            .Distinct()
            .ToListAsync();
        return Ok(ids);
    }

    /// <summary>
    /// Erlaubte Filialen des eingeloggten Users — gleiche Logik wie
    /// EmployeesController.GetAllowedBranchIdsAsync (Walter 22.07.2026).
    /// null = unbeschraenkt (admin + reiner superuser); buchhaltung-Claim
    /// ZUERST pruefen (CLAUDE.md), user/lowuser via user_branch_access.
    /// </summary>
    private async Task<List<int>?> GetAllowedBranchIdsAsync()
    {
        if (User.IsInRole("admin")) return null;
        var restricted = User.IsInRole("buchhaltung") || !User.IsInRole("superuser");
        if (!restricted) return null;
        var idStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(idStr, out var uid)) return new List<int>();
        return await _db.UserBranchAccesses.AsNoTracking()
            .Where(a => a.UserId == uid)
            .Select(a => a.CompanyProfileId)
            .ToListAsync();
    }

    // ── GET /api/absences/kalender?companyProfileId&from&to ───────────────
    // Filial-Kalender (Walter 22.07.2026): alle aktiven MA der Filiale mit
    // im Zeitfenster laufendem Vertrag + deren Absenzen, die das Fenster
    // ueberlappen. Freies von/bis-Fenster (max. 100 Tage) statt fixem
    // Kalendermonat — das Frontend schiebt ein 31-Tage-Fenster frei nach
    // links/rechts. Dazu best-effort der letzte Ferien-Saldo (payroll_saldo).
    [HttpGet("kalender")]
    public async Task<IActionResult> GetKalender(int companyProfileId, string? from, string? to)
    {
        if (companyProfileId <= 0
            || !DateOnly.TryParse(from, out var fromD)
            || !DateOnly.TryParse(to, out var toD)
            || toD < fromD
            || toD.DayNumber - fromD.DayNumber > 100)
            return BadRequest(new { error = "INVALID_PARAMS" });

        var allowed = await GetAllowedBranchIdsAsync();
        if (allowed != null && !allowed.Contains(companyProfileId))
            return StatusCode(403, new { error = "BRANCH_FORBIDDEN", message = "Kein Zugriff auf diese Filiale." });

        var fromDt = fromD.ToDateTime(TimeOnly.MinValue);
        var toDt   = toD.ToDateTime(TimeOnly.MinValue);

        // MA mit im Zeitfenster LAUFENDEM Vertrag in dieser Filiale. Bewusst
        // KEIN IsActive-Filter (weder Employment noch Employee): fuer die
        // Vergangenheit zaehlt allein der Datums-Overlap — alte Vertrags-
        // versionen stehen auf is_active=false (cleanup_old_contracts_
        // inactive.sql) und ausgetretene MA sind inaktiv, waren im damaligen
        // Monat aber da (Walter-Bug 22.07.2026: beim Zurueckblaettern wurden
        // die MA immer weniger). Roh laden, Konvertierungen im Speicher —
        // CLAUDE.md Datum-Regelwerk Pkt. 1.
        var emps = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden && !e.IsPayrollExcluded)
            .Where(e => e.Employments.Any(x =>
                x.CompanyProfileId == companyProfileId
                && x.ContractStartDate <= toDt
                && (x.ContractEndDate == null || x.ContractEndDate >= fromDt)))
            .Select(e => new
            {
                e.Id, e.FirstName, e.LastName, e.IsActive,
                Contract = e.Employments
                    .Where(x => x.CompanyProfileId == companyProfileId
                        && x.ContractStartDate <= toDt
                        && (x.ContractEndDate == null || x.ContractEndDate >= fromDt))
                    .OrderByDescending(x => x.ContractStartDate)
                    .Select(x => new { x.EmploymentModel, x.EmploymentPercentage, x.GuaranteedHoursPerWeek })
                    .FirstOrDefault()
            })
            .ToListAsync();

        var empIds = emps.Select(e => e.Id).ToList();

        var abs = await _db.Absences.AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId) && a.DateFrom <= toD && a.DateTo >= fromD)
            .Select(a => new { a.EmployeeId, a.AbsenceType, a.DateFrom, a.DateTo, a.Prozent, a.Notes })
            .ToListAsync();
        var absByEmp = abs.ToLookup(a => a.EmployeeId);   // Lookup: fehlender Key = leere Sequenz

        // Letzter Ferien-Saldo pro MA (best-effort; nur juengere Perioden laden,
        // Auswahl der letzten Zeile im Speicher — GroupBy+First ist EF-heikel).
        var saldiRaw = await _db.PayrollSaldos.AsNoTracking()
            .Where(s => s.CompanyProfileId == companyProfileId
                && empIds.Contains(s.EmployeeId)
                && s.PeriodYear >= fromD.Year - 1)
            .Select(s => new { s.EmployeeId, s.PeriodYear, s.PeriodMonth, s.FerienTageSaldo, s.FerienGeldSaldo })
            .ToListAsync();
        var saldoByEmp = saldiRaw
            .GroupBy(s => s.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.PeriodYear).ThenByDescending(s => s.PeriodMonth).First());

        var result = emps
            .OrderBy(e => e.FirstName ?? "").ThenBy(e => e.LastName ?? "")   // MA-Listen IMMER nach Vorname (CLAUDE.md)
            .Select(e => new
            {
                id = e.Id,
                name = $"{e.FirstName} {e.LastName}".Trim(),
                isActive = e.IsActive,
                modell = e.Contract?.EmploymentModel,
                pensum = e.Contract?.EmploymentPercentage,
                garantierteStunden = e.Contract?.GuaranteedHoursPerWeek,
                ferienTageSaldo = saldoByEmp.TryGetValue(e.Id, out var s) ? (decimal?)s.FerienTageSaldo : null,
                ferienGeldSaldo = saldoByEmp.TryGetValue(e.Id, out var s2) ? (decimal?)s2.FerienGeldSaldo : null,
                absenzen = absByEmp[e.Id]
                    .OrderBy(a => a.DateFrom)
                    .Select(a => new
                    {
                        type = a.AbsenceType,
                        dateFrom = a.DateFrom.ToString("yyyy-MM-dd"),
                        dateTo = a.DateTo.ToString("yyyy-MM-dd"),
                        prozent = a.Prozent,
                        notes = a.Notes,
                    }),
            });

        return Ok(new { from = fromD.ToString("yyyy-MM-dd"), to = toD.ToString("yyyy-MM-dd"), mitarbeiter = result });
    }

    // ── POST /api/absences ────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AbsenceDto dto)
    {
        // Lohnlauf-Sperre: keine Absenz in einer Periode anlegen, die bei HR
        // liegt oder bereits ausbezahlt/abgeschlossen ist.
        var locked = await CheckLohnLockAsync(dto.EmployeeId, DateOnly.Parse(dto.DateFrom), DateOnly.Parse(dto.DateTo));
        if (locked != null) return locked;

        var absence = new Absence
        {
            EmployeeId    = dto.EmployeeId,
            AbsenceType   = dto.AbsenceType.ToUpper(),
            DateFrom      = DateOnly.Parse(dto.DateFrom),
            DateTo        = DateOnly.Parse(dto.DateTo),
            WorkedDays    = dto.WorkedDays,
            HoursCredited = dto.HoursCredited,
            Prozent       = ClampProzent(dto.Prozent),
            Notes         = dto.Notes,
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow,
        };

        _db.Absences.Add(absence);
        await _db.SaveChangesAsync();

        return Ok(MapToDto(absence));
    }

    // ── PUT /api/absences/{id} ────────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AbsenceDto dto)
    {
        var absence = await _db.Absences.FindAsync(id);
        if (absence == null) return NotFound();

        // Lohnlauf-Sperre: alte UND neue Daten müssen ausserhalb gesperrter
        // Perioden liegen. Prüft beide Zeiträume separat.
        var newFrom = DateOnly.Parse(dto.DateFrom);
        var newTo   = DateOnly.Parse(dto.DateTo);
        var lock1   = await CheckLohnLockAsync(absence.EmployeeId, absence.DateFrom, absence.DateTo);
        if (lock1 != null) return lock1;
        var lock2   = await CheckLohnLockAsync(absence.EmployeeId, newFrom, newTo);
        if (lock2 != null) return lock2;

        // Lock: Tage in bestätigten Lohnperioden dürfen nicht verändert werden.
        // Prüfung umfasst sowohl die alten als auch die neuen markierten Tage
        // (um sowohl "Tag aus confirmed-Periode entfernen" als auch "Tag in
        // confirmed-Periode hinzufügen" zu blockieren).
        var lockError = await CheckNotInConfirmedPeriodAsync(absence, dto.WorkedDays, dto.DateFrom, dto.DateTo);
        if (lockError != null) return StatusCode(403, new { message = lockError });

        absence.AbsenceType   = dto.AbsenceType.ToUpper();
        absence.DateFrom      = DateOnly.Parse(dto.DateFrom);
        absence.DateTo        = DateOnly.Parse(dto.DateTo);
        absence.WorkedDays    = dto.WorkedDays;
        absence.HoursCredited = dto.HoursCredited;
        absence.Prozent       = ClampProzent(dto.Prozent);
        absence.Notes         = dto.Notes;
        absence.UpdatedAt     = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(MapToDto(absence));
    }

    // ── GET /api/absences/employee/{id}/karenz-history ────────────────────
    // Liefert die Absenz-History (Krank ODER Unfall) gruppiert nach
    // Karenzjahren — pro Jahr Metadaten (Von/Bis, TageMax, verbraucht,
    // Grenz-Datum) und die Liste der Absenzen mit kumulierten Karenztagen.
    // absenceType: KRANK (Default) | UNFALL — getrennte Kumulation.
    [HttpGet("employee/{employeeId:int}/karenz-history")]
    public async Task<IActionResult> GetKarenzHistory(
        int employeeId,
        [FromQuery] int companyProfileId,
        [FromQuery] string absenceType = "KRANK")
    {
        var typ = NormalizeKarenzAbsenceType(absenceType);
        if (typ is null)
            return BadRequest(new { message = "absenceType muss KRANK oder UNFALL sein." });
        var list = await _karenz.GetHistoryAsync(employeeId, companyProfileId, typ);
        return Ok(list);
    }

    // ── GET /api/absences/employee/{id}/karenz-current ────────────────────
    // Aktuelles Karenzjahr zu einem Stichdatum (Default: heute). Liefert
    // nur die Zusammenfassung, keine Detail-Absenzen.
    // absenceType: KRANK (Default) | UNFALL.
    [HttpGet("employee/{employeeId:int}/karenz-current")]
    public async Task<IActionResult> GetKarenzCurrent(
        int employeeId,
        [FromQuery] int companyProfileId,
        [FromQuery] string? datum = null,
        [FromQuery] string absenceType = "KRANK")
    {
        var typ = NormalizeKarenzAbsenceType(absenceType);
        if (typ is null)
            return BadRequest(new { message = "absenceType muss KRANK oder UNFALL sein." });
        DateOnly d = DateOnly.TryParse(datum, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.Today);
        var info = await _karenz.GetCurrentAsync(employeeId, companyProfileId, d, typ);
        if (info is null) return NotFound();
        return Ok(info);
    }

    private static string? NormalizeKarenzAbsenceType(string? absenceType)
    {
        var t = (absenceType ?? "KRANK").Trim().ToUpperInvariant();
        return t is "KRANK" or "UNFALL" ? t : null;
    }

    // ── GET /api/absences/employee/{id}/sperrfrist ────────────────────────
    // Kündigungsschutz nach Art. 336c OR zum Stichtag (Default: heute).
    // Liefert Sperrfrist-Status, Ende der Sperrfrist und frühestes Datum
    // an dem gekündigt werden darf.
    [HttpGet("employee/{employeeId:int}/sperrfrist")]
    public async Task<IActionResult> GetSperrfrist(
        int employeeId,
        [FromQuery] string? stichtag = null)
    {
        DateOnly s = DateOnly.TryParse(stichtag, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.Today);
        var info = await _sperrfrist.ComputeAsync(employeeId, s);
        return Ok(info);
    }

    // ── DELETE /api/absences/{id} ─────────────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var absence = await _db.Absences.FindAsync(id);
        if (absence == null) return NotFound();

        // Lohnlauf-Sperre: kein Löschen wenn die Absenz in einer in-Verarbeitung-
        // oder abgeschlossenen Periode liegt.
        var lockResult = await CheckLohnLockAsync(absence.EmployeeId, absence.DateFrom, absence.DateTo);
        if (lockResult != null) return lockResult;

        // Lock: Löschen nicht erlaubt wenn markierte Tage in bestätigter Periode
        var lockError = await CheckNotInConfirmedPeriodAsync(absence, absence.WorkedDays,
            absence.DateFrom.ToString("yyyy-MM-dd"), absence.DateTo.ToString("yyyy-MM-dd"));
        if (lockError != null) return StatusCode(403, new { message = lockError });

        _db.Absences.Remove(absence);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── Lock-Prüfung ──────────────────────────────────────────────────────
    /// <summary>
    /// Prüft ob die Absenz Tage in einer bestätigten Lohnperiode berührt
    /// (alte UND neue markierte Tage). Liefert Fehler-Text falls gesperrt,
    /// sonst null.
    /// </summary>
    private async Task<string?> CheckNotInConfirmedPeriodAsync(
        Absence existing, string? newWorkedDaysJson, string? newDateFromStr, string? newDateToStr)
    {
        // Alle relevanten Tage einsammeln: alte + neue
        var touchedDays = new HashSet<DateOnly>();
        AddDays(touchedDays, existing.WorkedDays, existing.DateFrom, existing.DateTo);
        DateOnly? newFrom = DateOnly.TryParse(newDateFromStr, out var nf) ? nf : (DateOnly?)null;
        DateOnly? newTo   = DateOnly.TryParse(newDateToStr,   out var nt) ? nt : (DateOnly?)null;
        if (newFrom.HasValue && newTo.HasValue)
            AddDays(touchedDays, newWorkedDaysJson, newFrom.Value, newTo.Value);

        if (touchedDays.Count == 0) return null;

        // Alle bestätigten PayrollSaldos des MA holen (pro Filiale unterschiedlich
        // weil Perioden-Start-Tag unterschiedlich sein kann)
        var confirmed = await _db.PayrollSaldos
            .Where(s => s.EmployeeId == existing.EmployeeId && s.Status == "confirmed")
            .ToListAsync();
        if (confirmed.Count == 0) return null;

        // Pro confirmed-Periode: Zeitraum berechnen, auf berührte Tage prüfen.
        // Walter-Vorgabe 20.05.2026: die Lohnperiode ist IMMER der Kalendermonat
        // (1.–letzter Tag).
        foreach (var c in confirmed)
        {
            var (from, to) = CalcPeriodRange(c.PeriodYear, c.PeriodMonth);
            if (touchedDays.Any(d => d >= from && d <= to))
            {
                return $"Diese Absenz berührt die bereits bestätigte Lohnperiode {MonthName(c.PeriodMonth)} {c.PeriodYear}. " +
                       $"Bestätigte Perioden sind unveränderlich.";
            }
        }
        return null;
    }

    private static void AddDays(HashSet<DateOnly> set, string? workedDaysJson, DateOnly from, DateOnly to)
    {
        if (!string.IsNullOrWhiteSpace(workedDaysJson))
        {
            try
            {
                var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(workedDaysJson);
                if (arr is not null)
                {
                    foreach (var s in arr)
                        if (DateOnly.TryParse(s, out var d)) set.Add(d);
                    if (arr.Length > 0) return;
                }
            }
            catch { /* fall through */ }
        }
        // Fallback: alle Kalendertage
        for (var d = from; d <= to; d = d.AddDays(1)) set.Add(d);
    }

    // Walter-Vorgabe 20.05.2026: Lohnperiode = IMMER Kalendermonat (1.–letzter Tag).
    private static (DateOnly from, DateOnly to) CalcPeriodRange(int year, int month)
        => (new DateOnly(year, month, 1),
            new DateOnly(year, month, DateTime.DaysInMonth(year, month)));

    private static string MonthName(int m) => m switch
    {
        1 => "Januar", 2 => "Februar", 3 => "März", 4 => "April",
        5 => "Mai", 6 => "Juni", 7 => "Juli", 8 => "August",
        9 => "September", 10 => "Oktober", 11 => "November", 12 => "Dezember",
        _ => m.ToString()
    };

    // ── Mapping ───────────────────────────────────────────────────────────
    private static object MapToDto(Absence a) => new
    {
        id            = a.Id,
        employeeId    = a.EmployeeId,
        absenceType   = a.AbsenceType,
        dateFrom      = a.DateFrom.ToString("yyyy-MM-dd"),
        dateTo        = a.DateTo.ToString("yyyy-MM-dd"),
        workedDays    = a.WorkedDays,
        hoursCredited = a.HoursCredited,
        prozent       = a.Prozent,
        notes         = a.Notes,
        createdAt     = a.CreatedAt,
    };

    // Prozent auf 1–100 clampen; Default 100 wenn nicht übermittelt.
    private static decimal ClampProzent(decimal? p)
    {
        if (p is null || p <= 0) return 100m;
        if (p > 100m) return 100m;
        return Math.Round(p.Value, 2);
    }
}

public class AbsenceDto
{
    public int    EmployeeId    { get; set; }
    public string AbsenceType   { get; set; } = "";
    public string DateFrom      { get; set; } = "";
    public string DateTo        { get; set; } = "";
    public string? WorkedDays   { get; set; }
    public decimal HoursCredited { get; set; }
    public decimal? Prozent     { get; set; }   // 1–100, Default 100
    public string? Notes        { get; set; }
}

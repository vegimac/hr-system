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
    private readonly AppDbContext       _db;
    private readonly KarenzService      _karenz;
    private readonly SperrfristService  _sperrfrist;
    public AbsencesController(AppDbContext db, KarenzService karenz, SperrfristService sperrfrist)
    {
        _db         = db;
        _karenz     = karenz;
        _sperrfrist = sperrfrist;
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

    // ── POST /api/absences ────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AbsenceDto dto)
    {
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
    // Liefert die Krankheits-History gruppiert nach Karenzjahren — pro Jahr
    // Metadaten (Von/Bis, TageMax, verbraucht, Grenz-Datum) und die Liste der
    // Krank-Absenzen mit kumulierten Karenztagen.
    [HttpGet("employee/{employeeId:int}/karenz-history")]
    public async Task<IActionResult> GetKarenzHistory(
        int employeeId,
        [FromQuery] int companyProfileId)
    {
        var list = await _karenz.GetHistoryAsync(employeeId, companyProfileId);
        return Ok(list);
    }

    // ── GET /api/absences/employee/{id}/karenz-current ────────────────────
    // Aktuelles Karenzjahr zu einem Stichdatum (Default: heute). Liefert
    // nur die Zusammenfassung, keine Detail-Absenzen.
    [HttpGet("employee/{employeeId:int}/karenz-current")]
    public async Task<IActionResult> GetKarenzCurrent(
        int employeeId,
        [FromQuery] int companyProfileId,
        [FromQuery] string? datum = null)
    {
        DateOnly d = DateOnly.TryParse(datum, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.Today);
        var info = await _karenz.GetCurrentAsync(employeeId, companyProfileId, d);
        if (info is null) return NotFound();
        return Ok(info);
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

        // Filial-Profile (für PayrollPeriodStartDay)
        var profileIds = confirmed.Select(c => c.CompanyProfileId).Distinct().ToList();
        var profiles = await _db.CompanyProfiles
            .Where(p => profileIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        // Pro confirmed-Periode: Zeitraum berechnen, auf berührte Tage prüfen
        foreach (var c in confirmed)
        {
            if (!profiles.TryGetValue(c.CompanyProfileId, out var profile)) continue;
            int startDay = profile.PayrollPeriodStartDay ?? 1;
            var (from, to) = CalcPeriodRange(startDay, c.PeriodYear, c.PeriodMonth);
            if (touchedDays.Any(d => d >= from && d <= to))
            {
                string periode = startDay <= 1
                    ? $"{MonthName(c.PeriodMonth)} {c.PeriodYear}"
                    : $"{from:dd.MM.yyyy}–{to:dd.MM.yyyy}";
                return $"Diese Absenz berührt die bereits bestätigte Lohnperiode {periode}. " +
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

    private static (DateOnly from, DateOnly to) CalcPeriodRange(int startDay, int year, int month)
    {
        if (startDay <= 1)
        {
            return (new DateOnly(year, month, 1),
                    new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
        }
        var toDate   = new DateOnly(year, month, Math.Min(startDay - 1, DateTime.DaysInMonth(year, month)));
        int prevYear = month == 1 ? year - 1 : year;
        int prevMonth = month == 1 ? 12 : month - 1;
        int prevMaxDay = DateTime.DaysInMonth(prevYear, prevMonth);
        var fromDate = new DateOnly(prevYear, prevMonth, Math.Min(startDay, prevMaxDay));
        return (fromDate, toDate);
    }

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

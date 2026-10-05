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
    private readonly FerienKuerzungService _ferienKuerzung;
    public AbsencesController(AppDbContext db, KarenzService karenz, SperrfristService sperrfrist, LohnEditLockService editLock,
                              FerienKuerzungService ferienKuerzung)
    {
        _db         = db;
        _karenz     = karenz;
        _sperrfrist = sperrfrist;
        _editLock   = editLock;
        _ferienKuerzung = ferienKuerzung;
    }

    /// <summary>
    /// Filiale des MA für Lock-Lookup (aktueller aktiver Vertrag).
    /// </summary>
    private async Task<int?> ResolveBranchIdAsync(int employeeId)
    {
        return await _db.Employees
            .Where(e => e.Id == employeeId)
            .Select(e => e.Employments
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.ContractStartDate)
                .Select(x => (int?)x.CompanyProfileId)
                .FirstOrDefault())
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Prüft ob für (employeeId, dateRange) eine Lohnlauf-bedingte Sperre greift.
    /// Soft-Lock (Walter 27.06.2026 / Aug 2026): nur wenn die Periode der Absenz
    /// DEFINITIV abgeschlossen ist (Status «abgeschlossen») — nicht bei
    /// provisorisch / HR / Akonto. Liefert null wenn frei, sonst 409.
    /// </summary>
    private async Task<IActionResult?> CheckLohnLockAsync(int employeeId, DateOnly from, DateOnly to)
    {
        var branchId = await ResolveBranchIdAsync(employeeId);
        if (branchId is null) return null; // keine Filial-Zuordnung → kein Lock

        var r = await _editLock.CheckRangePeriodAsync(User, branchId.Value, from, to);
        if (!r.Locked) return null;

        return Conflict(new { error = "LOHN_EDIT_LOCKED", message = r.Reason, firstAllowedDate = r.FirstAllowedDate?.ToString("yyyy-MM-dd") });
    }

    /// <summary>
    /// Soft-Lock-Flag für die Liste: Absenz-Monate mit Status «abgeschlossen».
    /// </summary>
    private async Task<HashSet<(int Year, int Month)>> LoadFrozenMonthsAsync(int? branchId, int minYear)
    {
        if (branchId is null) return new HashSet<(int, int)>();
        var rows = await _db.PayrollPerioden.AsNoTracking()
            .Where(p => p.CompanyProfileId == branchId.Value
                     && p.Status == "abgeschlossen"
                     && p.Year >= minYear)
            .Select(p => new { p.Year, p.Month })
            .ToListAsync();
        return rows.Select(r => (r.Year, r.Month)).ToHashSet();
    }

    private static bool IsAbsenceInFrozenMonths(Absence a, HashSet<(int Year, int Month)> frozen)
    {
        if (frozen.Count == 0) return false;
        var cursor = new DateOnly(a.DateFrom.Year, a.DateFrom.Month, 1);
        var last   = new DateOnly(a.DateTo.Year, a.DateTo.Month, 1);
        while (cursor <= last)
        {
            if (frozen.Contains((cursor.Year, cursor.Month))) return true;
            cursor = cursor.AddMonths(1);
        }
        return false;
    }

    // ── GET /api/absences/employee/{employeeId} ───────────────────────────
    [HttpGet("employee/{employeeId:int}")]
    public async Task<IActionResult> GetByEmployee(int employeeId)
    {
        var list = await _db.Absences
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.DateFrom)
            .ToListAsync();

        var branchId = await ResolveBranchIdAsync(employeeId);
        var minYear  = list.Count > 0 ? list.Min(a => a.DateFrom.Year) - 1 : DateTime.Today.Year - 1;
        var frozen   = await LoadFrozenMonthsAsync(branchId, minYear);

        return Ok(list.Select(a => MapToDto(a, IsAbsenceInFrozenMonths(a, frozen))));
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

    /// <summary>
    /// Walter 26.07.2026: Pro Kalendertag höchstens EINE Absenz — egal welcher Typ.
    /// Krank/Unfall/Mutterschaft dürfen keine Ferien/Nachtkomp/etc. überlappen;
    /// umgekehrt genauso. Erlaubt ist nur Aufteilen (z.B. Ferien 2.–6.,
    /// Nachtkomp 7., Ferien 8.–9.).
    /// </summary>
    private async Task<IActionResult?> CheckOverlapAsync(
        int employeeId, DateOnly from, DateOnly to, int? excludeId = null)
    {
        if (to < from)
            return BadRequest(new { error = "INVALID_RANGE", message = "Datum bis darf nicht vor Datum von liegen." });

        var q = _db.Absences.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId
                     && a.DateFrom <= to
                     && a.DateTo >= from);
        if (excludeId is int xid)
            q = q.Where(a => a.Id != xid);

        var conflict = await q
            .OrderBy(a => a.DateFrom)
            .Select(a => new { a.Id, a.AbsenceType, a.DateFrom, a.DateTo })
            .FirstOrDefaultAsync();
        if (conflict is null) return null;

        var label = AbsenceTypeLabel(conflict.AbsenceType);
        var fromCh = conflict.DateFrom.ToString("dd.MM.yyyy");
        var toCh   = conflict.DateTo.ToString("dd.MM.yyyy");
        return Conflict(new
        {
            error = "ABSENCE_OVERLAP",
            message = $"Überlappung mit «{label}» vom {fromCh}–{toCh}. "
                    + "Pro Tag ist nur eine Absenz erlaubt — bei Bedarf die bestehende Absenz aufteilen "
                    + "(z.B. Ferien vor/nach einem einzelnen Kompensationstag).",
            conflictingId = conflict.Id,
            conflictingType = conflict.AbsenceType,
            conflictingDateFrom = conflict.DateFrom.ToString("yyyy-MM-dd"),
            conflictingDateTo = conflict.DateTo.ToString("yyyy-MM-dd"),
        });
    }

    /// <summary>
    /// Feiertage haben nur FIX/FIX-M zugut (Walter 05.10.2026). MTP/FLEX bekommen
    /// die Entschädigung als % pro Stunde — von Hand erfassen ist dort gesperrt.
    /// Importierte Feiertage bleiben erlaubt, wirken aber nicht (Engine).
    /// </summary>
    private async Task<IActionResult?> CheckFeiertagNurFixAsync(int employeeId, DateOnly from, DateOnly to)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MinValue);
        var modelle = await _db.Employments.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId
                     && e.ContractStartDate <= toDt
                     && (e.ContractEndDate == null || e.ContractEndDate >= fromDt))
            .Select(e => e.EmploymentModel)
            .ToListAsync();
        if (FeiertagErfassbar(modelle)) return null;
        return BadRequest(new
        {
            error = "FEIERTAG_NUR_FIX",
            message = "Feiertage werden nur bei FIX-Verträgen erfasst. Bei MTP und FLEX ist der Feiertag "
                    + "über die Feiertagsentschädigung (% pro Stunde) bezahlt.",
        });
    }

    /// <summary>Ohne Vertrag im Zeitraum nicht sperren — dann fehlt die Grundlage für die Regel.</summary>
    public static bool FeiertagErfassbar(IReadOnlyCollection<string?> modelleImZeitraum)
        => modelleImZeitraum.Count == 0
        || modelleImZeitraum.Any(m => m is "FIX" or "FIX-M");

    private static string AbsenceTypeLabel(string? code) => (code ?? "").ToUpperInvariant() switch
    {
        "KRANK" => "Krankheit",
        "UNFALL" => "Unfall",
        "FERIEN" => "Ferien",
        "NACHT_KOMP" => "Nacht-Kompensation",
        "FREI_KOMP" => "Frei-Kompensation",
        "FEIERTAG" => "Feiertag",
        "SCHULUNG" => "Schulung",
        "MILITAER" => "Militär",
        "MUTT_VATER" => "Mutter-/Vaterschaftsurlaub",
        "BEZ_ABSENZ" => "Bezahlte Absenz",
        "UNBEZ_URLAUB" => "Unbezahlter Urlaub",
        _ => string.IsNullOrWhiteSpace(code) ? "Absenz" : code,
    };

    // ── POST /api/absences ────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AbsenceDto dto)
    {
        if (dto is null || dto.EmployeeId <= 0)
            return BadRequest(new { error = "INVALID_DTO", message = "Mitarbeiter fehlt." });
        if (string.IsNullOrWhiteSpace(dto.AbsenceType))
            return BadRequest(new { error = "INVALID_TYPE", message = "Abwesenheitstyp fehlt." });
        if (!DateOnly.TryParse(dto.DateFrom, out var from) || !DateOnly.TryParse(dto.DateTo, out var to))
            return BadRequest(new { error = "INVALID_DATE", message = "Ungültiges Datum." });

        // Lohnlauf-Sperre: keine Absenz in einer Periode anlegen, die bei HR
        // liegt oder bereits ausbezahlt/abgeschlossen ist.
        var locked = await CheckLohnLockAsync(dto.EmployeeId, from, to);
        if (locked != null) return locked;

        var overlap = await CheckOverlapAsync(dto.EmployeeId, from, to);
        if (overlap != null) return overlap;

        if (string.Equals(dto.AbsenceType.Trim(), "FEIERTAG", StringComparison.OrdinalIgnoreCase))
        {
            var nurFix = await CheckFeiertagNurFixAsync(dto.EmployeeId, from, to);
            if (nurFix != null) return nurFix;
        }

        var absence = new Absence
        {
            EmployeeId    = dto.EmployeeId,
            AbsenceType   = dto.AbsenceType.Trim().ToUpperInvariant(),
            DateFrom      = from,
            DateTo        = to,
            WorkedDays    = dto.WorkedDays,
            HoursCredited = dto.HoursCredited,
            Prozent       = ClampProzent(dto.Prozent),
            Notes         = dto.Notes,
            Ferienfaehig  = dto.Ferienfaehig == true
                            && string.Equals(dto.AbsenceType.Trim(), "FERIEN", StringComparison.OrdinalIgnoreCase),
            DienstplanBis = DienstplanBisNormalisieren(dto.DienstplanBis, dto.AbsenceType.Trim(), from, to),
            CreatedAt     = DateTime.Now,
            UpdatedAt     = DateTime.Now,
        };
        await KrankUnfallStundenSetzenAsync(absence);

        _db.Absences.Add(absence);
        await _db.SaveChangesAsync();

        return Ok(MapToDto(absence, inLohnVerwendet: false));
    }

    // ── PUT /api/absences/{id} ────────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AbsenceDto dto)
    {
        var absence = await _db.Absences.FindAsync(id);
        if (absence == null) return NotFound();

        // Soft-Lock: alte UND neue Daten dürfen keine abgeschlossene Periode berühren.
        var newFrom = DateOnly.Parse(dto.DateFrom);
        var newTo   = DateOnly.Parse(dto.DateTo);
        var lock1   = await CheckLohnLockAsync(absence.EmployeeId, absence.DateFrom, absence.DateTo);
        if (lock1 != null) return lock1;
        var lock2   = await CheckLohnLockAsync(absence.EmployeeId, newFrom, newTo);
        if (lock2 != null) return lock2;

        var overlap = await CheckOverlapAsync(absence.EmployeeId, newFrom, newTo, excludeId: id);
        if (overlap != null) return overlap;

        if (string.Equals(dto.AbsenceType, "FEIERTAG", StringComparison.OrdinalIgnoreCase)
            && absence.AbsenceType != "FEIERTAG")
        {
            var nurFix = await CheckFeiertagNurFixAsync(absence.EmployeeId, newFrom, newTo);
            if (nurFix != null) return nurFix;
        }

        absence.AbsenceType   = dto.AbsenceType.ToUpper();
        absence.DateFrom      = newFrom;
        absence.DateTo        = newTo;
        absence.WorkedDays    = dto.WorkedDays;
        absence.HoursCredited = dto.HoursCredited;
        absence.Prozent       = ClampProzent(dto.Prozent);
        absence.Notes         = dto.Notes;
        if (dto.Ferienfaehig.HasValue) absence.Ferienfaehig = dto.Ferienfaehig.Value;
        if (absence.AbsenceType != "FERIEN") absence.Ferienfaehig = false;
        absence.DienstplanBis = DienstplanBisNormalisieren(dto.DienstplanBis, absence.AbsenceType, newFrom, newTo);
        await KrankUnfallStundenSetzenAsync(absence);
        absence.UpdatedAt     = DateTime.Now;

        await _db.SaveChangesAsync();
        return Ok(MapToDto(absence, inLohnVerwendet: false));
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

    // ── Absenzbedingte Ferienkürzung (Walter-Vorgabe 23.09.2026) ─────────
    // Bewusster HR-Eintrag statt Häkchen im Lohnlauf. Eigene Tabelle (kein
    // Abwesenheitstag). Obergrenze = «noch möglich» laut Rechnung, genauer
    // Betrag erlaubt, NIE mehr. Lohn-Sperre wie bei Absenzen (Datum = Monat).

    [HttpGet("employee/{employeeId:int}/ferienkuerzung-info")]
    public async Task<IActionResult> FerienKuerzungInfo(int employeeId, [FromQuery] string? datum, [FromQuery] int? ausserId)
    {
        var d = DateOnly.TryParse(datum, out var p) ? p : DateOnly.FromDateTime(DateTime.Today);
        return Ok(await _ferienKuerzung.InfoAsync(employeeId, d, ausserId));
    }

    [HttpGet("employee/{employeeId:int}/ferienkuerzungen")]
    public async Task<IActionResult> FerienKuerzungen(int employeeId)
        => Ok(await _db.FerienKuerzungen.AsNoTracking()
            .Where(k => k.EmployeeId == employeeId)
            .OrderByDescending(k => k.Datum)
            .Select(k => new { k.Id, k.EmployeeId, datum = k.Datum.ToString("yyyy-MM-dd"),
                               dienstjahrVon = k.DienstjahrVon.ToString("yyyy-MM-dd"), k.Tage, k.Verzicht,
                               k.Bemerkung, k.DokumentId, k.ErstelltVon, k.ErstelltAm })
            .ToListAsync());

    public class FerienKuerzungDto
    {
        public int     EmployeeId { get; set; }
        public string  Datum      { get; set; } = "";
        /// <summary>Irgendein Tag im Dienstjahr, das gekürzt wird (Default = Datum).</summary>
        public string? Dienstjahr { get; set; }
        public decimal Tage       { get; set; }
        public bool    Verzicht   { get; set; }
        public string? Bemerkung  { get; set; }
    }

    [HttpPost("ferienkuerzung")]
    public Task<IActionResult> FerienKuerzungAnlegen([FromBody] FerienKuerzungDto dto) => FerienKuerzungSpeichernAsync(null, dto);

    [HttpPut("ferienkuerzung/{id:int}")]
    public Task<IActionResult> FerienKuerzungAendern(int id, [FromBody] FerienKuerzungDto dto) => FerienKuerzungSpeichernAsync(id, dto);

    private async Task<IActionResult> FerienKuerzungSpeichernAsync(int? id, FerienKuerzungDto dto)
    {
        if (!DateOnly.TryParse(dto.Datum, out var datum))
            return BadRequest(new { error = "DATUM", message = "Datum fehlt oder ist ungültig." });
        FerienKuerzungEintrag? e = null;
        if (id.HasValue)
        {
            e = await _db.FerienKuerzungen.FirstOrDefaultAsync(k => k.Id == id.Value);
            if (e == null) return NotFound();
            var lockAlt = await CheckLohnLockAsync(e.EmployeeId, e.Datum, e.Datum);
            if (lockAlt != null) return lockAlt;
        }
        int empId = e?.EmployeeId ?? dto.EmployeeId;
        var lockNeu = await CheckLohnLockAsync(empId, datum, datum);
        if (lockNeu != null) return lockNeu;

        var djStichtag = DateOnly.TryParse(dto.Dienstjahr, out var dj) ? dj : datum;
        var info = await _ferienKuerzung.InfoAsync(empId, djStichtag, id);
        if (info.DienstjahrVon == default)
            return BadRequest(new { error = "EINTRITT", message = "Ohne Eintrittsdatum lässt sich das Dienstjahr nicht bestimmen." });
        decimal tage = dto.Verzicht ? 0m : Math.Round(dto.Tage, 2);
        if (!dto.Verzicht)
        {
            if (tage <= 0)
                return BadRequest(new { error = "TAGE", message = "Bitte die Anzahl Tage angeben (oder «nicht kürzen» wählen)." });
            if (tage > info.NochMoeglich)
                return BadRequest(new { error = "ZU_VIEL",
                    message = $"Höchstens {info.NochMoeglich:0.00} Tage möglich (Dienstjahr {info.DienstjahrVon:dd.MM.yyyy}–{info.DienstjahrBis:dd.MM.yyyy}, bereits gekürzt {info.BereitsGekuerzt:0.00})." });
        }

        if (e == null)
        {
            e = new FerienKuerzungEintrag { EmployeeId = empId };
            _db.FerienKuerzungen.Add(e);
        }
        e.Datum       = datum;
        e.DienstjahrVon = info.DienstjahrVon;
        e.Tage        = tage;
        e.Verzicht    = dto.Verzicht;
        e.Bemerkung   = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        e.ErstelltVon = User.Identity?.Name;
        e.ErstelltAm  = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { e.Id, datum = e.Datum.ToString("yyyy-MM-dd"), e.Tage, e.Verzicht });
    }

    [HttpDelete("ferienkuerzung/{id:int}")]
    public async Task<IActionResult> FerienKuerzungLoeschen(int id)
    {
        var e = await _db.FerienKuerzungen.FirstOrDefaultAsync(k => k.Id == id);
        if (e == null) return NotFound();
        var lockRes = await CheckLohnLockAsync(e.EmployeeId, e.Datum, e.Datum);
        if (lockRes != null) return lockRes;
        _db.FerienKuerzungen.Remove(e);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── Ferien auszahlen ohne Bezug (Walter-Vorgabe 05.10.2026) ──────────
    // Eigene Tabelle wie die Kürzung (kein Abwesenheitstag). Art TAGE = Anzahl
    // Tage, Geld im Verhältnis aus dem Topf; Art VORJAHR = Saldo per 31.12.
    // (höchstens einmal pro Jahr). Gedeckelt wird im Lohnlauf (kein Vorbezug).

    [HttpGet("employee/{employeeId:int}/ferienauszahlung-info")]
    public async Task<IActionResult> FerienAuszahlungInfo(int employeeId, [FromQuery] string? datum)
    {
        var d = DateOnly.TryParse(datum, out var p) ? p : DateOnly.FromDateTime(DateTime.Today);
        var saldi = await _db.PayrollSaldos.AsNoTracking().Where(s => s.EmployeeId == employeeId).ToListAsync();
        var vormonat = saldi
            .Where(s => s.PeriodYear * 100 + s.PeriodMonth < d.Year * 100 + d.Month)
            .OrderByDescending(s => s.PeriodYear * 100 + s.PeriodMonth).ThenByDescending(s => s.Id)
            .FirstOrDefault();
        var vj = await FerienAuszahlungDaten.SaldoVorjahrAsync(_db, employeeId, d.Year);
        return Ok(new
        {
            vormonat = vormonat == null ? null : new
            {
                periode = $"{vormonat.PeriodYear}-{vormonat.PeriodMonth:D2}",
                tage = Math.Round(vormonat.FerienTageSaldo, 2),
                chf = Math.Round(vormonat.FerienGeldSaldo, 2),
            },
            vorjahr = vj == null ? null : new { jahr = d.Year - 1, tage = Math.Round(vj.Tage, 2), chf = Math.Round(vj.Chf, 2) },
        });
    }

    [HttpGet("employee/{employeeId:int}/ferienauszahlungen")]
    public async Task<IActionResult> FerienAuszahlungen(int employeeId)
        => Ok(await _db.FerienAuszahlungen.AsNoTracking()
            .Where(f => f.EmployeeId == employeeId)
            .OrderByDescending(f => f.Datum)
            .Select(f => new { f.Id, f.EmployeeId, datum = f.Datum.ToString("yyyy-MM-dd"), f.Art, f.Tage,
                               f.Bemerkung, f.ErstelltVon, f.ErstelltAm })
            .ToListAsync());

    public class FerienAuszahlungDto
    {
        public int      EmployeeId { get; set; }
        public string   Datum      { get; set; } = "";
        public string   Art        { get; set; } = FerienAuszahlungRechnung.ArtTage;
        public decimal? Tage       { get; set; }
        public string?  Bemerkung  { get; set; }
    }

    [HttpPost("ferienauszahlung")]
    public Task<IActionResult> FerienAuszahlungAnlegen([FromBody] FerienAuszahlungDto dto) => FerienAuszahlungSpeichernAsync(null, dto);

    [HttpPut("ferienauszahlung/{id:int}")]
    public Task<IActionResult> FerienAuszahlungAendern(int id, [FromBody] FerienAuszahlungDto dto) => FerienAuszahlungSpeichernAsync(id, dto);

    private async Task<IActionResult> FerienAuszahlungSpeichernAsync(int? id, FerienAuszahlungDto dto)
    {
        if (!DateOnly.TryParse(dto.Datum, out var datum))
            return BadRequest(new { error = "DATUM", message = "Datum fehlt oder ist ungültig." });
        var art = (dto.Art ?? "").Trim().ToUpperInvariant();
        if (art != FerienAuszahlungRechnung.ArtTage && art != FerienAuszahlungRechnung.ArtVorjahr)
            return BadRequest(new { error = "ART", message = "Bitte «Anzahl Tage» oder «Saldo per 31.12.» wählen." });
        decimal? tage = null;
        if (art == FerienAuszahlungRechnung.ArtTage)
        {
            tage = Math.Round(dto.Tage ?? 0m, 2);
            if (tage <= 0)
                return BadRequest(new { error = "TAGE", message = "Bitte die Anzahl Tage angeben." });
        }

        FerienAuszahlungEintrag? e = null;
        if (id.HasValue)
        {
            e = await _db.FerienAuszahlungen.FirstOrDefaultAsync(f => f.Id == id.Value);
            if (e == null) return NotFound();
            var lockAlt = await CheckLohnLockAsync(e.EmployeeId, e.Datum, e.Datum);
            if (lockAlt != null) return lockAlt;
        }
        int empId = e?.EmployeeId ?? dto.EmployeeId;
        var lockNeu = await CheckLohnLockAsync(empId, datum, datum);
        if (lockNeu != null) return lockNeu;

        if (art == FerienAuszahlungRechnung.ArtVorjahr)
        {
            bool doppelt = await _db.FerienAuszahlungen.AnyAsync(f => f.EmployeeId == empId
                && f.Art == FerienAuszahlungRechnung.ArtVorjahr && f.Datum.Year == datum.Year && f.Id != (id ?? 0));
            if (doppelt)
                return Conflict(new { error = "VORJAHR_DOPPELT",
                    message = $"Der Saldo per 31.12.{datum.Year - 1} ist schon zur Auszahlung erfasst." });
        }

        if (e == null)
        {
            e = new FerienAuszahlungEintrag { EmployeeId = empId };
            _db.FerienAuszahlungen.Add(e);
        }
        e.Datum       = datum;
        e.Art         = art;
        e.Tage        = tage;
        e.Bemerkung   = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        e.ErstelltVon = User.Identity?.Name;
        e.ErstelltAm  = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { e.Id, datum = e.Datum.ToString("yyyy-MM-dd"), e.Art, e.Tage });
    }

    [HttpDelete("ferienauszahlung/{id:int}")]
    public async Task<IActionResult> FerienAuszahlungLoeschen(int id)
    {
        var e = await _db.FerienAuszahlungen.FirstOrDefaultAsync(f => f.Id == id);
        if (e == null) return NotFound();
        var lockRes = await CheckLohnLockAsync(e.EmployeeId, e.Datum, e.Datum);
        if (lockRes != null) return lockRes;
        _db.FerienAuszahlungen.Remove(e);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── DELETE /api/absences/{id} ─────────────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var absence = await _db.Absences.FindAsync(id);
        if (absence == null) return NotFound();

        // Soft-Lock: kein Löschen wenn die Absenz in einer definitiv
        // abgeschlossenen Periode liegt (nicht bei provisorisch/HR/Akonto).
        var lockResult = await CheckLohnLockAsync(absence.EmployeeId, absence.DateFrom, absence.DateTo);
        if (lockResult != null) return lockResult;

        _db.Absences.Remove(absence);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── Mapping ───────────────────────────────────────────────────────────
    private static object MapToDto(Absence a, bool inLohnVerwendet) => new
    {
        id              = a.Id,
        employeeId      = a.EmployeeId,
        absenceType     = a.AbsenceType,
        dateFrom        = a.DateFrom.ToString("yyyy-MM-dd"),
        dateTo          = a.DateTo.ToString("yyyy-MM-dd"),
        workedDays      = a.WorkedDays,
        hoursCredited   = a.HoursCredited,
        prozent         = a.Prozent,
        notes           = a.Notes,
        dokumentId      = a.DokumentId,
        ferienfaehig    = a.Ferienfaehig,
        dienstplanBis   = a.DienstplanBis?.ToString("yyyy-MM-dd"),
        createdAt       = a.CreatedAt,
        inLohnVerwendet = inLohnVerwendet,
    };

    // ── Zeitgutschrift Krankheit/Unfall (Walter 05.10.2026) ──────────────

    /// <summary>Vertrag am Stichtag (sonst aktivster/jüngster) + seine Filiale.</summary>
    private async Task<(Employment? Vertrag, CompanyProfile? Filiale)> VertragAmAsync(int employeeId, DateOnly datum)
    {
        var dt = datum.ToDateTime(TimeOnly.MinValue);
        var alle = await _db.Employments.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .ToListAsync();
        var vertrag = alle
            .Where(e => e.ContractStartDate <= dt && (e.ContractEndDate == null || e.ContractEndDate >= dt))
            .OrderByDescending(e => e.ContractStartDate)
            .FirstOrDefault()
            ?? alle.OrderByDescending(e => e.IsActive).ThenByDescending(e => e.ContractStartDate).FirstOrDefault();
        CompanyProfile? filiale = vertrag?.CompanyProfileId is int cpId
            ? await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == cpId)
            : null;
        return (vertrag, filiale);
    }

    /// <summary>Nur Krank/Unfall; vor Absenzbeginn = kein Plan, nach Absenzende = Absenzende.</summary>
    public static DateOnly? DienstplanBisNormalisieren(string? wert, string? typ, DateOnly von, DateOnly bis)
    {
        if ((typ ?? "").ToUpperInvariant() is not ("KRANK" or "UNFALL")) return null;
        if (!DateOnly.TryParse(wert, out var d) || d < von) return null;
        return d > bis ? bis : d;
    }

    /// <summary>Angekreuzte Tage nach «Dienstplan bis» zählen nicht — gar nicht erst speichern.</summary>
    public static string? TageBisPlanende(string? workedDays, DateOnly? dienstplanBis)
    {
        if (dienstplanBis is not DateOnly ende) return workedDays;
        var tage = KrankUnfallZeitgutschrift.GeplanteTage(workedDays)
            .Where(d => d <= ende)
            .OrderBy(d => d)
            .Select(d => d.ToString("yyyy-MM-dd"));
        return System.Text.Json.JsonSerializer.Serialize(tage);
    }

    /// <summary>Krank/Unfall FIX/FIX-M/MTP: Stunden rechnet der Server, nicht die Maske.</summary>
    private async Task KrankUnfallStundenSetzenAsync(Absence a)
    {
        var (vertrag, filiale) = await VertragAmAsync(a.EmployeeId, a.DateFrom);
        if (vertrag == null || !KrankUnfallZeitgutschrift.Betrifft(a.AbsenceType, vertrag.EmploymentModel)) return;
        a.WorkedDays    = TageBisPlanende(a.WorkedDays, a.DienstplanBis);
        a.HoursCredited = Math.Round(KrankUnfallZeitgutschrift.Stunden(a, vertrag, filiale), 2);
    }

    public class ZeitgutschriftVorschauDto
    {
        public int     EmployeeId    { get; set; }
        public string  AbsenceType   { get; set; } = "";
        public string  DateFrom      { get; set; } = "";
        public string  DateTo        { get; set; } = "";
        public string? DienstplanBis { get; set; }
        public string? WorkedDays    { get; set; }
        public decimal? Prozent      { get; set; }
    }

    // ── POST /api/absences/zeitgutschrift-vorschau ────────────────────────
    // Rechnet die Krank-/Unfall-Gutschrift für die Maske — dieselbe Regel wie
    // Lohnrechnung und Speichern. Speichert nichts.
    [HttpPost("zeitgutschrift-vorschau")]
    public async Task<IActionResult> ZeitgutschriftVorschau([FromBody] ZeitgutschriftVorschauDto dto)
    {
        if (dto is null || dto.EmployeeId <= 0
            || !DateOnly.TryParse(dto.DateFrom, out var von)
            || !DateOnly.TryParse(dto.DateTo, out var bis)
            || bis < von || bis.DayNumber - von.DayNumber > 400)
            return BadRequest(new { error = "INVALID_PARAMS" });

        var typ = (dto.AbsenceType ?? "").Trim().ToUpperInvariant();
        var (vertrag, filiale) = await VertragAmAsync(dto.EmployeeId, von);
        if (vertrag == null || !KrankUnfallZeitgutschrift.Betrifft(typ, vertrag.EmploymentModel))
            return Ok(new { betrifft = false });

        var planBis = DienstplanBisNormalisieren(dto.DienstplanBis, typ, von, bis);
        var a = new Absence
        {
            EmployeeId    = dto.EmployeeId,
            AbsenceType   = typ,
            DateFrom      = von,
            DateTo        = bis,
            DienstplanBis = planBis,
            WorkedDays    = dto.WorkedDays,
            Prozent       = ClampProzent(dto.Prozent),
        };
        var tage = KrankUnfallZeitgutschrift.Tage(a, vertrag, filiale);
        decimal? betrieb = filiale?.NormalWeeklyHours;
        return Ok(new
        {
            betrifft          = true,
            modell            = vertrag.EmploymentModel,
            methode           = KrankUnfallZeitgutschrift.Methode(filiale?.ZeitgutschriftKrankMethode),
            wochenstunden     = Math.Round(KrankUnfallZeitgutschrift.Wochenstunden(vertrag, betrieb), 2),
            arbeitstage       = KrankUnfallZeitgutschrift.Arbeitstage(vertrag, betrieb),
            arbeitstageVonHand = vertrag.ArbeitstageProWoche != null,
            dienstplanBis     = planBis?.ToString("yyyy-MM-dd"),
            stunden           = Math.Round(tage.Sum(t => t.Stunden), 2),
            erklaerung        = KrankUnfallZeitgutschrift.Erklaerung(tage),
            tage              = tage.Select(t => new
            {
                datum   = t.Datum.ToString("yyyy-MM-dd"),
                art     = t.Art.ToString(),
                stunden = Math.Round(t.Stunden, 2),
            }),
        });
    }

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
    /// <summary>Nur FERIEN: arbeitsunfähig, aber ferienfähig (Walter 23.09.2026). NULL = unverändert.</summary>
    public bool?   Ferienfaehig { get; set; }
    /// <summary>Nur KRANK/UNFALL: Dienstplan bekannt bis (inkl.), leer = kein Plan → 1/7 (Walter 05.10.2026).</summary>
    public string? DienstplanBis { get; set; }
}

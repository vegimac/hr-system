using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmploymentsController : ControllerBase
{
    private readonly AppDbContext        _context;
    private readonly LohnEditLockService _editLock;

    public EmploymentsController(AppDbContext context, LohnEditLockService editLock)
    {
        _context  = context;
        _editLock = editLock;
    }

    /// <summary>
    /// Lohnlauf-Schutz für Verträge (Walter-Vorgabe 17.05.2026):
    /// Ein Vertrag gilt als „in Lohnlauf verwendet", wenn sein
    /// ContractStartDate VOR dem FirstAllowedDate der Filiale liegt.
    /// Editieren/Löschen ist dann gesperrt — stattdessen muss ein NEUER
    /// Vertrag (POST) mit ContractStartDate >= FirstAllowedDate angelegt
    /// werden; der offene wird automatisch beendet.
    /// admin/superuser werden im Service bypassed.
    /// </summary>
    private async Task<DateOnly?> GetFirstAllowedAsync(int companyProfileId)
        => await _editLock.GetFirstAllowedDateAsync(User, companyProfileId);

    private static bool IsInLohnVerwendet(Employment e, DateOnly? firstAllowed)
    {
        if (firstAllowed is null) return false;
        return DateOnly.FromDateTime(e.ContractStartDate) < firstAllowed.Value;
    }

    // GET /api/employments — alle Verträge
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var employments = await _context.Employments
            .Include(e => e.JobGroup)        // FK-Code für Frontend (Walter 26.05.2026)
            .OrderBy(e => e.EmployeeId)
            .ThenBy(e => e.ContractStartDate)
            .ToListAsync();

        return Ok(employments);
    }

    // GET /api/employments/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var employment = await _context.Employments
            .Include(e => e.JobGroup)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (employment == null)
            return NotFound();

        return Ok(employment);
    }

    // GET /api/employments/employee/{employeeId} — alle Verträge eines Mitarbeitenden
    [HttpGet("employee/{employeeId:int}")]
    public async Task<IActionResult> GetByEmployee(int employeeId)
    {
        var employments = await _context.Employments
            .Include(e => e.JobGroup)
            .Where(e => e.EmployeeId == employeeId)
            .OrderByDescending(e => e.ContractStartDate)
            .ToListAsync();

        // Pro Filiale FirstAllowedDate cachen — ein MA hat oft Verträge in
        // verschiedenen Filialen (z.B. nach Filialwechsel). Wir gruppieren
        // damit der Lock-Service nicht pro Vertrag erneut gefragt wird.
        // CompanyProfileId ist nullable — Legacy-Verträge ohne Filial-Zuordnung
        // werden ohne Lock-Prüfung behandelt.
        var firstAllowedByBranch = new Dictionary<int, DateOnly?>();
        foreach (var branchId in employments
                    .Where(e => e.CompanyProfileId.HasValue && e.CompanyProfileId.Value > 0)
                    .Select(e => e.CompanyProfileId!.Value)
                    .Distinct())
        {
            firstAllowedByBranch[branchId] = await GetFirstAllowedAsync(branchId);
        }

        // Anonymous-Objekt-Liste mit zusätzlichem inLohnVerwendet-Flag.
        var result = employments.Select(e =>
        {
            DateOnly? fa = null;
            if (e.CompanyProfileId.HasValue)
                firstAllowedByBranch.TryGetValue(e.CompanyProfileId.Value, out fa);
            return new
            {
                e.Id, e.EmployeeId, e.CompanyProfileId,
                e.ContractStartDate, e.ContractEndDate,
                e.EmploymentModel, e.SalaryType, e.ContractType,
                e.JobTitle,
                jobGroupId   = e.JobGroupId,
                jobGroupCode = e.JobGroup != null ? e.JobGroup.Code : null,
                e.EducationLevelCode,
                e.EmploymentPercentage,
                e.WeeklyHours, e.GuaranteedHoursPerWeek,
                e.MonthlySalaryFte, e.MonthlySalary, e.HourlyRate,
                e.EasyAtWorkManualOverride,
                e.VacationPaymentMode, e.ProbationPeriodMonths, e.ProbationEndDate,
                e.IsActive,
                inLohnVerwendet  = IsInLohnVerwendet(e, fa),
                firstAllowedDate = fa?.ToString("yyyy-MM-dd")
            };
        }).ToList();

        return Ok(result);
    }

    // POST /api/employments — neuer Vertrag (schliesst den offenen automatisch)
    [HttpPost]
    public async Task<IActionResult> Create(Employment employment)
    {
        var employeeExists = await _context.Employees
            .AnyAsync(e => e.Id == employment.EmployeeId);

        if (!employeeExists)
            return BadRequest(new { error = $"Mitarbeiter {employment.EmployeeId} nicht gefunden." });

        // Walter-Vorgabe 06.06.2026 (Stufe 1b): Ferien %, Feiertag %, 13. ML %
        // sind aus dem Vertrag entfernt — kommen jetzt aus der Filiale. Keine
        // Pflicht-Validierung mehr nötig.

        // Walter-Vorgabe 17.05.2026: ContractStartDate eines neuen Vertrags
        // darf NICHT rückwirkend in einer Periode liegen, die schon in
        // Verarbeitung ist. Frühester Beginn = FirstAllowedDate (= 1 Tag
        // nach letzter abgeschlossener oder in HR liegender Periode).
        // admin/superuser werden im Service bypassed.
        if (employment.CompanyProfileId.HasValue && employment.CompanyProfileId.Value > 0)
        {
            var firstAllowed = await GetFirstAllowedAsync(employment.CompanyProfileId.Value);
            if (firstAllowed.HasValue && DateOnly.FromDateTime(employment.ContractStartDate) < firstAllowed.Value)
            {
                return Conflict(new
                {
                    error            = "LOHN_EDIT_LOCKED",
                    message          = $"'Vertragsbeginn {employment.ContractStartDate:dd.MM.yyyy}' liegt in einer bereits in Verarbeitung befindlichen Lohnperiode. " +
                                       $"Frühester Vertragsbeginn: {firstAllowed.Value:dd.MM.yyyy}.",
                    firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
                });
            }
        }

        // Offenen Vertrag (ContractEndDate IS NULL) automatisch schliessen
        var openContract = await _context.Employments
            .Where(e => e.EmployeeId == employment.EmployeeId && e.ContractEndDate == null)
            .OrderByDescending(e => e.ContractStartDate)
            .FirstOrDefaultAsync();

        if (openContract != null)
        {
            // Ende = Tag vor Beginn des neuen Vertrags
            openContract.ContractEndDate = employment.ContractStartDate.AddDays(-1);
        }

        // Tatsächlicher Lohn aus FTE × Pensum berechnen (falls FTE vorhanden)
        if (employment.MonthlySalaryFte.HasValue && employment.EmploymentPercentage.HasValue)
            employment.MonthlySalary = Math.Round(
                employment.MonthlySalaryFte.Value * employment.EmploymentPercentage.Value / 100m, 2);

        // JobGroupId aus JobGroupCode resolven, falls nur der Code übermittelt
        // wurde (Frontend-Dropdown liefert den Code). JobTitle (Stellenbezeichnung)
        // bleibt unverändert — das ist Free-Text und hat nichts mit der
        // Funktionsgruppe zu tun. (Walter-Klarstellung 26.05.2026)
        if (!employment.JobGroupId.HasValue && !string.IsNullOrWhiteSpace(employment.JobGroupCode))
        {
            employment.JobGroupId = await _context.JobGroups
                .Where(g => g.Code == employment.JobGroupCode)
                .Select(g => (int?)g.Id)
                .FirstOrDefaultAsync();
        }

        _context.Employments.Add(employment);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = employment.Id }, new
        {
            employment,
            previousContractClosed = openContract != null
                ? $"Vorheriger Vertrag wurde per {openContract.ContractEndDate:dd.MM.yyyy} abgeschlossen."
                : null
        });
    }

    // GET /api/employments/{id}/exit-summary?exitDate=YYYY-MM-DD
    // Liefert eine Übersicht über aktuelle Saldi und Projektion bis Austritt.
    // Hilft dem Operator, den Austritt mit "Punktlandung" auf 0 zu planen
    // (verbleibende Sollstunden, verbleibender Ferienanspruch, etc.).
    [HttpGet("{id:int}/exit-summary")]
    public async Task<IActionResult> ExitSummary(int id, [FromQuery] DateTime exitDate)
    {
        var employment = await _context.Employments
            .Include(e => e.CompanyProfile)
            .Include(e => e.Employee)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (employment == null) return NotFound();

        var exitDateOnly = DateOnly.FromDateTime(exitDate.Date);
        var startDateOnly = DateOnly.FromDateTime(employment.ContractStartDate.Date);

        if (exitDateOnly < startDateOnly)
            return BadRequest(new { error = "Austrittsdatum liegt vor Vertragsbeginn." });

        // Letzten PayrollSaldo laden (höchstes Jahr/Monat)
        var lastSaldo = await _context.PayrollSaldos
            .Where(s => s.EmployeeId == employment.EmployeeId
                     && s.CompanyProfileId == employment.CompanyProfileId)
            .OrderByDescending(s => s.PeriodYear)
            .ThenByDescending(s => s.PeriodMonth)
            .FirstOrDefaultAsync();

        // Berechnungs-Stichtag: Ende der letzten geschlossenen Periode bzw.
        // Vertragsbeginn (falls noch keine Periode abgerechnet).
        // Lohnperiode = Kalendermonat (Walter 16.05.2026, Etappe 5f) — Stichtag =
        // letzter Tag des PeriodMonth. PayrollPeriodStartDay (Legacy) wird nicht
        // mehr ausgewertet.
        var company = employment.CompanyProfile;
        DateOnly fromDate;
        if (lastSaldo != null)
        {
            int year  = lastSaldo.PeriodYear;
            int month = lastSaldo.PeriodMonth;
            fromDate = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        }
        else
        {
            fromDate = startDateOnly;
        }

        // Berechnung: Restzeit von fromDate+1 bis exitDate
        DateOnly projFrom = fromDate.AddDays(1);
        if (projFrom > exitDateOnly) projFrom = exitDateOnly;
        int remainingDays = Math.Max(0, exitDateOnly.DayNumber - projFrom.DayNumber + 1);

        // Sollstunden bis Austritt (modellabhängig)
        decimal pct           = employment.EmploymentPercentage ?? 100m;
        decimal normalWeekly  = company?.NormalWeeklyHours ?? 42m;
        decimal weeklySoll    = employment.WeeklyHours ?? Math.Round(normalWeekly * pct / 100m, 2);
        decimal guaranteedH   = employment.GuaranteedHoursPerWeek ?? 0;
        bool isMtp = employment.EmploymentModel == "MTP";
        bool isFix = employment.EmploymentModel is "FIX" or "FIX-M";

        // Walter-Vorgabe 30.05.2026: MTP-Sollstunden pro-rata identisch zu FIX —
        // garantierte WoStd / 7 × Anzahl Tage (kein 52/365 mehr). Konsistent zur
        // Engine, die den Monats-Festlohn ebenfalls mit guaranteedH / 7 × Tage rechnet.
        decimal sollStundenRest = isMtp
            ? Math.Round(guaranteedH / 7m * remainingDays, 2)
            : isFix
                ? Math.Round(weeklySoll / 7m * remainingDays, 2)
                : 0m;  // UTP: keine Sollstunden

        // Ferien-Anspruch: 5 Wochen = 35 Tage/Jahr, 6 Wochen = 42 Tage/Jahr
        // Walter-Vorgabe 06.06.2026: Quelle = Filial-Defaults (5-Wo. bzw. 6-Wo.)
        // + altersaware Schwelle am Austrittsdatum, analog Engine.
        decimal vacPct = company?.DefaultVacationPercent5Weeks ?? 10.64m;
        if (employment.Employee?.DateOfBirth != null && company != null)
        {
            var dob = DateOnly.FromDateTime(employment.Employee.DateOfBirth.Value);
            if (dob.AddYears(company.VacationSixWeeksFromAge) <= exitDateOnly)
                vacPct = company.DefaultVacationPercent6Weeks ?? 13.04m;
        }
        decimal wochenFerien = vacPct <= 8.40m ? 4m : vacPct <= 11m ? 5m : vacPct <= 13.5m ? 6m : 7m;
        decimal ferienAnspruchJahr = wochenFerien * 7m;  // 5 Wo. = 35, 6 Wo. = 42

        // Zusätzlicher Anspruch zwischen Saldo-Stichtag und Austritt (anteilig).
        // Beispiel: Saldo per 20.4.2026 = 10.67 Tage. Austritt 30.4.2026 →
        //   10 Resttage × 35/365 = 0.96 Tage zusätzlicher Anspruch.
        //   Erwarteter Saldo bei Austritt = 10.67 + 0.96 = 11.63 Tage
        //   → bei Austritt entweder noch beziehen oder auszahlen.
        decimal ferienAnspruchRest = Math.Round(ferienAnspruchJahr * remainingDays / 365m, 2);
        decimal ferienSaldoStichtag = lastSaldo?.FerienTageSaldo ?? 0;
        decimal ferienErwarteterSaldoBeiAustritt = Math.Round(ferienSaldoStichtag + ferienAnspruchRest, 2);

        return Ok(new
        {
            employmentId       = id,
            exitDate           = exitDate.Date,
            employmentModel    = employment.EmploymentModel,
            isFixOrFixM        = isFix,
            isMtp              = isMtp,
            // Letzter Saldo-Stichtag und seine Werte
            saldoStand         = fromDate,
            saldoVorhanden     = lastSaldo != null,
            // Für Debugging/Transparenz: aus welcher Periode kommt der Saldo
            saldoQuelleYear    = lastSaldo?.PeriodYear,
            saldoQuelleMonth   = lastSaldo?.PeriodMonth,
            saldoQuelleStatus  = lastSaldo?.Status,
            hourSaldo          = lastSaldo?.HourSaldo ?? 0,
            ferienTageSaldo    = ferienSaldoStichtag,
            feiertagTageSaldo  = lastSaldo?.FeiertagTageSaldo ?? 0,
            ferienGeldSaldo    = lastSaldo?.FerienGeldSaldo ?? 0,
            thirteenthAccumulated = lastSaldo?.ThirteenthMonthAccumulated ?? 0,
            // Projektion bis Exit
            remainingDays      = remainingDays,
            sollStundenRest    = sollStundenRest,
            // Negative HourSaldo → MA muss noch arbeiten; Positive → Mehrstunden auszahlen
            stundenNochZuLeisten = Math.Round(sollStundenRest - (lastSaldo?.HourSaldo ?? 0), 2),
            ferienAnspruchJahr,
            // Neue, klar interpretierbare Felder für die Anzeige:
            ferienAnspruchRest,                   // zusätzlicher Anspruch in der Restzeit
            ferienErwarteterSaldoBeiAustritt      // Saldo + Rest = was bei Austritt offen bleibt
        });
    }

    // POST /api/employments/{id}/terminate — Austritt erfassen
    // Setzt contract_end_date am aktiven Vertrag und employee.exit_date.
    // Hinweis: CH-Recht verlangt i.d.R. Austritt auf Monatsende — Frontend
    // schlägt das vor. Wenn der Austrittstag mitten in der Lohnperiode
    // liegt, rechnet PayrollController automatisch eine Kurzperiode.
    public class TerminateDto { public DateTime ExitDate { get; set; } }

    [HttpPost("{id:int}/terminate")]
    public async Task<IActionResult> Terminate(int id, [FromBody] TerminateDto dto)
    {
        var employment = await _context.Employments.FindAsync(id);
        if (employment == null) return NotFound(new { error = "Vertrag nicht gefunden." });
        if (employment.ContractEndDate != null)
            return BadRequest(new { error = "Vertrag ist bereits abgeschlossen." });

        var exit = dto.ExitDate.Date;
        if (exit < employment.ContractStartDate.Date)
            return BadRequest(new { error = "Austrittsdatum liegt vor Vertragsbeginn." });

        // Walter-Vorgabe 17.05.2026: Austrittsdatum darf nicht in einer
        // bereits in Verarbeitung befindlichen Lohnperiode liegen — sonst
        // wäre die letzte Abrechnung schon gerechnet ohne dass das System
        // vom Austritt wusste. Frühestes Austrittsdatum = FirstAllowedDate
        // (= erster Tag der ersten noch offenen Periode). Wenn Walter den
        // MA z.B. per 31.01.2026 austragen will und Januar bei HR liegt,
        // muss er erst die Periode zurücksetzen.
        DateOnly? firstAllowed = employment.CompanyProfileId.HasValue
            ? await GetFirstAllowedAsync(employment.CompanyProfileId.Value)
            : null;
        if (firstAllowed.HasValue && DateOnly.FromDateTime(exit) < firstAllowed.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Austrittsdatum {exit:dd.MM.yyyy} liegt in einer bereits in Verarbeitung befindlichen Lohnperiode. " +
                                   $"Frühestes erlaubtes Austrittsdatum: {firstAllowed.Value:dd.MM.yyyy}. " +
                                   $"Falls der MA wirklich früher austritt, muss die laufende Periode erst zurückgesetzt werden.",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });
        }

        employment.ContractEndDate = exit;

        // Employee.ExitDate spiegeln, damit der MA in Übersichten als
        // "ausgetreten" geführt wird.
        var employee = await _context.Employees.FindAsync(employment.EmployeeId);
        if (employee != null)
        {
            employee.ExitDate = exit;
        }

        await _context.SaveChangesAsync();
        return Ok(new {
            employment,
            message = $"Austritt per {exit:dd.MM.yyyy} erfasst."
        });
    }

    // POST /api/employments/{id}/reopen — Austritt rückgängig machen
    [HttpPost("{id:int}/reopen")]
    public async Task<IActionResult> Reopen(int id)
    {
        var employment = await _context.Employments.FindAsync(id);
        if (employment == null) return NotFound(new { error = "Vertrag nicht gefunden." });

        // Es darf keinen Nachfolge-Vertrag geben, sonst Lücke
        var hasNewer = await _context.Employments.AnyAsync(e =>
            e.EmployeeId == employment.EmployeeId
            && e.Id != employment.Id
            && e.ContractStartDate > employment.ContractStartDate);
        if (hasNewer)
            return BadRequest(new { error = "Es existiert bereits ein neuerer Vertrag. Vertrag kann nicht wieder geöffnet werden." });

        // Walter 17.05.2026: Wieder-Öffnen ist tabu wenn das aktuelle Austritts-
        // datum in einer bereits verarbeiteten Periode liegt — dann wurde der
        // Austritt schon abgerechnet und das Wiederöffnen würde rückwirkend
        // den Vertrag in einer geschlossenen Periode reaktivieren.
        DateOnly? firstAllowed = employment.CompanyProfileId.HasValue
            ? await GetFirstAllowedAsync(employment.CompanyProfileId.Value)
            : null;
        if (firstAllowed.HasValue
            && employment.ContractEndDate.HasValue
            && DateOnly.FromDateTime(employment.ContractEndDate.Value) < firstAllowed.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Austritt ist bereits in einer abgerechneten Periode (Austrittsdatum {employment.ContractEndDate:dd.MM.yyyy}). Reopen würde rückwirkend in einer geschlossenen Periode wirken.",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });
        }

        employment.ContractEndDate = null;
        var employee = await _context.Employees.FindAsync(employment.EmployeeId);
        if (employee != null) employee.ExitDate = null;

        await _context.SaveChangesAsync();
        return Ok(new { employment, message = "Vertrag wieder geöffnet." });
    }

    // PUT /api/employments/{id} — Vertrag korrigieren (nur der aktive, ohne ContractEndDate)
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, Employment dto)
    {
        var existing = await _context.Employments.FindAsync(id);
        if (existing == null) return NotFound();

        // Walter-Vorgabe 06.06.2026 (Stufe 1b): Ferien %, Feiertag %, 13. ML %
        // sind aus dem Vertrag entfernt — kommen jetzt aus der Filiale. Keine
        // Pflicht-Validierung mehr nötig.

        // Walter-Vorgabe 17.05.2026: Vertrag der bereits in einem Lohnlauf
        // verwendet wurde, ist tabu für Edit. Stattdessen muss ein neuer
        // Vertrag mit "Beginn ab X" angelegt werden — der schliesst den
        // offenen automatisch ab (POST-Endpoint).
        // Legacy-Verträge ohne CompanyProfileId können nicht geprüft werden
        // (keine Filial-Zuordnung) — die werden ohne Lock-Check durchgelassen.
        DateOnly? firstAllowed = existing.CompanyProfileId.HasValue
            ? await GetFirstAllowedAsync(existing.CompanyProfileId.Value)
            : null;
        if (IsInLohnVerwendet(existing, firstAllowed))
        {
            return Conflict(new
            {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Dieser Vertrag (Beginn {existing.ContractStartDate:dd.MM.yyyy}) wurde bereits in einem Lohnlauf verwendet. " +
                                   $"Für Änderungen bitte einen neuen Vertrag ab frühestens {firstAllowed:dd.MM.yyyy} anlegen — der bestehende wird dann automatisch beendet.",
                firstAllowedDate = firstAllowed?.ToString("yyyy-MM-dd")
            });
        }

        // ContractStartDate, ContractEndDate, ContractType etc. sind editierbar —
        // Walter braucht das z.B. um eine falsche Lohn-Eingabe nach dem CSV-Import
        // nachträglich zu korrigieren oder ein Austrittsdatum anzupassen.
        // EmployeeId und CompanyProfileId bleiben unverändert (Vertrag bleibt
        // demselben MA in derselben Filiale zugewiesen).
        if (dto.ContractStartDate != default)
            existing.ContractStartDate  = dto.ContractStartDate;
        existing.ContractEndDate        = dto.ContractEndDate;
        existing.EmploymentModel        = dto.EmploymentModel;
        existing.SalaryType             = dto.SalaryType;
        existing.ContractType           = dto.ContractType;
        // Walter-Klarstellung 26.05.2026: JobTitle ist Free-Text-Stellenbezeichnung
        // (z.B. „Shift Coordinator") und wird 1:1 gespeichert. JobGroupId ist die
        // FK-Referenz auf die Funktionsgruppe (z.B. SHIFT_LEADER_7_PLUS) und
        // steuert den Mindestlohn — komplett unabhängig vom Stellenbezeichnungs-Text.
        existing.JobTitle = dto.JobTitle;
        // JobGroupId-Quelle (Priorität): explizit übergebene id → JobGroupCode
        // (vom Frontend-Dropdown) → kein Update.
        if (dto.JobGroupId.HasValue)
        {
            existing.JobGroupId = dto.JobGroupId;
        }
        else if (!string.IsNullOrWhiteSpace(dto.JobGroupCode))
        {
            existing.JobGroupId = await _context.JobGroups
                .Where(g => g.Code == dto.JobGroupCode)
                .Select(g => (int?)g.Id)
                .FirstOrDefaultAsync();
        }
        existing.EducationLevelCode     = dto.EducationLevelCode;
        existing.EmploymentPercentage   = dto.EmploymentPercentage;
        existing.WeeklyHours            = dto.WeeklyHours;
        existing.GuaranteedHoursPerWeek = dto.GuaranteedHoursPerWeek;
        existing.MonthlySalaryFte       = dto.MonthlySalaryFte;
        // Tatsächlicher Lohn = FTE-Lohn × Pensum%; Fallback auf direkt übermittelten Wert
        existing.MonthlySalary = dto.MonthlySalaryFte.HasValue && dto.EmploymentPercentage.HasValue
            ? Math.Round(dto.MonthlySalaryFte.Value * dto.EmploymentPercentage.Value / 100m, 2)
            : dto.MonthlySalary;
        existing.HourlyRate             = dto.HourlyRate;
        existing.VacationPaymentMode    = dto.VacationPaymentMode;
        existing.ProbationPeriodMonths  = dto.ProbationPeriodMonths;
        existing.ProbationEndDate       = dto.ProbationEndDate;
        existing.IsActive               = dto.IsActive;
        existing.EasyAtWorkManualOverride = dto.EasyAtWorkManualOverride;

        await _context.SaveChangesAsync();
        return Ok(existing);
    }

    // DELETE /api/employments/{id} — Vertrag endgültig löschen.
    // Nur für admin/superuser. Walter-Vorgabe 01.06.2026: löschbar SOLANGE
    // der Vertrag in KEINER abgeschlossenen Lohnperiode verwendet wurde
    // (= kein Snapshot in einer Periode mit Status='abgeschlossen', dessen
    // Periodenzeitraum mit dem Vertrags-Zeitraum überlappt). Verträge in
    // offenen / provisorisch_abgeschlossenen Perioden sind frei löschbar —
    // dort hängt nichts Finales dran. ?force=true bleibt als Notfall-Knopf
    // erhalten, falls Walter doch mal eine abgeschlossene Periode wegräumt.
    [Authorize(Roles = "admin,superuser")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool force = false)
    {
        var employment = await _context.Employments.FindAsync(id);
        if (employment == null)
            return NotFound(new { error = "Vertrag nicht gefunden." });

        var startDateOnly = DateOnly.FromDateTime(employment.ContractStartDate);
        var endDateOnly   = employment.ContractEndDate.HasValue
            ? (DateOnly?)DateOnly.FromDateTime(employment.ContractEndDate.Value)
            : null;

        // Snapshot in abgeschlossener Periode mit Zeit-Überlappung?
        var hasFinalPayroll = await (
            from snap in _context.PayrollSnapshots
            join per in _context.PayrollPerioden on snap.PayrollPeriodeId equals per.Id
            where snap.EmployeeId == employment.EmployeeId
               && per.Status == "abgeschlossen"
               && per.PeriodTo   >= startDateOnly
               && (endDateOnly == null || per.PeriodFrom <= endDateOnly)
            select snap.Id
        ).AnyAsync();

        if (hasFinalPayroll && !force)
        {
            return Conflict(new
            {
                error   = "LOHN_FINAL_BLOCKING",
                message = "Dieser Vertrag wurde in einer abgeschlossenen Lohnperiode verwendet — "
                        + "Löschen würde den Lohnbeleg invalidieren. Mit ?force=true (Admin) trotzdem löschbar.",
                requiresForce = true
            });
        }

        _context.Employments.Remove(employment);
        await _context.SaveChangesAsync();
        return Ok(new { success = true, deletedId = id, forced = hasFinalPayroll });
    }

    // GET /api/employments/{id}/can-delete — Frontend-Vorab-Check.
    // Liefert { canDelete, reason? } damit der Löschen-Button im Vertrags-
    // Modal nur erscheint wenn er auch wirklich klickbar ist.
    [Authorize(Roles = "admin,superuser")]
    [HttpGet("{id:int}/can-delete")]
    public async Task<IActionResult> CanDelete(int id)
    {
        var employment = await _context.Employments.FindAsync(id);
        if (employment == null) return NotFound();

        var startDateOnly = DateOnly.FromDateTime(employment.ContractStartDate);
        var endDateOnly   = employment.ContractEndDate.HasValue
            ? (DateOnly?)DateOnly.FromDateTime(employment.ContractEndDate.Value)
            : null;

        var hasFinalPayroll = await (
            from snap in _context.PayrollSnapshots
            join per in _context.PayrollPerioden on snap.PayrollPeriodeId equals per.Id
            where snap.EmployeeId == employment.EmployeeId
               && per.Status == "abgeschlossen"
               && per.PeriodTo   >= startDateOnly
               && (endDateOnly == null || per.PeriodFrom <= endDateOnly)
            select snap.Id
        ).AnyAsync();

        return Ok(new {
            canDelete = !hasFinalPayroll,
            reason = hasFinalPayroll
                ? "In abgeschlossener Lohnperiode verwendet."
                : null
        });
    }

    // GET /api/employments/unassigned-count — Anzahl Verträge ohne Filial-Zuordnung.
    // Diagnose-Hilfe: zeigt wie viele Legacy-Datensätze noch keine company_profile_id haben.
    [Authorize(Roles = "admin,superuser")]
    [HttpGet("unassigned-count")]
    public async Task<IActionResult> UnassignedCount()
    {
        var count = await _context.Employments.CountAsync(e => e.CompanyProfileId == null);
        return Ok(new { unassigned = count });
    }

    // POST /api/employments/assign-unassigned/{companyProfileId} — Massnahme:
    // alle Verträge ohne Filial-Zuordnung werden der angegebenen Filiale zugeordnet.
    // Nutzung: Nach Filial-Wechsel die Sicht auf "Sursee" stellen, dann diesen Aufruf
    // → alle MA mit unzugewiesenen Verträgen werden zu Sursee zugeordnet.
    [Authorize(Roles = "admin,superuser")]
    [HttpPost("assign-unassigned/{companyProfileId:int}")]
    public async Task<IActionResult> AssignUnassigned(int companyProfileId)
    {
        var company = await _context.CompanyProfiles.FindAsync(companyProfileId);
        if (company == null)
            return NotFound(new { error = "Filiale nicht gefunden." });

        var orphans = await _context.Employments
            .Where(e => e.CompanyProfileId == null)
            .ToListAsync();

        foreach (var emp in orphans)
        {
            emp.CompanyProfileId = companyProfileId;
        }
        await _context.SaveChangesAsync();
        return Ok(new
        {
            success = true,
            assigned = orphans.Count,
            companyProfileId,
            companyName = company.CompanyName
        });
    }

}

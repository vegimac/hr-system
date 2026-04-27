using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmploymentsController : ControllerBase
{
    private readonly AppDbContext _context;

    public EmploymentsController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/employments — alle Verträge
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var employments = await _context.Employments
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
            .Where(e => e.EmployeeId == employeeId)
            .OrderByDescending(e => e.ContractStartDate)
            .ToListAsync();

        return Ok(employments);
    }

    // POST /api/employments — neuer Vertrag (schliesst den offenen automatisch)
    [HttpPost]
    public async Task<IActionResult> Create(Employment employment)
    {
        var employeeExists = await _context.Employees
            .AnyAsync(e => e.Id == employment.EmployeeId);

        if (!employeeExists)
            return BadRequest(new { error = $"Mitarbeiter {employment.EmployeeId} nicht gefunden." });

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
        // Für die Stunden-Projektion: vom Stichtag bis exitDate.
        var company = employment.CompanyProfile;
        int startDay = company?.PayrollPeriodStartDay ?? 1;
        DateOnly fromDate;
        if (lastSaldo != null)
        {
            // Letzte Periode endete am Tag vor Periodenstart des Folgemonats.
            // Für startDay=21: PeriodMonth=3 → Periode 21.2.-20.3., nächste Periode beginnt 21.3.
            int year = lastSaldo.PeriodYear;
            int month = lastSaldo.PeriodMonth;
            // Folge-Periode startet am startDay des AKTUELLEN Monats (PeriodMonth bezeichnet das Auszahlungs-Monat,
            // was der Periode vom 21. des Vormonats bis 20. des Auszahlungs-Monats entspricht).
            // → Stichtag = 20. des PeriodMonth (Ende der letzten Periode).
            fromDate = startDay > 1
                ? new DateOnly(year, month, startDay - 1)
                : new DateOnly(year, month, DateTime.DaysInMonth(year, month));
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

        decimal sollStundenRest = isMtp
            ? Math.Round(guaranteedH * 52m / 365m * remainingDays, 2)
            : isFix
                ? Math.Round(weeklySoll / 7m * remainingDays, 2)
                : 0m;  // UTP: keine Sollstunden

        // Ferien-Anspruch: 5 Wochen = 35 Tage/Jahr, 6 Wochen = 42 Tage/Jahr
        // (Kalendertage × Wochen; gleiche Logik wie PayrollController).
        // Monatlicher Accrual: Jahresanspruch / 12.
        decimal ferienAnspruchJahr = 0m;
        if (employment.VacationPercent.HasValue && employment.VacationPercent.Value > 0)
        {
            decimal vp = employment.VacationPercent.Value;
            decimal wochenFerien = vp <= 8.40m ? 4m : vp <= 11m ? 5m : vp <= 13.5m ? 6m : 7m;
            ferienAnspruchJahr = wochenFerien * 7m;  // 5 Wo. = 35, 6 Wo. = 42
        }

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

        // Nur aktive Verträge dürfen bearbeitet werden
        if (existing.ContractEndDate != null)
            return BadRequest(new { error = "Abgeschlossene Verträge können nicht mehr bearbeitet werden." });

        // Felder übernehmen (ContractStartDate und EmployeeId bleiben)
        existing.EmploymentModel        = dto.EmploymentModel;
        existing.SalaryType             = dto.SalaryType;
        existing.ContractType           = dto.ContractType;
        existing.JobTitle               = dto.JobTitle;
        existing.EmploymentPercentage   = dto.EmploymentPercentage;
        existing.WeeklyHours            = dto.WeeklyHours;
        existing.GuaranteedHoursPerWeek = dto.GuaranteedHoursPerWeek;
        existing.MonthlySalaryFte       = dto.MonthlySalaryFte;
        // Tatsächlicher Lohn = FTE-Lohn × Pensum%; Fallback auf direkt übermittelten Wert
        existing.MonthlySalary = dto.MonthlySalaryFte.HasValue && dto.EmploymentPercentage.HasValue
            ? Math.Round(dto.MonthlySalaryFte.Value * dto.EmploymentPercentage.Value / 100m, 2)
            : dto.MonthlySalary;
        existing.HourlyRate             = dto.HourlyRate;
        existing.VacationPercent        = dto.VacationPercent;
        existing.HolidayPercent         = dto.HolidayPercent;
        existing.ThirteenthSalaryPercent= dto.ThirteenthSalaryPercent;
        existing.VacationPaymentMode    = dto.VacationPaymentMode;
        existing.ProbationPeriodMonths  = dto.ProbationPeriodMonths;
        existing.ProbationEndDate       = dto.ProbationEndDate;

        await _context.SaveChangesAsync();
        return Ok(existing);
    }

}

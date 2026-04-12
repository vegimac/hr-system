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

using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmployeesController : ControllerBase
{
    private readonly AppDbContext _context;

    public EmployeesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var employees = await _context.Employees
            .Include(e => e.Employments)
            .OrderBy(e => ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim())
            .ToListAsync();

        return Ok(employees);
    }

    [HttpGet("lookup")]
    public async Task<IActionResult> GetLookup()
    {
        var employees = await _context.Employees
            .Where(e => e.IsActive)
            .OrderBy(e => ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim())
            .Select(e => new
            {
                Id = e.Id,
                DisplayName = ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim() + " (" + e.EmployeeNumber + ")"
            })
            .ToListAsync();

        return Ok(employees);
    }

    [HttpGet("lookup/company/{companyId:int}")]
    public async Task<IActionResult> GetEmployeesForCompany(int companyId)
    {
        var company = await _context.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (company == null)
            return NotFound("Company not found.");

        var restaurantPrefix = NormalizeRestaurantPrefix(company.RestaurantCode);

        var employees = await _context.Employees
            .Where(e => e.IsActive)
            .ToListAsync();

        var filtered = employees
            .Where(e => NormalizeEmployeeNumber(e.EmployeeNumber).StartsWith(restaurantPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim())
            .Select(e => new
            {
                Id = e.Id,
                DisplayName = ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim() + " (" + e.EmployeeNumber + ")",
                Gender = e.Gender,
                DateOfBirth = e.DateOfBirth  // NEU
            })
            .ToList();

        return Ok(filtered);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var employee = await _context.Employees
            .Include(e => e.Employments.OrderByDescending(c => c.ContractStartDate))
            .FirstOrDefaultAsync(e => e.Id == id);

        if (employee == null)
            return NotFound();

        // Aktiver Vertrag = ContractEndDate IS NULL (kein Enddatum = laufend)
        // Fallback: neuester Vertrag
        var active = employee.Employments.FirstOrDefault(c => c.ContractEndDate == null)
                  ?? employee.Employments.FirstOrDefault();

        // Flache Felder des aktiven Vertrags direkt in die Antwort einbauen
        // damit das bestehende UI (emp.employmentModel, emp.employmentPercentage usw.)
        // ohne Änderung weiter funktioniert
        return Ok(new
        {
            // ── Employee-Felder ──────────────────────────────────────────
            employee.Id,
            employee.EmployeeNumber,
            employee.Salutation,
            employee.FirstName,
            employee.LastName,
            employee.Gender,
            employee.DateOfBirth,
            employee.LanguageCode,
            employee.Nationality,
            employee.NationalityId,
            employee.PhoneMobile,
            employee.Email,
            employee.EntryDate,
            employee.ExitDate,
            employee.IsActive,
            employee.PermitTypeId,
            employee.PermitExpiryDate,
            employee.QuellensteuerBefreitAb,
            employee.SocialSecurityNumber,
            employee.Zivilstand,

            // ── Felder aus aktivem Vertrag (flach) ───────────────────────
            employmentModel        = active?.EmploymentModel,
            salaryType             = active?.SalaryType,
            contractStartDate      = active?.ContractStartDate,
            contractEndDate        = active?.ContractEndDate,
            contractType           = active?.ContractType,
            jobTitle               = active?.JobTitle,
            employmentPercentage   = active?.EmploymentPercentage,
            weeklyHours            = active?.WeeklyHours,
            guaranteedHoursPerWeek = active?.GuaranteedHoursPerWeek,
            monthlySalary          = active?.MonthlySalary,
            hourlyRate             = active?.HourlyRate,
            vacationPercent        = active?.VacationPercent,
            holidayPercent         = active?.HolidayPercent,
            thirteenthSalaryPercent= active?.ThirteenthSalaryPercent,
            vacationPaymentMode    = active?.VacationPaymentMode,
            probationPeriodMonths  = active?.ProbationPeriodMonths,
            probationEndDate       = active?.ProbationEndDate,
            activeContractId       = active?.Id,

            // ── Alle Verträge (History) ──────────────────────────────────
            employments = employee.Employments
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(Employee employee)
    {
        _context.Employees.Add(employee);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = employee.Id }, employee);
    }

    // PUT /api/employees/{id} – Mitarbeiterstammdaten aktualisieren
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] EmployeeUpdateDto dto)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee is null) return NotFound();

        // ── Personalien ───────────────────────────────────────────────────
        if (dto.FirstName    is not null) employee.FirstName    = dto.FirstName;
        if (dto.LastName     is not null) employee.LastName     = dto.LastName;
        if (dto.Salutation   is not null) employee.Salutation   = dto.Salutation   == "" ? null : dto.Salutation;
        if (dto.Gender       is not null) employee.Gender       = dto.Gender       == "" ? null : dto.Gender;
        if (dto.DateOfBirth  is not null) employee.DateOfBirth  = dto.DateOfBirth;
        if (dto.LanguageCode is not null) employee.LanguageCode = dto.LanguageCode == "" ? null : dto.LanguageCode;
        if (dto.NationalityId.HasValue)   employee.NationalityId = dto.NationalityId == 0 ? null : dto.NationalityId;
        if (dto.PhoneMobile  is not null) employee.PhoneMobile  = dto.PhoneMobile  == "" ? null : dto.PhoneMobile;
        if (dto.Email        is not null) employee.Email        = dto.Email        == "" ? null : dto.Email;

        // ── Adresse ───────────────────────────────────────────────────────
        if (dto.Street      is not null) employee.Street      = dto.Street      == "" ? null : dto.Street;
        if (dto.HouseNumber is not null) employee.HouseNumber = dto.HouseNumber == "" ? null : dto.HouseNumber;
        if (dto.ZipCode     is not null) employee.ZipCode     = dto.ZipCode     == "" ? null : dto.ZipCode;
        if (dto.City        is not null) employee.City        = dto.City        == "" ? null : dto.City;
        if (dto.Country     is not null) employee.Country     = dto.Country     == "" ? null : dto.Country;

        // ── Aufenthalt ────────────────────────────────────────────────────
        if (dto.PermitTypeId.HasValue)     employee.PermitTypeId     = dto.PermitTypeId == 0 ? null : dto.PermitTypeId;
        if (dto.PermitExpiryDate.HasValue) employee.PermitExpiryDate = dto.PermitExpiryDate;
        if (dto.QuellensteuerBefreitAbSet) employee.QuellensteuerBefreitAb = dto.QuellensteuerBefreitAb;

        // ── Ein-/Austritt ─────────────────────────────────────────────────
        if (dto.EntryDate.HasValue) employee.EntryDate = dto.EntryDate;
        if (dto.ExitDateSet)        employee.ExitDate  = dto.ExitDate;

        // ── ALV / Zwischenverdienst ───────────────────────────────────────
        if (dto.AhvNummer  is not null) employee.SocialSecurityNumber = dto.AhvNummer == "" ? null : dto.AhvNummer;
        if (dto.Zivilstand is not null) employee.Zivilstand = dto.Zivilstand == "" ? null : dto.Zivilstand;

        await _context.SaveChangesAsync();
        return Ok(employee);
    }

    // PUT /api/employees/{id}/employment/{employmentId} – Vertragsdaten aktualisieren
    [HttpPut("{id:int}/employment/{employmentId:int}")]
    public async Task<IActionResult> UpdateEmployment(int id, int employmentId, [FromBody] EmploymentUpdateDto dto)
    {
        var emp = await _context.Employments
            .FirstOrDefaultAsync(e => e.Id == employmentId && e.EmployeeId == id);
        if (emp is null) return NotFound();

        if (dto.JobTitle        is not null) emp.JobTitle        = dto.JobTitle        == "" ? null : dto.JobTitle;
        if (dto.ContractType    is not null) emp.ContractType    = dto.ContractType    == "" ? null : dto.ContractType;
        if (dto.EmploymentModel is not null) emp.EmploymentModel = dto.EmploymentModel;
        if (dto.EmploymentPercentage.HasValue)    emp.EmploymentPercentage    = dto.EmploymentPercentage;
        if (dto.WeeklyHours.HasValue)             emp.WeeklyHours             = dto.WeeklyHours;
        if (dto.GuaranteedHoursPerWeek.HasValue)  emp.GuaranteedHoursPerWeek  = dto.GuaranteedHoursPerWeek;
        if (dto.HourlyRate.HasValue)              emp.HourlyRate              = dto.HourlyRate;
        if (dto.MonthlySalary.HasValue)           emp.MonthlySalary           = dto.MonthlySalary;
        if (dto.MonthlySalaryFte.HasValue)        emp.MonthlySalaryFte        = dto.MonthlySalaryFte;
        if (dto.VacationPercent.HasValue)         emp.VacationPercent         = dto.VacationPercent;
        if (dto.HolidayPercent.HasValue)          emp.HolidayPercent          = dto.HolidayPercent;
        if (dto.ThirteenthSalaryPercent.HasValue) emp.ThirteenthSalaryPercent = dto.ThirteenthSalaryPercent;
        if (dto.ContractStartDate.HasValue)       emp.ContractStartDate       = dto.ContractStartDate.Value;
        if (dto.ContractEndDateSet)               emp.ContractEndDate         = dto.ContractEndDate;
        if (dto.ProbationPeriodMonths.HasValue)   emp.ProbationPeriodMonths   = dto.ProbationPeriodMonths;
        if (dto.ProbationEndDate.HasValue)        emp.ProbationEndDate        = dto.ProbationEndDate;

        await _context.SaveChangesAsync();
        return Ok(emp);
    }

    private static string NormalizeRestaurantPrefix(string? restaurantCode)
    {
        var digits = Regex.Replace(restaurantCode ?? "", @"\D", "");
        digits = digits.TrimStart('0');
        return string.IsNullOrWhiteSpace(digits) ? "" : digits;
    }

    private static string NormalizeEmployeeNumber(string? employeeNumber)
    {
        return Regex.Replace(employeeNumber ?? "", @"\s", "");
    }
}

/// <summary>DTO für Updates auf Employee-Stammdaten (Personalien, Adresse, Aufenthalt).</summary>
public class EmployeeUpdateDto
{
    // Personalien
    public string?   FirstName    { get; set; }
    public string?   LastName     { get; set; }
    public string?   Salutation   { get; set; }
    public string?   Gender       { get; set; }
    public DateTime? DateOfBirth  { get; set; }
    public string?   LanguageCode { get; set; }
    public int?      NationalityId { get; set; }
    public string?   PhoneMobile  { get; set; }
    public string?   Email        { get; set; }

    // Adresse
    public string?   Street      { get; set; }
    public string?   HouseNumber { get; set; }
    public string?   ZipCode     { get; set; }
    public string?   City        { get; set; }
    public string?   Country     { get; set; }

    // Aufenthalt
    public int?      PermitTypeId     { get; set; }
    public DateTime? PermitExpiryDate { get; set; }

    /// <summary>Wenn true, wird QuellensteuerBefreitAb gesetzt (auch wenn null → Befreiung aufheben).</summary>
    public bool      QuellensteuerBefreitAbSet { get; set; } = false;
    public DateOnly? QuellensteuerBefreitAb    { get; set; }

    // Ein-/Austritt
    public DateTime? EntryDate   { get; set; }
    public bool      ExitDateSet { get; set; } = false;
    public DateTime? ExitDate    { get; set; }

    // ALV / Zwischenverdienst
    public string? AhvNummer  { get; set; }
    public string? Zivilstand { get; set; }
}

/// <summary>DTO für Updates auf Employment-Vertragsdaten.</summary>
public class EmploymentUpdateDto
{
    public string?   JobTitle               { get; set; }
    public string?   ContractType           { get; set; }
    public string?   EmploymentModel        { get; set; }
    public decimal?  EmploymentPercentage   { get; set; }
    public decimal?  WeeklyHours            { get; set; }
    public decimal?  GuaranteedHoursPerWeek { get; set; }
    public decimal?  HourlyRate             { get; set; }
    public decimal?  MonthlySalary          { get; set; }
    public decimal?  MonthlySalaryFte       { get; set; }
    public decimal?  VacationPercent        { get; set; }
    public decimal?  HolidayPercent         { get; set; }
    public decimal?  ThirteenthSalaryPercent { get; set; }
    public DateTime? ContractStartDate      { get; set; }
    public bool      ContractEndDateSet     { get; set; } = false;
    public DateTime? ContractEndDate        { get; set; }
    public int?      ProbationPeriodMonths  { get; set; }
    public DateTime? ProbationEndDate       { get; set; }
}
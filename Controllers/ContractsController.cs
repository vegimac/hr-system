using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ContractsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ContractsController(AppDbContext context)
    {
        _context = context;
    }

    private static async Task<string?> GetJobTitleDisplayName(AppDbContext db, string? code, string lang)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var key = $"{code}.NAME";
        return await db.AppTexts
            .Where(t => t.Module == "JOB_GROUP" && t.TextKey == key && t.LanguageCode == lang && t.IsActive)
            .Select(t => t.Content)
            .FirstOrDefaultAsync();
    }

    [HttpGet("employment/{employmentId}/pdf")]
    public async Task<IActionResult> DownloadContractPdf(int employmentId)
    {
        var employment = await _context.Employments
            .Include(e => e.Employee)
            .FirstOrDefaultAsync(e => e.Id == employmentId);
        if (employment == null) return NotFound("Employment not found.");
        if (employment.CompanyProfileId == null) return BadRequest("No company profile.");

        // Neu: ohne .Include(c => c.Signatories)
        var company = await _context.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == employment.CompanyProfileId.Value);
        if (company == null) return BadRequest("Company profile not found.");

        var employee = employment.Employee;
        if (employee == null) return BadRequest("Employee not found.");

        // Neu: Unterzeichner aus UserBranchAccess laden
        var signatory = await _context.UserBranchAccesses
            .Include(s => s.User)
            .Where(s => s.CompanyProfileId == employment.CompanyProfileId.Value
                     && s.IsDefault == true)
            .FirstOrDefaultAsync();

        var salaryType = employment.SalaryType ?? GetSalaryType(employment.EmploymentModel);
        var jobTitleDisplay = await GetJobTitleDisplayName(_context, employment.JobTitle, "de") ?? employment.JobTitle ?? "";

        var addressParts = new[] { company.Street, company.HouseNumber }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var streetAddress = string.Join(" ", addressParts);
        var companyAddress = $"{streetAddress}, {company.ZipCode} {company.City}".Trim().TrimStart(',').Trim();

        var input = new ContractPdfInput(
            CompanyName:             company.FullDisplayName,
            CompanyAddress:          companyAddress,
            WorkLocation:            company.WorkLocation ?? "",
            // Neu: Name und Funktion aus UserBranchAccess + User
            SignatoryName:           signatory != null
                                         ? $"{signatory.User.FirstName} {signatory.User.LastName}".Trim()
                                         : "",
            SignatoryTitle:          signatory?.FunctionTitle ?? "",
            SignatureCity:           company.City ?? "",
            ContractDate:            DateTime.Today,
            DefaultVacationWeeks:    company.DefaultVacationWeeks,
            Salutation:              employee.Salutation ?? "",
            FirstName:               employee.FirstName,
            LastName:                employee.LastName,
            DateOfBirth:             employee.DateOfBirth,
            EmployeeStreet:          employee.Street,
            EmployeeZipCity:         !string.IsNullOrWhiteSpace(employee.ZipCode) || !string.IsNullOrWhiteSpace(employee.City)
                                         ? $"{employee.ZipCode} {employee.City}".Trim() : null,
            EmploymentModel:         employment.EmploymentModel,
            SalaryType:              salaryType,
            JobTitle:                jobTitleDisplay,
            ContractType:            employment.ContractType,
            ContractStartDate:       employment.ContractStartDate,
            ContractEndDate:         employment.ContractEndDate,
            ProbationMonths:         employment.ProbationPeriodMonths,
            MonthlySalary:           employment.MonthlySalary,
            MonthlySalaryFte:        employment.MonthlySalaryFte,
            HourlyRate:              employment.HourlyRate,
            EmploymentPercentage:    employment.EmploymentPercentage,
            WeeklyHours:             employment.EmploymentModel == "MTP"
                                         ? (decimal?)company.NormalWeeklyHours : employment.WeeklyHours,
            GuaranteedHoursPerWeek:  employment.GuaranteedHoursPerWeek,
            VacationPercent:         employment.VacationPercent,
            HolidayPercent:          employment.HolidayPercent,
            ThirteenthSalaryPercent: employment.ThirteenthSalaryPercent,
            Gender:                  employee.Gender
        );

        var pdfBytes = new ContractPdfService().Generate(input);
        var fileName = $"Vertrag_{employee.LastName}_{employee.FirstName}_{employment.ContractStartDate:yyyyMMdd}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    [HttpGet("employment/{employmentId}")]
    public async Task<IActionResult> GenerateContractText(int employmentId)
    {
        var employment = await _context.Employments
            .Include(e => e.Employee)
            .FirstOrDefaultAsync(e => e.Id == employmentId);
        if (employment == null) return NotFound("Employment not found.");
        if (employment.CompanyProfileId == null) return BadRequest("No company profile.");

        // Neu: ohne .Include(c => c.Signatories)
        var company = await _context.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == employment.CompanyProfileId.Value);
        if (company == null) return BadRequest("Company profile not found.");

        var employee = employment.Employee;
        if (employee == null) return BadRequest("Employee not found.");

        var checkDate = employment.ContractStartDate;
        var educationHistory = await _context.EmployeeEducationHistories
            .Include(eh => eh.EducationLevel)
            .Where(eh => eh.EmployeeId == employee.Id && eh.IsActive
                      && eh.ValidFrom <= checkDate && (eh.ValidTo == null || eh.ValidTo >= checkDate))
            .OrderByDescending(eh => eh.ValidFrom)
            .FirstOrDefaultAsync();

        string? educationLevelCode = null;
        decimal? minimumWage = null, currentWage = null, difference = null;
        string complianceStatus = "NOT_CHECKED";
        string? warningMessage = null;

        if (educationHistory?.EducationLevel != null && !string.IsNullOrWhiteSpace(employment.JobTitle))
        {
            educationLevelCode = educationHistory.EducationLevel.Code;
            var salaryType2 = employment.SalaryType ?? GetSalaryType(employment.EmploymentModel);
            var rule = await _context.MinimumWageRulesNew
                .Where(r => r.IsActive && r.JobGroupCode == employment.JobTitle
                         && r.EmploymentModelCode == MapEmploymentModel(employment.EmploymentModel)
                         && r.EducationLevelId == educationHistory.EducationLevelId
                         && r.SalaryType == salaryType2
                         && r.ValidFrom <= checkDate && (r.ValidTo == null || r.ValidTo >= checkDate))
                .OrderByDescending(r => r.ValidFrom)
                .FirstOrDefaultAsync();

            if (rule != null)
            {
                minimumWage = rule.Amount;
                currentWage = salaryType2 == "monthly" ? employment.MonthlySalary : employment.HourlyRate;
                if (currentWage != null)
                {
                    difference = currentWage - minimumWage;
                    complianceStatus = difference < 0 ? "UNDERPAID" : "OK";
                    if (difference < 0)
                        warningMessage = $"Lohn zu tief um CHF {Math.Abs(difference.Value):0.00}";
                }
            }
        }

        return Ok(new
        {
            employmentId = employment.Id,
            employee = $"{employee.FirstName} {employee.LastName}",
            company = company.FullDisplayName,
            educationLevelCode, complianceStatus, currentWage, minimumWage, difference, warningMessage
        });
    }

    private static string MapEmploymentModel(string? model) =>
        (model ?? "").ToUpperInvariant() switch
        {
            "UTP" => "PARTTIME", "MTP" => "MTP", "FIX" => "FULLTIME", "FIX-M" => "FIX-M", _ => "PARTTIME"
        };

    private static string GetSalaryType(string? m) =>
        ((m ?? "").ToUpperInvariant() is "FIX" or "FIX-M") ? "monthly" : "hourly";

    private static string GetEmploymentModelText(string? model) =>
        (model ?? "").ToUpperInvariant() switch
        {
            "UTP" => "Stundenlohn Teilzeit (UTP)", "MTP" => "Garantiertes Mindest-Teilzeitpensum (MTP)",
            "FIX" => "Festpensum Vollzeit/Teilzeit (FIX)", "FIX-M" => "Management Vollzeit/Teilzeit (FIX-M)",
            _ => model ?? ""
        };

    private static string GetSalaryTypeText(string? s) =>
        (s ?? "").ToLowerInvariant() switch { "hourly" => "Stundenlohn", "monthly" => "Monatslohn", _ => s ?? "" };

    private static string GetVacationPaymentModeText(string? mode) =>
        (mode ?? "").ToLowerInvariant() switch
        {
            "vacation_account" => "Ferienguthaben wird auf Ferienkonto gebucht",
            "paid_with_salary" => "Ferienentschädigung wird laufend ausbezahlt",
            _ => mode ?? ""
        };
}
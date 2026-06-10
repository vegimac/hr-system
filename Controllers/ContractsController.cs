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
            // Walter-Vorgabe 06.06.2026 (Stufe 1b): Ferien %, Feiertag %, 13. ML %
            // kommen jetzt AUSSCHLIESSLICH aus der Filiale. Vertragsfelder wurden
            // entfernt. Bei Ferien % wird das Alter am Vertragsbeginn geprüft —
            // ist der MA ≥ Filial-Schwelle, erscheint der 6-Wochen-Satz im PDF.
            VacationPercent:         ResolveVacationPctForContract(employee, company, employment.ContractStartDate),
            HolidayPercent:          company.DefaultHolidayPercent,
            ThirteenthSalaryPercent: company.DefaultThirteenthSalaryPercent,
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
            .Include(e => e.JobGroup)   // FK-Code für Mindestlohn-Lookup (Walter 26.05.2026)
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
        // Walter-Architektur: EducationLevelCode kommt vom Vertrag selbst.
        // Falls leer (Alt-Vertrag vor der Migration) → auf alte EmployeeEducationHistory
        // zurückfallen, damit historische PDFs nicht plötzlich „NOT_CHECKED" zeigen.
        string? educationLevelCode = employment.EducationLevelCode;
        int? educationLevelId = null;
        if (!string.IsNullOrWhiteSpace(educationLevelCode))
        {
            educationLevelId = await _context.EducationLevels
                .Where(el => el.Code == educationLevelCode && el.IsActive)
                .Select(el => (int?)el.Id)
                .FirstOrDefaultAsync();
        }
        if (educationLevelId == null)
        {
            var educationHistory = await _context.EmployeeEducationHistories
                .Include(eh => eh.EducationLevel)
                .Where(eh => eh.EmployeeId == employee.Id && eh.IsActive
                          && eh.ValidFrom <= checkDate && (eh.ValidTo == null || eh.ValidTo >= checkDate))
                .OrderByDescending(eh => eh.ValidFrom)
                .FirstOrDefaultAsync();
            if (educationHistory?.EducationLevel != null)
            {
                educationLevelCode ??= educationHistory.EducationLevel.Code;
                educationLevelId    = educationHistory.EducationLevelId;
            }
        }

        decimal? minimumWage = null, currentWage = null, difference = null;
        string complianceStatus = "NOT_CHECKED";
        string? warningMessage = null;

        var empJobCode = employment.JobGroup?.Code;
        if (educationLevelId.HasValue && !string.IsNullOrWhiteSpace(empJobCode))
        {
            var salaryType2 = employment.SalaryType ?? GetSalaryType(employment.EmploymentModel);

            // Alter zum Vertrags-Stichtag (für altersabhängige Regeln, z.B. unter 18)
            int? ageAtCheck = null;
            if (employee.DateOfBirth.HasValue)
            {
                var bd = employee.DateOfBirth.Value;
                int age = checkDate.Year - bd.Year;
                if (checkDate < new DateTime(checkDate.Year, bd.Month, bd.Day)) age--;
                ageAtCheck = age;
            }

            var candidates = await _context.MinimumWageRulesNew
                .Where(r => r.IsActive && r.JobGroupCode == empJobCode
                         && r.EmploymentModelCode == MapEmploymentModel(employment.EmploymentModel)
                         && r.EducationLevelId == educationLevelId
                         && r.SalaryType == salaryType2
                         && r.ValidFrom <= checkDate && (r.ValidTo == null || r.ValidTo >= checkDate)
                         && (r.AgeMax == null
                             || (ageAtCheck != null && ageAtCheck <= r.AgeMax)))
                .OrderBy(r => r.AgeMax == null ? int.MaxValue : r.AgeMax)   // NULLS LAST
                .ThenByDescending(r => r.ValidFrom)
                .ToListAsync();
            var rule = candidates.FirstOrDefault();

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

    // 1:1-Mapping (Mindestlohn-DB nutzt seit der Migration die gleichen Codes wie
    // im Frontend: UTP / MTP / FIX / FIX-M). Legacy-Werte trotzdem unterstützen.
    private static string MapEmploymentModel(string? model) =>
        (model ?? "").ToUpperInvariant() switch
        {
            "UTP" => "UTP", "MTP" => "MTP", "FIX" => "FIX", "FIX-M" => "FIX-M",
            "PARTTIME" => "UTP", "FULLTIME" => "FIX", "FLEX" => "UTP",
            _ => "UTP"
        };

    private static string GetSalaryType(string? m) =>
        ((m ?? "").ToUpperInvariant() is "FIX" or "FIX-M") ? "monthly" : "hourly";

    /// <summary>
    /// Walter-Vorgabe 06.06.2026: Ferien-% für den Arbeitsvertrag-PDF.
    /// Nimmt den Filial-5-Wochen-Default. Ist der MA am Vertragsbeginn bereits
    /// ≥ company.VacationSixWeeksFromAge (L-GAV-Standard 50), wird auf den
    /// 6-Wochen-Default hochgesetzt — analog zur Engine-Logik. Gibt es kein
    /// Geburtsdatum, bleibt's beim 5-Wochen-Default.
    /// </summary>
    private static decimal? ResolveVacationPctForContract(
        Models.Employee employee, Models.CompanyProfile company, DateTime contractStartDate)
    {
        var fiveWeeks = company.DefaultVacationPercent5Weeks;
        var sixWeeks  = company.DefaultVacationPercent6Weeks;
        if (employee.DateOfBirth is null) return fiveWeeks;

        var dob = DateOnly.FromDateTime(employee.DateOfBirth.Value);
        var start = DateOnly.FromDateTime(contractStartDate);
        var schwelle = dob.AddYears(company.VacationSixWeeksFromAge);
        return schwelle <= start ? sixWeeks : fiveWeeks;
    }

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
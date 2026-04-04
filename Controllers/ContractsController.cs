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

    [HttpGet("employment/{employmentId}")]
    public async Task<IActionResult> GenerateContractText(int employmentId)
    {
        var employment = await _context.Employments
            .Include(e => e.Employee)
            .FirstOrDefaultAsync(e => e.Id == employmentId);

        if (employment == null)
            return NotFound("Employment not found.");

        if (employment.CompanyProfileId == null)
            return BadRequest("Employment has no company profile assigned.");

        var company = await _context.CompanyProfiles
            .Include(c => c.Signatories)
            .FirstOrDefaultAsync(c => c.Id == employment.CompanyProfileId.Value);

        if (company == null)
            return BadRequest("Company profile not found.");

        var signatory = company.Signatories
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.LastName)
            .FirstOrDefault();

        var employee = employment.Employee;
        if (employee == null)
            return BadRequest("Employee not found.");

        var checkDate = employment.ContractStartDate;

        var educationHistory = await _context.EmployeeEducationHistories
            .Include(eh => eh.EducationLevel)
            .Where(eh => eh.EmployeeId == employee.Id
                      && eh.IsActive
                      && eh.ValidFrom <= checkDate
                      && (eh.ValidTo == null || eh.ValidTo >= checkDate))
            .OrderByDescending(eh => eh.ValidFrom)
            .FirstOrDefaultAsync();

        string? educationLevelCode = null;
        decimal? minimumWage = null;
        decimal? currentWage = null;
        decimal? difference = null;
        string complianceStatus = "NOT_CHECKED";
        string? warningMessage = null;

        if (educationHistory?.EducationLevel != null && !string.IsNullOrWhiteSpace(employment.JobTitle))
        {
            educationLevelCode = educationHistory.EducationLevel.Code;
            var salaryType = employment.SalaryType ?? GetSalaryType(employment.EmploymentModel);
            var employmentModelCode = MapEmploymentModel(employment.EmploymentModel);

            var rule = await _context.MinimumWageRulesNew
                .Where(r => r.IsActive
                         && r.JobGroupCode == employment.JobTitle
                         && r.EmploymentModelCode == employmentModelCode
                         && r.EducationLevelId == educationHistory.EducationLevelId
                         && r.SalaryType == salaryType
                         && r.ValidFrom <= checkDate
                         && (r.ValidTo == null || r.ValidTo >= checkDate))
                .OrderByDescending(r => r.ValidFrom)
                .FirstOrDefaultAsync();

            if (rule != null)
            {
                minimumWage = rule.Amount;
                currentWage = salaryType == "monthly" ? employment.MonthlySalary : employment.HourlyRate;

                if (currentWage != null)
                {
                    difference = currentWage - minimumWage;
                    complianceStatus = difference < 0 ? "UNDERPAID" : "OK";

                    if (difference < 0)
                    {
                        var lohnText = salaryType == "monthly" ? "Monatslohn" : "Stundenlohn";
                        warningMessage =
                            $"Achtung: Der aktuelle {lohnText} von CHF {currentWage:0.00} liegt CHF {Math.Abs(difference.Value):0.00} unter dem Mindestlohn von CHF {minimumWage:0.00}.";
                    }
                }
            }
        }

        var sb = new StringBuilder();

        sb.AppendLine("ARBEITSVERTRAG");
        sb.AppendLine();
        sb.AppendLine($"Arbeitgeber: {company.CompanyName}");
        sb.AppendLine($"Arbeitsort: {company.WorkLocation}");
        sb.AppendLine($"Adresse: {company.Street} {company.HouseNumber}, {company.ZipCode} {company.City}");
        sb.AppendLine();
        sb.AppendLine($"Arbeitnehmer: {employee.FirstName} {employee.LastName}");
        sb.AppendLine($"Mitarbeiter-Nr.: {employee.EmployeeNumber}");
        sb.AppendLine($"Wohnort: {employee.City}");
        sb.AppendLine();

        sb.AppendLine("1. Anstellungsart");
        sb.AppendLine($"Modell: {GetEmploymentModelText(employment.EmploymentModel)}");
        sb.AppendLine($"Lohnart: {GetSalaryTypeText(employment.SalaryType)}");
        sb.AppendLine();

        sb.AppendLine("2. Funktion");
        sb.AppendLine($"{employment.JobTitle}");
        sb.AppendLine();

        sb.AppendLine("3. Beginn des Arbeitsverhältnisses");
        sb.AppendLine($"{employment.ContractStartDate:dd.MM.yyyy}");
        sb.AppendLine();

        if (employment.ProbationPeriodMonths != null)
        {
            sb.AppendLine("4. Probezeit");
            sb.AppendLine($"{employment.ProbationPeriodMonths} Monat(e)");
            if (employment.ProbationEndDate != null)
                sb.AppendLine($"Probezeit endet am: {employment.ProbationEndDate:dd.MM.yyyy}");
            sb.AppendLine();
        }

        sb.AppendLine("5. Arbeitszeit");
        if (employment.EmploymentModel == "FIX" || employment.EmploymentModel == "FIX-M")
        {
            sb.AppendLine($"Pensum: {employment.EmploymentPercentage}%");
            sb.AppendLine($"Normale Wochenarbeitszeit Betrieb: {company.NormalWeeklyHours} Stunden");
        }
        else if (employment.EmploymentModel == "MTP")
        {
            sb.AppendLine($"Garantierte Mindeststunden pro Woche: {employment.GuaranteedHoursPerWeek}");
        }
        sb.AppendLine();

        sb.AppendLine("6. Lohn");
        if ((employment.SalaryType ?? "").ToLowerInvariant() == "monthly")
        {
            sb.AppendLine($"Monatslohn brutto: CHF {employment.MonthlySalary:0.00}");
        }
        else
        {
            sb.AppendLine($"Stundenlohn brutto: CHF {employment.HourlyRate:0.00}");
            if (employment.VacationPercent != null)
                sb.AppendLine($"Ferienentschädigung: {employment.VacationPercent:0.##}%");
            if (employment.HolidayPercent != null)
                sb.AppendLine($"Feiertagsentschädigung: {employment.HolidayPercent:0.##}%");
            if (employment.ThirteenthSalaryPercent != null)
                sb.AppendLine($"13. Monatslohn: {employment.ThirteenthSalaryPercent:0.##}%");
            if (!string.IsNullOrWhiteSpace(employment.VacationPaymentMode))
                sb.AppendLine($"Ferienbehandlung: {GetVacationPaymentModeText(employment.VacationPaymentMode)}");
        }
        sb.AppendLine();

        sb.AppendLine("7. Ferien");
        if (company.DefaultVacationWeeks != null)
            sb.AppendLine($"{company.DefaultVacationWeeks} Wochen pro Jahr");
        sb.AppendLine();

        sb.AppendLine("8. Unterzeichnung");
        if (signatory != null)
        {
            sb.AppendLine($"{signatory.FirstName} {signatory.LastName}");
            sb.AppendLine($"{signatory.FunctionTitle}");
        }
        else
        {
            sb.AppendLine("Kein Unterzeichner definiert.");
        }

        return Ok(new
        {
            employmentId = employment.Id,
            employee = $"{employee.FirstName} {employee.LastName}",
            company = company.CompanyName,
            educationLevelCode,
            complianceStatus,
            currentWage,
            minimumWage,
            difference,
            warningMessage,
            contractText = sb.ToString()
        });
    }

    // ─── PDF Download ──────────────────────────────────────────────────────────

    [HttpGet("employment/{employmentId}/pdf")]
    public async Task<IActionResult> DownloadContractPdf(int employmentId)
    {
        var employment = await _context.Employments
            .Include(e => e.Employee)
            .FirstOrDefaultAsync(e => e.Id == employmentId);

        if (employment == null)
            return NotFound("Employment not found.");

        if (employment.CompanyProfileId == null)
            return BadRequest("Employment has no company profile assigned.");

        var company = await _context.CompanyProfiles
            .Include(c => c.Signatories)
            .FirstOrDefaultAsync(c => c.Id == employment.CompanyProfileId.Value);

        if (company == null)
            return BadRequest("Company profile not found.");

        var employee = employment.Employee;
        if (employee == null)
            return BadRequest("Employee not found.");

        var signatory = company.Signatories
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.LastName)
            .FirstOrDefault();

        var salaryType = employment.SalaryType ?? GetSalaryType(employment.EmploymentModel);

        var input = new ContractPdfInput(
            CompanyName:             string.IsNullOrWhiteSpace(company.WorkLocation)
                                         ? (company.CompanyName ?? "")
                                         : $"{company.CompanyName} Filiale {company.WorkLocation}",
            CompanyAddress:          $"{company.Street} {company.HouseNumber} {company.ZipCode} {company.City}".Trim(),
            WorkLocation:            company.WorkLocation ?? "",
            SignatoryName:           signatory != null ? $"{signatory.FirstName} {signatory.LastName}" : "",
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
                                         ? $"{employee.ZipCode} {employee.City}".Trim()
                                         : null,
            EmploymentModel:         employment.EmploymentModel,
            SalaryType:              salaryType,
            JobTitle:                employment.JobTitle ?? "",
            ContractType:            employment.ContractType,
            ContractStartDate:       employment.ContractStartDate,
            ContractEndDate:         employment.ContractEndDate,
            ProbationMonths:         employment.ProbationPeriodMonths,
            MonthlySalary:           employment.MonthlySalary,
            HourlyRate:              employment.HourlyRate,
            EmploymentPercentage:    employment.EmploymentPercentage,
            WeeklyHours:             employment.EmploymentModel == "MTP"
                                         ? (decimal?)company.NormalWeeklyHours
                                         : employment.WeeklyHours,
            GuaranteedHoursPerWeek:  employment.GuaranteedHoursPerWeek,
            VacationPercent:         employment.VacationPercent,
            HolidayPercent:          employment.HolidayPercent,
            ThirteenthSalaryPercent: employment.ThirteenthSalaryPercent
        );

        var pdfService = new ContractPdfService();
        var pdfBytes   = pdfService.Generate(input);

        var fileName = $"Vertrag_{employee.LastName}_{employee.FirstName}_{employment.ContractStartDate:yyyyMMdd}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    private static string MapEmploymentModel(string? model)
    {
        return (model ?? "").ToUpperInvariant() switch
        {
            "UTP"   => "PARTTIME",  // bestehende Regeln
            "MTP"   => "MTP",
            "FIX"   => "FULLTIME",  // bestehende Regeln
            "FIX-M" => "FIX-M",     // neue Management-Regeln
            _ => "PARTTIME"
        };
    }

    private static string GetSalaryType(string? employmentModel)
    {
        var m = (employmentModel ?? "").ToUpperInvariant();
        return (m == "FIX" || m == "FIX-M") ? "monthly" : "hourly";
    }

    private static string GetEmploymentModelText(string? model)
    {
        return (model ?? "").ToUpperInvariant() switch
        {
            "UTP"   => "Stundenlohn Teilzeit (UTP)",
            "MTP"   => "Garantiertes Mindest-Teilzeitpensum (MTP)",
            "FIX"   => "Festpensum Vollzeit/Teilzeit (FIX)",
            "FIX-M" => "Management Vollzeit/Teilzeit (FIX-M)",
            _ => model ?? ""
        };
    }

    private static string GetSalaryTypeText(string? salaryType)
    {
        return (salaryType ?? "").ToLowerInvariant() switch
        {
            "hourly" => "Stundenlohn",
            "monthly" => "Monatslohn",
            _ => salaryType ?? ""
        };
    }

    private static string GetVacationPaymentModeText(string? mode)
    {
        return (mode ?? "").ToLowerInvariant() switch
        {
            "vacation_account" => "Ferienguthaben wird auf ein Ferienkonto gebucht",
            "paid_with_salary" => "Ferienentschädigung wird laufend ausbezahlt",
            _ => mode ?? ""
        };
    }
}
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ComplianceController : ControllerBase
{
    private readonly AppDbContext _context;

    public ComplianceController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost("check-live")]
    public async Task<IActionResult> CheckLive([FromBody] ComplianceCheckLiveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.JobGroupCode))
            return BadRequest("JobGroupCode is required.");

        if (string.IsNullOrWhiteSpace(request.EducationLevelCode))
            return BadRequest("EducationLevelCode is required.");

        if (request.EffectiveDate == default)
            return BadRequest("EffectiveDate is required.");

        if (string.IsNullOrWhiteSpace(request.EmploymentModel))
            return BadRequest("EmploymentModel is required.");

        var educationLevel = await _context.EducationLevels
            .FirstOrDefaultAsync(e => e.Code == request.EducationLevelCode && e.IsActive);

        if (educationLevel == null)
            return BadRequest("Education level not found.");

        var employmentModelCode = MapEmploymentModel(request.EmploymentModel);
        var salaryType = GetSalaryType(request.EmploymentModel);

        var rule = await _context.MinimumWageRulesNew
            .Where(r =>
                r.IsActive &&
                r.JobGroupCode == request.JobGroupCode &&
                r.EmploymentModelCode == employmentModelCode &&
                r.EducationLevelId == educationLevel.Id &&
                r.SalaryType == salaryType &&
                r.ValidFrom <= request.EffectiveDate &&
                (r.ValidTo == null || r.ValidTo >= request.EffectiveDate))
            .OrderByDescending(r => r.ValidFrom)
            .FirstOrDefaultAsync();

        if (rule == null)
        {
            return Ok(new
            {
                status = "NO_RULE",
                warningMessage = "Keine Mindestlohnregel gefunden.",
                educationLevelCode = educationLevel.Code,
                jobGroupCode = request.JobGroupCode,
                employmentModelCode,
                salaryType
            });
        }

        if (salaryType == "hourly")
        {
            if (request.HourlyRate == null)
            {
                return Ok(new
                {
                    status = "NOT_CHECKED",
                    warningMessage = "Stundenlohn fehlt.",
                    educationLevelCode = educationLevel.Code,
                    jobGroupCode = request.JobGroupCode,
                    employmentModelCode,
                    salaryType,
                    minimumHourlyRate = rule.Amount
                });
            }

            var difference = request.HourlyRate.Value - rule.Amount;
            var status = difference < 0 ? "UNDERPAID" : "OK";

            string? warningMessage = null;
            if (difference < 0)
            {
                warningMessage =
                    $"Achtung: Der eingegebene Stundenlohn von CHF {request.HourlyRate.Value:0.00} liegt CHF {Math.Abs(difference):0.00} unter dem Mindestlohn von CHF {rule.Amount:0.00}.";
            }

            return Ok(new
            {
                status,
                educationLevelCode = educationLevel.Code,
                jobGroupCode = request.JobGroupCode,
                employmentModelCode,
                salaryType,
                currentHourlyRate = request.HourlyRate,
                minimumHourlyRate = rule.Amount,
                difference,
                warningMessage
            });
        }

        // Pensum-Faktor (100% = 1.0, 80% = 0.8 usw.)
        var pct = (request.EmploymentPercentage ?? 100m) / 100m;
        var minimumFte      = rule.Amount;                              // Mindestlohn 100%
        var minimumEffective = Math.Round(minimumFte * pct, 2);         // Mindestlohn anteilig

        if (request.MonthlySalary == null)
        {
            return Ok(new
            {
                status = "NOT_CHECKED",
                warningMessage = "Monatslohn fehlt.",
                educationLevelCode = educationLevel.Code,
                jobGroupCode = request.JobGroupCode,
                employmentModelCode,
                salaryType,
                minimumMonthlySalary     = minimumEffective,   // anteilig
                minimumMonthlySalaryFte  = minimumFte,         // 100%-Basis
                employmentPercentage     = request.EmploymentPercentage ?? 100m
            });
        }

        // Effektiver Lohn = eingetragener Betrag × Pensum
        var effectiveSalary    = Math.Round(request.MonthlySalary.Value * pct, 2);
        var monthlyDifference  = effectiveSalary - minimumEffective;
        var monthlyStatus      = monthlyDifference < 0 ? "UNDERPAID" : "OK";

        string? monthlyWarningMessage = null;
        if (monthlyDifference < 0)
        {
            monthlyWarningMessage =
                $"Achtung: Der effektive Monatslohn von CHF {effectiveSalary:0.00} ({pct * 100:0}% von CHF {request.MonthlySalary.Value:0.00}) " +
                $"liegt CHF {Math.Abs(monthlyDifference):0.00} unter dem Mindestlohn von CHF {minimumEffective:0.00}.";
        }

        return Ok(new
        {
            status = monthlyStatus,
            educationLevelCode = educationLevel.Code,
            jobGroupCode = request.JobGroupCode,
            employmentModelCode,
            salaryType,
            currentMonthlySalary     = effectiveSalary,        // effektiv (anteilig)
            currentMonthlySalaryFte  = request.MonthlySalary,  // 100%-Eingabe
            minimumMonthlySalary     = minimumEffective,        // Minimum anteilig
            minimumMonthlySalaryFte  = minimumFte,              // Minimum 100%
            employmentPercentage     = request.EmploymentPercentage ?? 100m,
            difference = monthlyDifference,
            warningMessage = monthlyWarningMessage
        });
    }

    private static string MapEmploymentModel(string model)
    {
        return model.ToUpperInvariant() switch
        {
            "UTP"      => "PARTTIME",  // bestehende Regeln nutzen PARTTIME
            "MTP"      => "MTP",
            "FIX"      => "FULLTIME",  // bestehende Regeln nutzen FULLTIME
            "FIX-M"    => "FIX-M",     // neue Management-Regeln
            // Legacy
            "FLEX"     => "PARTTIME",
            "GMTP"     => "MTP",
            "FULLTIME" => "FULLTIME",
            "PARTTIME" => "PARTTIME",
            _          => "PARTTIME"
        };
    }

    private static string GetSalaryType(string employmentModel)
    {
        var m = employmentModel.ToUpperInvariant();
        return (m == "FIX" || m == "FIX-M") ? "monthly" : "hourly";
    }
}
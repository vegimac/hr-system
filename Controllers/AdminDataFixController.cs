using System.Text.RegularExpressions;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Admin-Werkzeuge für Daten-Korrekturen (Walter 03.08.2026).
/// Kein GF-/HR-Pfad — bewusst nur admin.
/// </summary>
[ApiController]
[Route("api/admin/data-fix")]
[Authorize(Roles = "admin")]
public class AdminDataFixController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminDataFixController(AppDbContext db) => _db = db;

    /// <summary>
    /// MA laden + Vorprüfung für eine neue Personalnummer.
    /// Query: employeeId ODER currentNumber (+ optional newNumber).
    /// </summary>
    [HttpGet("employee-number/preview")]
    public async Task<IActionResult> PreviewEmployeeNumber(
        [FromQuery] int? employeeId,
        [FromQuery] string? currentNumber,
        [FromQuery] string? newNumber)
    {
        var emp = await FindEmployeeAsync(employeeId, currentNumber);
        if (emp == null)
            return NotFound(new { error = "NOT_FOUND", message = "Mitarbeiter nicht gefunden." });

        var branch = await GetPrimaryBranchAsync(emp.Id);
        var prefix = NormalizeRestaurantPrefix(branch?.RestaurantCode);
        var cur = NormalizeEmployeeNumber(emp.EmployeeNumber);
        var neu = NormalizeEmployeeNumber(newNumber);

        object? checks = null;
        if (!string.IsNullOrEmpty(neu))
            checks = await BuildChecksAsync(emp.Id, cur, neu, prefix, branch);

        return Ok(new
        {
            employeeId = emp.Id,
            firstName = emp.FirstName,
            lastName = emp.LastName,
            currentNumber = emp.EmployeeNumber,
            easyAtWorkEmployeeId = emp.EasyAtWorkEmployeeId,
            isActive = emp.IsActive,
            branchId = branch?.Id,
            branchName = branch?.BranchName ?? branch?.CompanyName,
            restaurantCode = branch?.RestaurantCode,
            expectedPrefix = prefix,
            hasPostfach = await _db.AppUsers.AnyAsync(u => u.EmployeeId == emp.Id && u.Role == "employee"),
            checks
        });
    }

    /// <summary>
    /// Personalnummer am MA setzen. Kein Alias. easy@work-ID bleibt.
    /// Postfach-Username wird mitgezogen, wenn vorhanden.
    /// </summary>
    [HttpPost("employee-number")]
    public async Task<IActionResult> ChangeEmployeeNumber([FromBody] ChangeEmployeeNumberDto dto)
    {
        if (dto == null || dto.EmployeeId <= 0)
            return BadRequest(new { error = "INVALID", message = "employeeId fehlt." });

        var neu = NormalizeEmployeeNumber(dto.NewNumber);
        if (string.IsNullOrEmpty(neu) || !Regex.IsMatch(neu, @"^\d+$"))
            return BadRequest(new { error = "INVALID_NUMBER", message = "Neue Personalnummer muss nur Ziffern enthalten." });

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == dto.EmployeeId && !e.IsHidden);
        if (emp == null)
            return NotFound(new { error = "NOT_FOUND", message = "Mitarbeiter nicht gefunden." });

        var cur = NormalizeEmployeeNumber(emp.EmployeeNumber);
        if (string.Equals(cur, neu, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "SAME_NUMBER", message = "Neue Nummer ist identisch mit der aktuellen." });

        var branch = await GetPrimaryBranchAsync(emp.Id);
        var prefix = NormalizeRestaurantPrefix(branch?.RestaurantCode);
        var checks = await BuildChecksAsync(emp.Id, cur, neu, prefix, branch);

        if (checks.Taken)
        {
            return Conflict(new
            {
                error = "NUMBER_TAKEN",
                message = $"Personalnummer «{neu}» gehört bereits {checks.TakenByName} (ID {checks.TakenById}).",
                checks
            });
        }

        if (!string.IsNullOrEmpty(prefix) && !neu.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !dto.AllowPrefixMismatch)
        {
            return Conflict(new
            {
                error = "PREFIX_MISMATCH",
                message = $"Nummer «{neu}» passt nicht zum Filial-Präfix «{prefix}» ({branch?.RestaurantCode}). Zum Fortfahren allowPrefixMismatch=true setzen.",
                checks
            });
        }

        // Username-Kollision (anderer AppUser ohne diesen MA)
        var userClash = await _db.AppUsers.AsNoTracking()
            .Where(u => u.Username == neu && u.EmployeeId != emp.Id)
            .Select(u => new { u.Id, u.Username, u.EmployeeId })
            .FirstOrDefaultAsync();
        if (userClash != null)
        {
            return Conflict(new
            {
                error = "USERNAME_TAKEN",
                message = $"Postfach-/Login-Name «{neu}» ist bereits vergeben (AppUser #{userClash.Id}).",
                checks
            });
        }

        emp.EmployeeNumber = neu;

        var postfach = await _db.AppUsers.FirstOrDefaultAsync(u => u.EmployeeId == emp.Id && u.Role == "employee");
        var postfachUpdated = false;
        if (postfach != null && !string.Equals(postfach.Username, neu, StringComparison.OrdinalIgnoreCase))
        {
            postfach.Username = neu;
            postfachUpdated = true;
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            ok = true,
            employeeId = emp.Id,
            firstName = emp.FirstName,
            lastName = emp.LastName,
            oldNumber = cur,
            newNumber = neu,
            easyAtWorkEmployeeId = emp.EasyAtWorkEmployeeId,
            postfachUsernameUpdated = postfachUpdated,
            prefixWarning = checks.PrefixMismatch
                ? $"Präfix «{prefix}» nicht eingehalten — bewusst überschrieben."
                : null,
            message = $"Personalnummer «{cur}» → «{neu}» gesetzt."
                + (postfachUpdated ? " Postfach-Login mitgezogen." : "")
        });
    }

    private async Task<Employee?> FindEmployeeAsync(int? employeeId, string? currentNumber)
    {
        if (employeeId.HasValue && employeeId.Value > 0)
        {
            return await _db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == employeeId.Value && !e.IsHidden);
        }

        var num = NormalizeEmployeeNumber(currentNumber);
        if (string.IsNullOrEmpty(num)) return null;
        return await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => !e.IsHidden && e.EmployeeNumber == num);
    }

    private async Task<CompanyProfile?> GetPrimaryBranchAsync(int employeeId)
    {
        var today = DateTime.Today;
        var cpId = await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == employeeId)
            .OrderByDescending(em => em.IsActive)
            .ThenByDescending(em => em.ContractEndDate == null
                || em.ContractEndDate >= today)
            .ThenByDescending(em => em.ContractStartDate)
            .Select(em => em.CompanyProfileId)
            .FirstOrDefaultAsync();
        if (!cpId.HasValue) return null;
        return await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cpId.Value);
    }

    private async Task<NumberChecks> BuildChecksAsync(
        int employeeId, string currentNumber, string newNumber, string prefix, CompanyProfile? branch)
    {
        var taken = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden && e.Id != employeeId && e.EmployeeNumber == newNumber)
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .FirstOrDefaultAsync();

        var aliasHit = await _db.EmployeeNumberAliases.AsNoTracking()
            .Where(a => a.Number == newNumber && a.EmployeeId != employeeId)
            .Select(a => new { a.EmployeeId })
            .FirstOrDefaultAsync();

        string? aliasName = null;
        if (aliasHit != null)
        {
            aliasName = await _db.Employees.AsNoTracking()
                .Where(e => e.Id == aliasHit.EmployeeId)
                .Select(e => ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim())
                .FirstOrDefaultAsync();
        }

        var prefixMismatch = !string.IsNullOrEmpty(prefix)
            && !newNumber.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        return new NumberChecks
        {
            NewNumber = newNumber,
            CurrentNumber = currentNumber,
            ExpectedPrefix = prefix,
            RestaurantCode = branch?.RestaurantCode,
            PrefixOk = string.IsNullOrEmpty(prefix) || !prefixMismatch,
            PrefixMismatch = prefixMismatch,
            Taken = taken != null,
            TakenById = taken?.Id,
            TakenByName = taken == null ? null : $"{taken.FirstName} {taken.LastName}".Trim(),
            AliasExistsElsewhere = aliasHit != null,
            AliasEmployeeId = aliasHit?.EmployeeId,
            AliasEmployeeName = aliasName,
            CanApply = taken == null
        };
    }

    private static string NormalizeRestaurantPrefix(string? restaurantCode)
    {
        var digits = Regex.Replace(restaurantCode ?? "", @"\D", "");
        digits = digits.TrimStart('0');
        return string.IsNullOrWhiteSpace(digits) ? "" : digits;
    }

    private static string NormalizeEmployeeNumber(string? employeeNumber)
        => Regex.Replace(employeeNumber ?? "", @"\s", "");

    private sealed class NumberChecks
    {
        public string NewNumber { get; set; } = "";
        public string CurrentNumber { get; set; } = "";
        public string? ExpectedPrefix { get; set; }
        public string? RestaurantCode { get; set; }
        public bool PrefixOk { get; set; }
        public bool PrefixMismatch { get; set; }
        public bool Taken { get; set; }
        public int? TakenById { get; set; }
        public string? TakenByName { get; set; }
        public bool AliasExistsElsewhere { get; set; }
        public int? AliasEmployeeId { get; set; }
        public string? AliasEmployeeName { get; set; }
        public bool CanApply { get; set; }
    }
}

public record ChangeEmployeeNumberDto(
    int EmployeeId,
    string NewNumber,
    bool AllowPrefixMismatch = false);

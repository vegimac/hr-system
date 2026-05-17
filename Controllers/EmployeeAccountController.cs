using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Verwaltung des Postfach-Login-Zugangs pro Mitarbeiter — Anzeige Status,
/// Passwort-Reset (Initial-Passwort), Lockout aufheben.
///
/// Walter-Anforderung: keine separate Mitarbeiter-User-Verwaltung. Postfach-
/// Accounts werden ausschliesslich im MA-Detail über diesen Controller
/// gepflegt; sie tauchen NICHT in /api/users auf (dort werden sie über
/// employee_id IS NULL gefiltert).
///
/// Berechtigungen: alle Backoffice-Rollen (admin, superuser, user) — die
/// UI zeigt die Buttons nur GF/HR/Admin. Bei Bedarf kann später ein feinerer
/// Branch-Access-Filter eingebaut werden.
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/postfach-account")]
[Authorize]
public class EmployeeAccountController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EmployeePostfachService _postfach;

    public EmployeeAccountController(AppDbContext db, EmployeePostfachService postfach)
    {
        _db = db;
        _postfach = postfach;
    }

    /// <summary>
    /// GET /api/employees/{id}/postfach-account
    /// Liefert den Status des Login-Zugangs des MA (existiert? aktiv? gesperrt?).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetStatus(int employeeId)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound();

        var status = await _postfach.GetStatusAsync(employeeId);
        return Ok(new
        {
            employeeId       = emp.Id,
            employeeNumber   = emp.EmployeeNumber,
            employeeIsActive = emp.IsActive,
            status.Exists,
            status.IsActive,
            status.Locked,
            status.LockedUntil,
            status.LastLoginAt,
            status.FailedLoginCount
        });
    }

    /// <summary>
    /// POST /api/employees/{id}/postfach-account/reset-password
    /// Setzt das Passwort auf das Initial-Passwort zurück und erzwingt
    /// einen Wechsel beim nächsten Login. Gibt das Klartext-Passwort
    /// einmalig zurück, damit der Aufrufer (GF/HR/Admin) es dem MA
    /// aushändigen kann.
    /// </summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(int employeeId)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound();

        var primary = await _postfach.GetPrimaryCompanyAsync(employeeId);
        var newPw   = await _postfach.ResetPasswordAsync(emp, primary);

        return Ok(new
        {
            message         = "Passwort wurde zurückgesetzt. Der MA muss es beim nächsten Login wechseln.",
            initialPassword = newPw,
            username        = emp.EmployeeNumber
        });
    }

    /// <summary>
    /// POST /api/employees/{id}/postfach-account/unlock
    /// Hebt eine 5-Fehlversuche-Sperre auf (FailedLoginCount=0, LockedUntil=NULL).
    /// </summary>
    [HttpPost("unlock")]
    public async Task<IActionResult> Unlock(int employeeId)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound();

        await _postfach.UnlockAsync(emp);
        return Ok(new { message = "Sperre wurde aufgehoben." });
    }

    /// <summary>
    /// POST /api/employees/{id}/postfach-account/sync
    /// Synchronisiert den Aktiv-Status des AppUsers mit dem MA-Status
    /// (idempotent; meist nicht nötig, da bei jedem Update eh aufgerufen).
    /// </summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(int employeeId)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound();

        await _postfach.SyncActiveStateAsync(emp);
        return Ok(new { message = "Status synchronisiert." });
    }
}

/// <summary>
/// Bulk-Endpoint: einmaliger Backfill für alle bestehenden aktiven MA.
/// Idempotent — überspringt MA, die schon einen Account haben. Wird nach
/// dem Deploy von Phase 1 einmalig vom Admin ausgeführt.
/// </summary>
[ApiController]
[Route("api/admin/postfach-backfill")]
[Authorize(Roles = "admin")]
public class PostfachBackfillController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EmployeePostfachService _postfach;

    public PostfachBackfillController(AppDbContext db, EmployeePostfachService postfach)
    {
        _db = db;
        _postfach = postfach;
    }

    /// <summary>
    /// POST /api/admin/postfach-backfill
    /// Erzeugt für alle aktiven MA, die noch keinen Account haben, einen
    /// neuen mit Initial-Passwort. Gibt eine Zusammenfassung zurück.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Run()
    {
        var aktiveMaOhneAccount = await (
            from e in _db.Employees
            where e.IsActive
               && !_db.AppUsers.Any(u => u.EmployeeId == e.Id)
            select e
        ).ToListAsync();

        int created = 0;
        int skipped = 0;
        var errors  = new List<string>();
        foreach (var emp in aktiveMaOhneAccount)
        {
            try
            {
                var primary = await _postfach.GetPrimaryCompanyAsync(emp.Id);
                var pw      = await _postfach.EnsureAccountAsync(emp, primary);
                if (pw != null) created++;
                else skipped++;
            }
            catch (Exception ex)
            {
                errors.Add($"{emp.EmployeeNumber} {emp.FirstName} {emp.LastName}: {ex.Message}");
            }
        }
        return Ok(new
        {
            scanned = aktiveMaOhneAccount.Count,
            created,
            skipped,
            errors
        });
    }
}

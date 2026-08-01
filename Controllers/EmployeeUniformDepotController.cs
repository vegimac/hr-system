using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Uniformen-Depot pro MA (Walter Aug 2026). Lesen + manuelle Rückgabe-
/// Entscheidung + Admin-Backfill. Kein Lohn-EditLock (Depot-Stammdaten;
/// Abzug/Refund läuft über Lohnlauf).
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/uniform-depot")]
public class EmployeeUniformDepotController : ControllerBase
{
    private readonly AppDbContext        _db;
    private readonly UniformDepotService _depot;

    public EmployeeUniformDepotController(AppDbContext db, UniformDepotService depot)
    {
        _db    = db;
        _depot = depot;
    }

    [HttpGet]
    public async Task<IActionResult> Get(int employeeId)
    {
        var exists = await _db.Employees.AnyAsync(e => e.Id == employeeId);
        if (!exists) return NotFound();
        var dto = await _depot.GetDtoAsync(employeeId);
        return Ok(dto ?? new {
            employeeId,
            balance = 0m,
            status = (string?)null,
            chargedPeriode = (string?)null,
            refundPeriode = (string?)null,
            returnConfirmed = (bool?)null,
            bemerkung = (string?)null,
        });
    }

    /// <summary>
    /// Rückgabe-Entscheidung nachträglich setzen (z.B. wenn Austritt schon
    /// erfasst war). Body: { returned: true|false }.
    /// </summary>
    [HttpPut("return")]
    [Authorize(Roles = "admin,superuser,user")]
    public async Task<IActionResult> SetReturn(int employeeId, [FromBody] UniformReturnDto dto)
    {
        if (dto is null) return BadRequest();
        var exists = await _db.Employees.AnyAsync(e => e.Id == employeeId);
        if (!exists) return NotFound();

        var uidStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int? uid = int.TryParse(uidStr, out var u) ? u : null;
        await _depot.SetReturnDecisionAsync(employeeId, dto.Returned, uid);
        return Ok(await _depot.GetDtoAsync(employeeId));
    }
}

public class UniformReturnDto
{
    public bool Returned { get; set; }
}

/// <summary>Admin: Backfill Eintritt vor 01.07.2026 erneut anstoßen.</summary>
[ApiController]
[Route("api/uniform-depot")]
public class UniformDepotAdminController : ControllerBase
{
    private readonly UniformDepotService _depot;
    public UniformDepotAdminController(UniformDepotService depot) => _depot = depot;

    [HttpPost("backfill")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Backfill()
    {
        var n = await _depot.BackfillAsync();
        return Ok(new { created = n, message = $"Backfill: {n} Depots angelegt." });
    }
}

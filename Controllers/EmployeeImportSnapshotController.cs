using HrSystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmployeeImportSnapshotController : ControllerBase
{
    private readonly AppDbContext _context;

    public EmployeeImportSnapshotController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("latest/{employeeId:int}")]
    public async Task<IActionResult> GetLatest(int employeeId)
    {
        var snapshot = await _context.EmployeeImportSnapshots
            .Where(x => x.EmployeeId == employeeId && x.IsActive)
            .OrderByDescending(x => x.ImportedAt)
            .FirstOrDefaultAsync();

        if (snapshot == null)
            return NotFound();

        return Ok(snapshot);
    }
}
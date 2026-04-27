using HrSystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/permittypes")]
public class PermitTypesController : ControllerBase
{
    private readonly AppDbContext _context;

    public PermitTypesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var types = await _context.PermitTypes
            .Where(p => p.IsActive)
            .OrderBy(p => p.Code)
            .ToListAsync();

        return Ok(types);
    }
}
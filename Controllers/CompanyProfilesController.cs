using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompanyProfilesController : ControllerBase
{
    private readonly AppDbContext _context;

    public CompanyProfilesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var profiles = await _context.CompanyProfiles
            .Include(c => c.Signatories)
            .ToListAsync();

        return Ok(profiles);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var profile = await _context.CompanyProfiles
            .Include(c => c.Signatories)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (profile == null)
            return NotFound();

        return Ok(profile);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CompanyProfile profile)
    {
        _context.CompanyProfiles.Add(profile);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, profile);
    }
}
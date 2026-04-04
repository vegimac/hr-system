using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompanySignatoriesController : ControllerBase
{
    private readonly AppDbContext _context;

    public CompanySignatoriesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var signatories = await _context.CompanySignatories
            .OrderBy(s => s.LastName)
            .ThenBy(s => s.FirstName)
            .ToListAsync();

        return Ok(signatories);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CompanySignatory signatory)
    {
        var companyExists = await _context.CompanyProfiles
            .AnyAsync(c => c.Id == signatory.CompanyProfileId);

        if (!companyExists)
            return BadRequest($"CompanyProfile with id {signatory.CompanyProfileId} does not exist.");

        _context.CompanySignatories.Add(signatory);
        await _context.SaveChangesAsync();

        return Ok(signatory);
    }
}
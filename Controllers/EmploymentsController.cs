using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmploymentsController : ControllerBase
{
    private readonly AppDbContext _context;

    public EmploymentsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var employments = await _context.Employments
            .OrderBy(e => e.EmployeeId)
            .ThenBy(e => e.ContractStartDate)
            .ToListAsync();

        return Ok(employments);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var employment = await _context.Employments
            .FirstOrDefaultAsync(e => e.Id == id);

        if (employment == null)
            return NotFound();

        return Ok(employment);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Employment employment)
    {
        var employeeExists = await _context.Employees
            .AnyAsync(e => e.Id == employment.EmployeeId);

        if (!employeeExists)
            return BadRequest($"Employee with id {employment.EmployeeId} does not exist.");

        _context.Employments.Add(employment);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = employment.Id }, employment);
    }
}
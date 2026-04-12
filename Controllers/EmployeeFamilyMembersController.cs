using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/employees/{employeeId:int}/family")]
public class EmployeeFamilyMembersController : ControllerBase
{
    private readonly AppDbContext _context;

    public EmployeeFamilyMembersController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/employees/{employeeId}/family
    [HttpGet]
    public async Task<IActionResult> GetByEmployee(int employeeId)
    {
        var members = await _context.EmployeeFamilyMembers
            .Where(m => m.EmployeeId == employeeId)
            .OrderBy(m => m.MemberType)
            .ThenBy(m => m.DateOfBirth)
            .ToListAsync();

        return Ok(members);
    }

    // GET /api/employees/{employeeId}/family/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int employeeId, int id)
    {
        var member = await _context.EmployeeFamilyMembers
            .FirstOrDefaultAsync(m => m.Id == id && m.EmployeeId == employeeId);

        if (member == null) return NotFound();
        return Ok(member);
    }

    // POST /api/employees/{employeeId}/family
    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, EmployeeFamilyMember member)
    {
        member.EmployeeId = employeeId;
        member.CreatedAt = DateTime.UtcNow;
        member.UpdatedAt = DateTime.UtcNow;

        _context.EmployeeFamilyMembers.Add(member);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { employeeId, id = member.Id }, member);
    }

    // PUT /api/employees/{employeeId}/family/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int employeeId, int id, EmployeeFamilyMember member)
    {
        var existing = await _context.EmployeeFamilyMembers
            .FirstOrDefaultAsync(m => m.Id == id && m.EmployeeId == employeeId);

        if (existing == null) return NotFound();

        existing.MemberType           = member.MemberType;
        existing.Gender               = member.Gender;
        existing.FamilyStatus         = member.FamilyStatus;
        existing.LastName             = member.LastName;
        existing.MaidenName           = member.MaidenName;
        existing.FirstName            = member.FirstName;
        existing.SocialSecurityNumber = member.SocialSecurityNumber;
        existing.LivesInSwitzerland   = member.LivesInSwitzerland;
        existing.DateOfBirth          = member.DateOfBirth;
        existing.DateOfDeath          = member.DateOfDeath;
        existing.Allowance1Until      = member.Allowance1Until;
        existing.Allowance2Until      = member.Allowance2Until;
        existing.Allowance3Until      = member.Allowance3Until;
        existing.QstDeductibleFrom    = member.QstDeductibleFrom;
        existing.QstDeductibleUntil   = member.QstDeductibleUntil;
        existing.UpdatedAt            = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(existing);
    }

    // DELETE /api/employees/{employeeId}/family/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int employeeId, int id)
    {
        var member = await _context.EmployeeFamilyMembers
            .FirstOrDefaultAsync(m => m.Id == id && m.EmployeeId == employeeId);

        if (member == null) return NotFound();

        _context.EmployeeFamilyMembers.Remove(member);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}

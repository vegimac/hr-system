using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/userbranch")]
public class UserBranchController : ControllerBase
{
    private readonly AppDbContext _context;

    public UserBranchController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/userbranch/company/{companyId} – alle Benutzer einer Filiale
    [HttpGet("company/{companyId:int}")]
    public async Task<IActionResult> GetByCompany(int companyId)
    {
        var list = await _context.UserBranchAccesses
            .Include(uba => uba.User)
            .Where(uba => uba.CompanyProfileId == companyId && uba.User.IsActive)
            .OrderBy(uba => uba.User.LastName)
            .ThenBy(uba => uba.User.FirstName)
            .Select(uba => new
            {
                uba.Id,
                uba.UserId,
                uba.CompanyProfileId,
                uba.Role,
                uba.FunctionTitle,
                uba.IsDefault,
                User = new
                {
                    uba.User.Id,
                    uba.User.Username,
                    uba.User.FirstName,
                    uba.User.LastName,
                    uba.User.Email,
                    uba.User.Phone,
                }
            })
            .ToListAsync();

        return Ok(list);
    }

    // GET /api/userbranch/company/{companyId}/role/{role}
    [HttpGet("company/{companyId:int}/role/{role}")]
    public async Task<IActionResult> GetByRole(int companyId, string role)
    {
        var list = await _context.UserBranchAccesses
            .Include(uba => uba.User)
            .Where(uba => uba.CompanyProfileId == companyId && uba.User.IsActive)
            .ToListAsync();

        var match = list.FirstOrDefault(uba =>
                        string.Equals(uba.Role, role, StringComparison.OrdinalIgnoreCase))
                 ?? list.FirstOrDefault(uba => uba.IsDefault)
                 ?? list.FirstOrDefault();

        if (match is null) return NotFound();
        return Ok(new
        {
            match.Id,
            match.UserId,
            match.Role,
            match.FunctionTitle,
            match.IsDefault,
            User = new
            {
                match.User.Id,
                match.User.Username,
                match.User.FirstName,
                match.User.LastName,
                match.User.Email,
                match.User.Phone,
            }
        });
    }

    public record UbaRequest(int UserId, int CompanyProfileId, string? Role, string? FunctionTitle, bool IsDefault);

    // POST /api/userbranch
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UbaRequest req)
    {
        // Prüfen ob bereits vorhanden
        var existing = await _context.UserBranchAccesses
            .FirstOrDefaultAsync(uba => uba.UserId == req.UserId && uba.CompanyProfileId == req.CompanyProfileId);

        if (existing != null)
        {
            // Aktualisieren statt doppelt anlegen
            existing.Role          = req.Role;
            existing.FunctionTitle = req.FunctionTitle;
            existing.IsDefault     = req.IsDefault;
            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        var uba = new UserBranchAccess
        {
            UserId           = req.UserId,
            CompanyProfileId = req.CompanyProfileId,
            Role             = req.Role,
            FunctionTitle    = req.FunctionTitle,
            IsDefault        = req.IsDefault,
        };
        _context.UserBranchAccesses.Add(uba);
        await _context.SaveChangesAsync();
        return Ok(uba);
    }

    // PUT /api/userbranch/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UbaRequest req)
    {
        var uba = await _context.UserBranchAccesses.FindAsync(id);
        if (uba is null) return NotFound();

        uba.Role          = req.Role;
        uba.FunctionTitle = req.FunctionTitle;
        uba.IsDefault     = req.IsDefault;
        await _context.SaveChangesAsync();
        return Ok(uba);
    }

    // DELETE /api/userbranch/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var uba = await _context.UserBranchAccesses.FindAsync(id);
        if (uba is null) return NotFound();
        _context.UserBranchAccesses.Remove(uba);
        await _context.SaveChangesAsync();
        return Ok();
    }
}

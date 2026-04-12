using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;

    public UsersController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/users – nur admin/superuser
    [HttpGet]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> GetAll()
    {
        var users = await _context.AppUsers
            .Include(u => u.BranchAccess)
            .ThenInclude(ba => ba.CompanyProfile)
            .OrderBy(u => u.Username)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.Email,
                u.Role,
                u.IsActive,
                u.CreatedAt,
                branches = u.BranchAccess.Select(ba => new
                {
                    id = ba.CompanyProfileId,
                    name = ba.CompanyProfile.BranchName ?? ba.CompanyProfile.CompanyName,
                    code = ba.CompanyProfile.RestaurantCode
                })
            })
            .ToListAsync();

        return Ok(users);
    }

    public record CreateUserRequest(string Username, string Email, string Password, string Role, List<int> BranchIds);
    public record UpdateUserRequest(string Username, string Email, string? Password, string Role, bool IsActive, List<int> BranchIds);

    // POST /api/users – nur admin/superuser
    [HttpPost]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;

        // superuser darf keine admins erstellen
        if (callerRole == "superuser" && req.Role == "admin")
            return Forbid();

        if (await _context.AppUsers.AnyAsync(u => u.Email == req.Email))
            return BadRequest(new { message = "Diese E-Mail ist bereits vergeben." });

        var user = new AppUser
        {
            Username = req.Username,
            Email = req.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = req.Role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.AppUsers.Add(user);
        await _context.SaveChangesAsync();

        // Filialen-Zuweisungen
        foreach (var branchId in req.BranchIds)
        {
            _context.UserBranchAccesses.Add(new UserBranchAccess
            {
                UserId = user.Id,
                CompanyProfileId = branchId
            });
        }
        await _context.SaveChangesAsync();

        return Ok(new { user.Id, user.Username, user.Email, user.Role });
    }

    // PUT /api/users/{id}
    [HttpPut("{id}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest req)
    {
        var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var callerId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var user = await _context.AppUsers
            .Include(u => u.BranchAccess)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();

        // superuser darf keine admins bearbeiten
        if (callerRole == "superuser" && (user.Role == "admin" || req.Role == "admin"))
            return Forbid();

        // admin darf sich nicht selbst deaktivieren
        if (callerId == id && !req.IsActive)
            return BadRequest(new { message = "Sie können sich nicht selbst deaktivieren." });

        user.Username = req.Username;
        user.Email = req.Email;
        user.Role = req.Role;
        user.IsActive = req.IsActive;

        if (!string.IsNullOrWhiteSpace(req.Password))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);

        // Filialen-Zuweisungen neu setzen
        _context.UserBranchAccesses.RemoveRange(user.BranchAccess);
        foreach (var branchId in req.BranchIds)
        {
            _context.UserBranchAccesses.Add(new UserBranchAccess
            {
                UserId = user.Id,
                CompanyProfileId = branchId
            });
        }

        await _context.SaveChangesAsync();
        return Ok(new { user.Id, user.Username, user.Email, user.Role, user.IsActive });
    }

    // DELETE /api/users/{id} – nur admin
    [HttpDelete("{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var callerId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        if (callerId == id)
            return BadRequest(new { message = "Sie können sich nicht selbst löschen." });

        var user = await _context.AppUsers.FindAsync(id);
        if (user == null) return NotFound();

        _context.AppUsers.Remove(user);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}

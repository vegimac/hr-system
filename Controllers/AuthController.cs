using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;

    public AuthController(AppDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    public record LoginRequest(string Email, string Password);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _context.AppUsers
            .Include(u => u.BranchAccess)
            .ThenInclude(ba => ba.CompanyProfile)
            .FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive);

        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "E-Mail oder Passwort falsch." });

        var token = GenerateToken(user);

        return Ok(new
        {
            token,
            user = new
            {
                user.Id,
                user.Username,
                user.Email,
                user.Role,
                branches = user.Role == "admin" || user.Role == "superuser"
                    ? (object)"all"
                    : user.BranchAccess.Select(ba => new
                    {
                        id = ba.CompanyProfileId,
                        name = ba.CompanyProfile.BranchName ?? ba.CompanyProfile.CompanyName,
                        code = ba.CompanyProfile.RestaurantCode
                    })
            }
        });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _context.AppUsers
            .Include(u => u.BranchAccess)
            .ThenInclude(ba => ba.CompanyProfile)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return NotFound();

        return Ok(new
        {
            user.Id,
            user.Username,
            user.Email,
            user.Role,
            branches = user.Role == "admin" || user.Role == "superuser"
                ? (object)"all"
                : user.BranchAccess.Select(ba => new
                {
                    id = ba.CompanyProfileId,
                    name = ba.CompanyProfile.BranchName ?? ba.CompanyProfile.CompanyName,
                    code = ba.CompanyProfile.RestaurantCode
                })
        });
    }

    private string GenerateToken(AppUser user)
    {
        var secret = _config["Jwt:Secret"] ?? "SchaUbHrSyStEmSeCrEtKeY2026!!SuperSecure";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

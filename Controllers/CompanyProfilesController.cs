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

    // PATCH /api/companyprofiles/{id}/nighthours
    [HttpPatch("{id:int}/nighthours")]
    public async Task<IActionResult> UpdateNightHours(int id, [FromBody] NightHoursDto dto)
    {
        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.NightStartTime = dto.NightStartTime;
        profile.NightEndTime   = dto.NightEndTime;
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record NightHoursDto(string NightStartTime, string NightEndTime);

    // PATCH /api/companyprofiles/{id}/alv
    [HttpPatch("{id:int}/alv")]
    public async Task<IActionResult> UpdateAlvDaten(int id, [FromBody] AlvDatenDto dto)
    {
        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.BurNummer      = dto.BurNummer;
        profile.BranchenCode   = dto.BranchenCode;
        profile.AhvKasse       = dto.AhvKasse;
        profile.BvgVersicherer = dto.BvgVersicherer;
        profile.IstGav         = dto.IstGav;
        profile.GavName        = dto.GavName;
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record AlvDatenDto(
        string? BurNummer,
        string? BranchenCode,
        string? AhvKasse,
        string? BvgVersicherer,
        bool    IstGav,
        string? GavName
    );
}
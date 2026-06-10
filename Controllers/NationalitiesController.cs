using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NationalitiesController : ControllerBase
{
    private readonly AppDbContext _context;

    public NationalitiesController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/nationalities?lang=de
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string lang = "de")
    {
        var nationalities = await _context.Nationalities
            .Where(n => n.IsActive)
            .OrderBy(n => n.Code)
            .ToListAsync();

        var texts = await _context.AppTexts
            .Where(t => t.Module == "NATIONALITY" && t.LanguageCode == lang)
            .ToListAsync();

        var result = nationalities.Select(n =>
        {
            var key = $"{n.Code}.NAME";

            var text = texts
                .FirstOrDefault(t => t.TextKey == key);

            return new
            {
                id = n.Id,
                code = n.Code,
                // Volltext (Walter-Vorgabe 14.05.2026): AppText → statische
                // ISO-Tabelle → Code als allerletzter Ausweg.
                name = text?.Content ?? CountryNamesDe.Resolve(n.Code) ?? n.Code
            };
        });

        return Ok(result);
    }

    // GET /api/nationalities/lookup?lang=de
    [HttpGet("lookup")]
    public async Task<IActionResult> GetLookup([FromQuery] string lang = "de")
    {
        var nationalities = await _context.Nationalities
            .Where(n => n.IsActive)
            .OrderBy(n => n.Code)
            .ToListAsync();

        var texts = await _context.AppTexts
            .Where(t => t.Module == "NATIONALITY" && t.LanguageCode == lang)
            .ToListAsync();

        var result = nationalities.Select(n =>
        {
            var key = $"{n.Code}.NAME";

            var text = texts
                .FirstOrDefault(t => t.TextKey == key);

            return new
            {
                id = n.Id,
                displayName = text?.Content ?? CountryNamesDe.Resolve(n.Code) ?? n.Code
            };
        });

        return Ok(result);
    }

    // ════════════════════════════════════════════════════════════════════
    // ADMIN-PFLEGE (Walter-Vorgabe 07.06.2026)
    // Liste mit Code2 + IsActive, sowie PATCH-Endpoint für Korrekturen.
    // Code (kanonischer ISO-Code) bleibt unveränderbar — wer eine neue Nation
    // braucht, fügt sie via SQL hinzu (seltener Fall).
    // ════════════════════════════════════════════════════════════════════
    [HttpGet("admin")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> GetAllForAdmin([FromQuery] string lang = "de")
    {
        var nationalities = await _context.Nationalities
            .OrderBy(n => n.Code)
            .ToListAsync();

        var texts = await _context.AppTexts
            .Where(t => t.Module == "NATIONALITY" && t.LanguageCode == lang)
            .ToListAsync();

        var result = nationalities.Select(n => new
        {
            id       = n.Id,
            code     = n.Code,
            code2    = n.Code2,
            isActive = n.IsActive,
            name     = texts.FirstOrDefault(t => t.TextKey == $"{n.Code}.NAME")?.Content
                       ?? CountryNamesDe.Resolve(n.Code) ?? n.Code
        });

        return Ok(result);
    }

    public class NationalityUpdateDto
    {
        public string? Code2 { get; set; }     // null = unverändert, "" = leeren
        public bool? IsActive { get; set; }
    }

    [HttpPatch("{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateAdmin(int id, [FromBody] NationalityUpdateDto dto)
    {
        var n = await _context.Nationalities.FirstOrDefaultAsync(x => x.Id == id);
        if (n == null) return NotFound();

        if (dto.Code2 != null)
        {
            var trimmed = dto.Code2.Trim().ToUpperInvariant();
            n.Code2 = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }
        if (dto.IsActive.HasValue) n.IsActive = dto.IsActive.Value;

        await _context.SaveChangesAsync();
        return Ok(new { id = n.Id, code = n.Code, code2 = n.Code2, isActive = n.IsActive });
    }

    // GET /api/nationalities/1
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, [FromQuery] string lang = "de")
    {
        var nationality = await _context.Nationalities
            .FirstOrDefaultAsync(n => n.Id == id);

        if (nationality == null)
            return NotFound();

        var key = $"{nationality.Code}.NAME";

        var text = await _context.AppTexts
            .FirstOrDefaultAsync(t =>
                t.Module == "NATIONALITY" &&
                t.TextKey == key &&
                t.LanguageCode == lang);

        return Ok(new
        {
            id = nationality.Id,
            code = nationality.Code,
            name = text?.Content ?? CountryNamesDe.Resolve(nationality.Code) ?? nationality.Code
        });
    }
}
using HrSystem.Data;
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
                name = text?.Content ?? n.Code // fallback
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
                displayName = text?.Content ?? n.Code
            };
        });

        return Ok(result);
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
            name = text?.Content ?? nationality.Code
        });
    }
}
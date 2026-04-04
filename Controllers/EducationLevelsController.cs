using HrSystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EducationLevelsController : ControllerBase
{
    private readonly AppDbContext _context;

    public EducationLevelsController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/educationlevels?lang=de
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string lang = "de")
    {
        var educationLevels = await _context.EducationLevels
            .Where(e => e.IsActive)
            .OrderBy(e => e.Code)
            .ToListAsync();

        var texts = await _context.AppTexts
            .Where(t => t.IsActive
                     && t.Module == "EDUCATION"
                     && t.LanguageCode == lang)
            .ToListAsync();

        var result = educationLevels.Select(e =>
        {
            var key = $"{e.Code}.NAME";
            var text = texts.FirstOrDefault(t => t.TextKey == key);

            return new
            {
                id = e.Id,
                code = e.Code,
                name = text?.Content ?? e.Code
            };
        });

        return Ok(result);
    }
}
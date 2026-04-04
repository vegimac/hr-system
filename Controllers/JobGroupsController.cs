using HrSystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobGroupsController : ControllerBase
{
    private readonly AppDbContext _context;

    public JobGroupsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string lang = "de")
    {
        var jobGroups = await _context.JobGroups
            .Where(j => j.IsActive)
            .OrderBy(j => j.SortOrder)
            .ToListAsync();

        var texts = await _context.AppTexts
            .Where(t => t.IsActive
                     && t.Module == "JOB_GROUP"
                     && t.LanguageCode == lang)
            .ToListAsync();

        var result = jobGroups.Select(j =>
        {
            var textKey = $"{j.Code}.NAME";
            var text = texts.FirstOrDefault(t => t.TextKey == textKey);

            return new
            {
                j.Id,
                j.Code,
                DisplayName = text?.Content ?? j.Code
            };
        });

        return Ok(result);
    }
}
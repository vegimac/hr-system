using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ContractTextsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ContractTextsController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/contracttexts?lang=de&contractType=MTP
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string lang = "de",
        [FromQuery] string? contractType = null)
    {
        var query = _context.ContractTexts
            .Where(t => t.LanguageCode == lang && t.IsActive);

        if (!string.IsNullOrWhiteSpace(contractType))
            query = query.Where(t => t.ContractTypes.Contains(contractType)
                                  || t.ContractTypes == "ALL");

        var texts = await query.OrderBy(t => t.TextKey).ToListAsync();
        return Ok(texts);
    }

    // GET /api/contracttexts/15?lang=de
    [HttpGet("{textKey}")]
    public async Task<IActionResult> GetByKey(
        string textKey,
        [FromQuery] string lang = "de")
    {
        var now = DateTime.Today;
        var text = await _context.ContractTexts
            .Where(t => t.TextKey == textKey
                     && t.LanguageCode == lang
                     && t.IsActive
                     && t.ValidFrom <= now
                     && (t.ValidTo == null || t.ValidTo >= now))
            .OrderByDescending(t => t.ValidFrom)
            .FirstOrDefaultAsync();

        if (text == null) return NotFound();
        return Ok(text);
    }

    // PUT /api/contracttexts/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ContractText updated)
    {
        var existing = await _context.ContractTexts.FindAsync(id);
        if (existing == null) return NotFound();

        existing.Content       = updated.Content;
        existing.ContractTypes = updated.ContractTypes;
        existing.IsActive      = updated.IsActive;
        existing.ValidTo       = updated.ValidTo;

        await _context.SaveChangesAsync();
        return Ok(existing);
    }

    // POST /api/contracttexts
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ContractText text)
    {
        _context.ContractTexts.Add(text);
        await _context.SaveChangesAsync();
        return Ok(text);
    }

    // POST /api/contracttexts/bulk  – Array von ContractText
    [HttpPost("bulk")]
    public async Task<IActionResult> BulkCreate([FromBody] List<ContractText> texts)
    {
        int added = 0;
        foreach (var t in texts)
        {
            var exists = await _context.ContractTexts.AnyAsync(x =>
                x.TextKey == t.TextKey &&
                x.LanguageCode == t.LanguageCode &&
                x.ContractTypes == t.ContractTypes);

            if (!exists)
            {
                _context.ContractTexts.Add(t);
                added++;
            }
        }
        await _context.SaveChangesAsync();
        return Ok(new { added, total = texts.Count });
    }

    // GET /api/contracttexts/languages
    [HttpGet("languages")]
    public async Task<IActionResult> GetLanguages()
    {
        var langs = await _context.ContractTexts
            .Where(t => t.IsActive)
            .Select(t => t.LanguageCode)
            .Distinct()
            .ToListAsync();
        return Ok(langs);
    }
}

using HrSystem.Data;
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
        // Walter-Vorgabe 13.06.2026: nur DB-Quelle (`nationality.name_de`).
        // Wenn ein Eintrag fehlt → in den Systemeinstellungen ergänzen, keine
        // hardgecodete Fallback-Tabelle mehr. Code als allerletzter Ausweg,
        // damit die UI bei einem leeren `name_de` nicht ganz kaputt geht.
        var nationalities = await _context.Nationalities
            .Where(n => n.IsActive)
            .ToListAsync();

        // Alphabetisch nach NAME (de-CH-Culture für korrekte Umlaut-Sortierung).
        var result = nationalities
            .Select(n => new
            {
                id   = n.Id,
                code = n.Code,
                name = string.IsNullOrWhiteSpace(n.NameDe) ? n.Code : n.NameDe
            })
            .OrderBy(x => x.name, StringComparer.Create(new System.Globalization.CultureInfo("de-CH"), ignoreCase: true))
            .ToList();

        return Ok(result);
    }

    // GET /api/nationalities/lookup?lang=de
    [HttpGet("lookup")]
    public async Task<IActionResult> GetLookup([FromQuery] string lang = "de")
    {
        // Walter-Vorgabe 13.06.2026: nur DB-Quelle (siehe GetAll).
        var nationalities = await _context.Nationalities
            .Where(n => n.IsActive)
            .ToListAsync();

        var result = nationalities
            .Select(n => new
            {
                id          = n.Id,
                displayName = string.IsNullOrWhiteSpace(n.NameDe) ? n.Code : n.NameDe
            })
            .OrderBy(x => x.displayName, StringComparer.Create(new System.Globalization.CultureInfo("de-CH"), ignoreCase: true))
            .ToList();

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
        // Walter-Vorgabe 13.06.2026: nur DB als Quelle, alphabetisch nach
        // name_de (de-CH-Culture). Fehlt ein Eintrag → in dieser Verwaltung
        // ergänzen, kein hardgecodeter Fallback mehr.
        var nationalities = await _context.Nationalities.ToListAsync();
        var result = nationalities
            .Select(n => new
            {
                id       = n.Id,
                code     = n.Code,
                code2    = n.Code2,
                isActive = n.IsActive,
                name     = string.IsNullOrWhiteSpace(n.NameDe) ? n.Code : n.NameDe
            })
            .OrderBy(x => x.name, StringComparer.Create(new System.Globalization.CultureInfo("de-CH"), ignoreCase: true))
            .ToList();
        return Ok(result);
    }

    public class NationalityUpdateDto
    {
        public string? Code2 { get; set; }     // null = unverändert, "" = leeren
        public bool? IsActive { get; set; }
        // Walter-Vorgabe 13.06.2026: deutscher Klartextname jetzt admin-pflegbar.
        // null = unverändert; "" = leeren; sonst neuer Wert.
        public string? NameDe { get; set; }
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
        if (dto.NameDe != null)
        {
            var trimmed = dto.NameDe.Trim();
            n.NameDe = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }
        if (dto.IsActive.HasValue) n.IsActive = dto.IsActive.Value;

        await _context.SaveChangesAsync();
        return Ok(new { id = n.Id, code = n.Code, code2 = n.Code2, nameDe = n.NameDe, isActive = n.IsActive });
    }

    // GET /api/nationalities/1
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, [FromQuery] string lang = "de")
    {
        var n = await _context.Nationalities.FirstOrDefaultAsync(x => x.Id == id);
        if (n == null) return NotFound();
        return Ok(new
        {
            id   = n.Id,
            code = n.Code,
            name = string.IsNullOrWhiteSpace(n.NameDe) ? n.Code : n.NameDe
        });
    }
}
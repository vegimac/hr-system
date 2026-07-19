using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Inhalte für OneCrew Moments (Walter-Vorgabe 01.07.2026): Moment-Typen,
/// Emotionsgrade und die Text-Vorlagen je Kombination Typ × Emotionsgrad.
/// Lesen: HR-Team (für das Compose). Schreiben/Verwalten: admin/superuser.
/// </summary>
[ApiController]
[Route("api/moment-content")]
public class MomentContentController : ControllerBase
{
    private readonly AppDbContext _db;
    public MomentContentController(AppDbContext db) { _db = db; }

    private string? ActorName() =>
        User.FindFirst(ClaimTypes.Name)?.Value ?? User.FindFirst("unique_name")?.Value;

    // ── Typen ───────────────────────────────────────────────────────────
    [HttpGet("types")]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> GetTypes([FromQuery] bool all = false)
    {
        var q = _db.MomentTypes.AsNoTracking().AsQueryable();
        if (!all) q = q.Where(t => t.IsActive);
        var list = await q.OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new { t.Id, t.Code, t.Name, t.ConsentCategory, t.SortOrder, t.IsActive })
            .ToListAsync();
        return Ok(list);
    }

    public record TypeDto(string Code, string Name, string ConsentCategory, int SortOrder, bool IsActive);

    [HttpPost("types")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> CreateType([FromBody] TypeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { error = "Code und Name sind Pflicht." });
        if (!IsValidCategory(dto.ConsentCategory))
            return BadRequest(new { error = "consentCategory muss birthday | appreciation | care sein." });
        if (await _db.MomentTypes.AnyAsync(t => t.Code == dto.Code))
            return Conflict(new { error = "Code existiert bereits." });
        var t = new MomentType { Code = dto.Code.Trim(), Name = dto.Name.Trim(), ConsentCategory = dto.ConsentCategory.Trim(), SortOrder = dto.SortOrder, IsActive = dto.IsActive };
        _db.MomentTypes.Add(t);
        await _db.SaveChangesAsync();
        return Ok(new { t.Id });
    }

    [HttpPut("types/{id:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> UpdateType(int id, [FromBody] TypeDto dto)
    {
        var t = await _db.MomentTypes.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        if (!IsValidCategory(dto.ConsentCategory))
            return BadRequest(new { error = "consentCategory muss birthday | appreciation | care sein." });
        t.Name = dto.Name.Trim();
        t.ConsentCategory = dto.ConsentCategory.Trim();
        t.SortOrder = dto.SortOrder;
        t.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static bool IsValidCategory(string? c) => c is "birthday" or "appreciation" or "care";

    // ── Emotionsgrade ───────────────────────────────────────────────────
    [HttpGet("tones")]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> GetTones([FromQuery] bool all = false)
    {
        var q = _db.MomentTones.AsNoTracking().AsQueryable();
        if (!all) q = q.Where(t => t.IsActive);
        var list = await q.OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new { t.Id, t.Code, t.Name, t.SortOrder, t.IsActive })
            .ToListAsync();
        return Ok(list);
    }

    public record ToneDto(string Code, string Name, int SortOrder, bool IsActive);

    [HttpPost("tones")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> CreateTone([FromBody] ToneDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { error = "Code und Name sind Pflicht." });
        if (await _db.MomentTones.AnyAsync(t => t.Code == dto.Code))
            return Conflict(new { error = "Code existiert bereits." });
        var t = new MomentTone { Code = dto.Code.Trim(), Name = dto.Name.Trim(), SortOrder = dto.SortOrder, IsActive = dto.IsActive };
        _db.MomentTones.Add(t);
        await _db.SaveChangesAsync();
        return Ok(new { t.Id });
    }

    [HttpPut("tones/{id:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> UpdateTone(int id, [FromBody] ToneDto dto)
    {
        var t = await _db.MomentTones.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        t.Name = dto.Name.Trim();
        t.SortOrder = dto.SortOrder;
        t.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("tones/{id:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> DeleteTone(int id)
    {
        var t = await _db.MomentTones.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        if (await _db.MomentTexts.AnyAsync(x => x.MomentToneId == id))
            return Conflict(new { error = "Emotionsgrad wird von Texten verwendet — bitte stattdessen deaktivieren." });
        _db.MomentTones.Remove(t);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ── Texte (Kombination Typ × Emotionsgrad) ──────────────────────────
    /// <summary>Texte lesen. Für das Compose: typeId (+ optional toneId) → aktive
    /// Vorlagen. Für die Verwaltung: all=true liefert auch inaktive + Namen.</summary>
    [HttpGet("texts")]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> GetTexts([FromQuery] int? typeId, [FromQuery] int? toneId, [FromQuery] bool all = false)
    {
        var q = _db.MomentTexts.AsNoTracking().AsQueryable();
        if (typeId != null) q = q.Where(x => x.MomentTypeId == typeId);
        if (toneId != null) q = q.Where(x => x.MomentToneId == toneId);
        if (!all) q = q.Where(x => x.IsActive);
        var list = await q
            .OrderBy(x => x.MomentTypeId).ThenBy(x => x.MomentToneId).ThenBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id, x.MomentTypeId, x.MomentToneId,
                typeName = x.MomentType != null ? x.MomentType.Name : null,
                toneName = x.MomentTone != null ? x.MomentTone.Name : null,
                x.Titel, x.SmsText, x.BodyText, x.IsActive, x.SortOrder
            })
            .ToListAsync();
        return Ok(list);
    }

    public record TextDto(int MomentTypeId, int MomentToneId, string? Titel, string? SmsText, string BodyText, bool IsActive, int SortOrder);

    private const int SmsMaxChars = 160;

    private static IActionResult? ValidateSmsLength(string? smsText)
    {
        if (string.IsNullOrWhiteSpace(smsText)) return null;
        // {Link} wird erst beim Versand ersetzt — zählt nicht zur 160-Grenze.
        var len = smsText.Replace("{Link}", "", StringComparison.Ordinal).Trim().Length;
        if (len > SmsMaxChars)
            return new BadRequestObjectResult(new {
                error = $"SMS-Kurztext ist {len} Zeichen (max. {SmsMaxChars}). Ausführlichen Text ins Feld «Mitteilung» — der Link wird automatisch angehängt."
            });
        return null;
    }

    [HttpPost("texts")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> CreateText([FromBody] TextDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.BodyText))
            return BadRequest(new { error = "Mitteilungstext ist Pflicht." });
        if (ValidateSmsLength(dto.SmsText) is { } smsErr) return smsErr;
        if (!await _db.MomentTypes.AnyAsync(t => t.Id == dto.MomentTypeId))
            return BadRequest(new { error = "Moment-Typ nicht gefunden." });
        if (!await _db.MomentTones.AnyAsync(t => t.Id == dto.MomentToneId))
            return BadRequest(new { error = "Emotionsgrad nicht gefunden." });
        var t = new MomentText
        {
            MomentTypeId = dto.MomentTypeId,
            MomentToneId = dto.MomentToneId,
            Titel = string.IsNullOrWhiteSpace(dto.Titel) ? null : dto.Titel.Trim(),
            SmsText = string.IsNullOrWhiteSpace(dto.SmsText) ? null : dto.SmsText.Trim(),
            BodyText = dto.BodyText.Trim(),
            IsActive = dto.IsActive,
            SortOrder = dto.SortOrder,
            CreatedAt = DateTime.Now,
            CreatedBy = ActorName(),
        };
        _db.MomentTexts.Add(t);
        await _db.SaveChangesAsync();
        return Ok(new { t.Id });
    }

    [HttpPut("texts/{id:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> UpdateText(int id, [FromBody] TextDto dto)
    {
        var t = await _db.MomentTexts.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.BodyText))
            return BadRequest(new { error = "Mitteilungstext ist Pflicht." });
        if (ValidateSmsLength(dto.SmsText) is { } smsErr) return smsErr;
        t.MomentTypeId = dto.MomentTypeId;
        t.MomentToneId = dto.MomentToneId;
        t.Titel = string.IsNullOrWhiteSpace(dto.Titel) ? null : dto.Titel.Trim();
        t.SmsText = string.IsNullOrWhiteSpace(dto.SmsText) ? null : dto.SmsText.Trim();
        t.BodyText = dto.BodyText.Trim();
        t.IsActive = dto.IsActive;
        t.SortOrder = dto.SortOrder;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("texts/{id:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> DeleteText(int id)
    {
        var t = await _db.MomentTexts.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        _db.MomentTexts.Remove(t);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}

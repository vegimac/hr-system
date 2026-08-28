using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Hauptsitz-/Rechtseinheiten-Verwaltung (Walter 29.08.2026): System →
/// Filialen &amp; Benutzer → «Hauptsitze». Mehrere Hauptsitze möglich;
/// Filialen werden im Filial-Stammdaten-Modal zugeordnet. Reiner Katalog,
/// kein MA-Lohn → EditLock-Audit-Whitelist.
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/hauptsitze")]
public class HauptsitzController : ControllerBase
{
    private readonly AppDbContext _db;
    public HauptsitzController(AppDbContext db) => _db = db;

    public record HauptsitzDto(string? Name, string? Uid, string? Strasse,
        string? Plz, string? Ort, string? KantonCode, string? Bemerkung, bool? IsActive);

    private static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? UidError(string? uid) =>
        uid != null && !System.Text.RegularExpressions.Regex.IsMatch(uid, @"^CHE-\d{3}\.\d{3}\.\d{3}$")
            ? "UID bitte im Format CHE-XXX.XXX.XXX erfassen."
            : null;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var filialen = await _db.CompanyProfiles.AsNoTracking()
            .Where(p => p.HauptsitzId != null)
            .Select(p => new { p.Id, p.HauptsitzId, Name = p.BranchName ?? p.CompanyName, p.RestaurantCode })
            .ToListAsync();

        var list = await _db.Hauptsitze.AsNoTracking()
            .OrderBy(h => h.Name)
            .ToListAsync();

        return Ok(list.Select(h => new
        {
            h.Id, h.Name, h.Uid, h.Strasse, h.Plz, h.Ort, h.KantonCode,
            h.Bemerkung, h.IsActive,
            filialen = filialen.Where(f => f.HauptsitzId == h.Id)
                .OrderBy(f => f.RestaurantCode)
                .Select(f => new { f.Id, f.Name, f.RestaurantCode })
                .ToList()
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] HauptsitzDto dto)
    {
        var name = Norm(dto.Name);
        if (name == null)
            return BadRequest(new { error = "NAME_FEHLT", message = "Bitte den Firmennamen der Rechtseinheit angeben." });
        var uid = Norm(dto.Uid);
        if (UidError(uid) is string err)
            return BadRequest(new { error = "UID_INVALID", message = err });

        var h = new Hauptsitz
        {
            Name = name, Uid = uid,
            Strasse = Norm(dto.Strasse), Plz = Norm(dto.Plz), Ort = Norm(dto.Ort),
            KantonCode = Norm(dto.KantonCode)?.ToUpperInvariant(),
            Bemerkung = Norm(dto.Bemerkung),
            IsActive = dto.IsActive ?? true
        };
        _db.Hauptsitze.Add(h);
        await _db.SaveChangesAsync();
        return Ok(new { h.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] HauptsitzDto dto)
    {
        var h = await _db.Hauptsitze.FirstOrDefaultAsync(x => x.Id == id);
        if (h == null) return NotFound();

        var name = Norm(dto.Name);
        if (name == null)
            return BadRequest(new { error = "NAME_FEHLT", message = "Bitte den Firmennamen der Rechtseinheit angeben." });
        var uid = Norm(dto.Uid);
        if (UidError(uid) is string err)
            return BadRequest(new { error = "UID_INVALID", message = err });

        h.Name = name; h.Uid = uid;
        h.Strasse = Norm(dto.Strasse); h.Plz = Norm(dto.Plz); h.Ort = Norm(dto.Ort);
        h.KantonCode = Norm(dto.KantonCode)?.ToUpperInvariant();
        h.Bemerkung = Norm(dto.Bemerkung);
        if (dto.IsActive.HasValue) h.IsActive = dto.IsActive.Value;
        h.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { h.Id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool force = false)
    {
        var h = await _db.Hauptsitze.FirstOrDefaultAsync(x => x.Id == id);
        if (h == null) return NotFound();

        var zugeordnet = await _db.CompanyProfiles.CountAsync(p => p.HauptsitzId == id);
        if (zugeordnet > 0 && !force)
            return Conflict(new
            {
                error = "HAUPTSITZ_IN_VERWENDUNG",
                message = $"«{h.Name}» ist {zugeordnet} Filiale(n) zugeordnet. Löschen entfernt die Zuordnungen.",
                filialen = zugeordnet
            });

        // FK ist ON DELETE SET NULL — Zuordnungen werden automatisch gelöst.
        _db.Hauptsitze.Remove(h);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }
}

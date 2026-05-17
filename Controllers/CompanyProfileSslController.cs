using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// CRUD für SSL-Nummern (Quellensteuer-Schuldner-Nummern) pro Filiale × Kanton.
/// Eine Filiale kann für jeden Kanton, in dem sie quellensteuerpflichtige
/// Mitarbeitende beschäftigt, eine eigene SSL-Nummer haben.
/// Pro (Filiale, Kanton) ist jeweils nur ein Eintrag erlaubt (Unique-Index).
/// </summary>
[Authorize]
[ApiController]
[Route("api/companyprofiles/{companyProfileId:int}/ssl")]
public class CompanyProfileSslController : ControllerBase
{
    private readonly AppDbContext _db;
    public CompanyProfileSslController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(int companyProfileId)
    {
        var list = await _db.CompanyProfileSsls
            .Where(s => s.CompanyProfileId == companyProfileId)
            .OrderBy(s => s.KantonCode)
            .Select(s => MapToDto(s))
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int companyProfileId, [FromBody] SslDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(new { error = err });

        // Filiale existiert?
        var exists = await _db.CompanyProfiles.AnyAsync(c => c.Id == companyProfileId);
        if (!exists) return NotFound(new { error = "Filiale nicht gefunden." });

        var kanton = dto.KantonCode!.Trim().ToUpper();

        // Doppelt? (Unique-Index würde sonst eine 500er werfen — schöner mit Klartext)
        var dupe = await _db.CompanyProfileSsls.AnyAsync(s =>
            s.CompanyProfileId == companyProfileId && s.KantonCode == kanton);
        if (dupe)
            return BadRequest(new { error = $"Für Kanton {kanton} existiert bereits eine SSL-Nummer für diese Filiale. Bitte den bestehenden Eintrag bearbeiten." });

        var entry = new CompanyProfileSsl
        {
            CompanyProfileId = companyProfileId,
            KantonCode       = kanton,
            SslNummer        = dto.SslNummer!.Trim(),
            Bemerkung        = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim(),
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow,
        };
        _db.CompanyProfileSsls.Add(entry);
        await _db.SaveChangesAsync();
        return Ok(MapToDto(entry));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int companyProfileId, int id, [FromBody] SslDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(new { error = err });

        var entry = await _db.CompanyProfileSsls
            .FirstOrDefaultAsync(s => s.Id == id && s.CompanyProfileId == companyProfileId);
        if (entry == null) return NotFound();

        var newKanton = dto.KantonCode!.Trim().ToUpper();
        // Wenn der Kanton geändert wird, prüfen ob's eine Kollision gibt
        if (newKanton != entry.KantonCode)
        {
            var dupe = await _db.CompanyProfileSsls.AnyAsync(s =>
                s.Id != id
                && s.CompanyProfileId == companyProfileId
                && s.KantonCode == newKanton);
            if (dupe)
                return BadRequest(new { error = $"Für Kanton {newKanton} existiert bereits eine SSL-Nummer für diese Filiale." });
        }

        entry.KantonCode = newKanton;
        entry.SslNummer  = dto.SslNummer!.Trim();
        entry.Bemerkung  = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        entry.UpdatedAt  = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(MapToDto(entry));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int companyProfileId, int id)
    {
        var entry = await _db.CompanyProfileSsls
            .FirstOrDefaultAsync(s => s.Id == id && s.CompanyProfileId == companyProfileId);
        if (entry == null) return NotFound();
        _db.CompanyProfileSsls.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Helpers ────────────────────────────────────────────────────────
    private static string? Validate(SslDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.KantonCode) || dto.KantonCode.Trim().Length != 2)
            return "Kanton-Code (2 Zeichen, z.B. LU) ist Pflicht.";
        if (string.IsNullOrWhiteSpace(dto.SslNummer))
            return "SSL-Nummer ist Pflicht.";
        return null;
    }

    private static object MapToDto(CompanyProfileSsl s) => new
    {
        id               = s.Id,
        companyProfileId = s.CompanyProfileId,
        kantonCode       = s.KantonCode,
        sslNummer        = s.SslNummer,
        bemerkung        = s.Bemerkung,
        createdAt        = s.CreatedAt,
        updatedAt        = s.UpdatedAt,
    };
}

public record SslDto(
    string? KantonCode,
    string? SslNummer,
    string? Bemerkung
);

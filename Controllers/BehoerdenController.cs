using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Ämter-Stammdaten (Betreibungsämter, Sozialämter etc.) als Empfänger von
/// Lohnabtretungen. Einmal erfasst, mehrfach nutzbar.
/// </summary>
[Authorize]
[ApiController]
[Route("api/behoerden")]
public class BehoerdenController : ControllerBase
{
    private readonly AppDbContext _db;
    public BehoerdenController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool includeInactive = false)
    {
        var q = _db.Behoerden.AsQueryable();
        if (!includeInactive) q = q.Where(b => b.IsActive);
        var list = await q.OrderBy(b => b.Name).Select(b => MapToDto(b)).ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var b = await _db.Behoerden.FindAsync(id);
        if (b == null) return NotFound();
        return Ok(MapToDto(b));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] BehoerdeDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(err);

        var entry = new Behoerde
        {
            Name      = dto.Name.Trim(),
            Typ       = (dto.Typ ?? "BETREIBUNGSAMT").Trim().ToUpper(),
            Adresse1  = dto.Adresse1?.Trim(),
            Adresse2  = dto.Adresse2?.Trim(),
            Adresse3  = dto.Adresse3?.Trim(),
            Plz       = dto.Plz?.Trim(),
            Ort       = dto.Ort?.Trim(),
            Telefon   = dto.Telefon?.Trim(),
            Email     = dto.Email?.Trim(),
            Iban      = NormalizeIban(dto.Iban),
            QrIban    = NormalizeIban(dto.QrIban),
            Bic       = dto.Bic?.Trim(),
            BankName  = dto.BankName?.Trim(),
            IsActive  = dto.IsActive ?? true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Behoerden.Add(entry);
        await _db.SaveChangesAsync();
        return Ok(MapToDto(entry));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] BehoerdeDto dto)
    {
        var entry = await _db.Behoerden.FindAsync(id);
        if (entry == null) return NotFound();

        var err = Validate(dto);
        if (err != null) return BadRequest(err);

        entry.Name      = dto.Name.Trim();
        entry.Typ       = (dto.Typ ?? "BETREIBUNGSAMT").Trim().ToUpper();
        entry.Adresse1  = dto.Adresse1?.Trim();
        entry.Adresse2  = dto.Adresse2?.Trim();
        entry.Adresse3  = dto.Adresse3?.Trim();
        entry.Plz       = dto.Plz?.Trim();
        entry.Ort       = dto.Ort?.Trim();
        entry.Telefon   = dto.Telefon?.Trim();
        entry.Email     = dto.Email?.Trim();
        entry.Iban      = NormalizeIban(dto.Iban);
        entry.QrIban    = NormalizeIban(dto.QrIban);
        entry.Bic       = dto.Bic?.Trim();
        entry.BankName  = dto.BankName?.Trim();
        entry.IsActive  = dto.IsActive ?? true;
        entry.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(MapToDto(entry));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await _db.Behoerden.FindAsync(id);
        if (entry == null) return NotFound();

        // Wenn referenziert: Soft-Delete (IsActive=false), sonst hart löschen
        bool referenziert = await _db.EmployeeLohnAssignments.AnyAsync(a => a.BehoerdeId == id);
        if (referenziert)
        {
            entry.IsActive  = false;
            entry.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { softDeleted = true });
        }
        _db.Behoerden.Remove(entry);
        await _db.SaveChangesAsync();
        return Ok(new { softDeleted = false });
    }

    // ── Helpers ─────────────────────────────────────────────────────────
    private static string? Validate(BehoerdeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Name ist erforderlich.";
        if (dto.Typ != null && dto.Typ != "BETREIBUNGSAMT" && dto.Typ != "SOZIALAMT" && dto.Typ != "ANDERE")
            return "Typ: erlaubt sind BETREIBUNGSAMT, SOZIALAMT, ANDERE.";
        return null;
    }

    private static string? NormalizeIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return null;
        // IBAN: Leerzeichen entfernen, uppercase
        return iban.Replace(" ", "").ToUpper();
    }

    private static object MapToDto(Behoerde b) => new
    {
        id        = b.Id,
        name      = b.Name,
        typ       = b.Typ,
        adresse1  = b.Adresse1,
        adresse2  = b.Adresse2,
        adresse3  = b.Adresse3,
        plz       = b.Plz,
        ort       = b.Ort,
        telefon   = b.Telefon,
        email     = b.Email,
        iban      = b.Iban,
        qrIban    = b.QrIban,
        bic       = b.Bic,
        bankName  = b.BankName,
        isActive  = b.IsActive,
        createdAt = b.CreatedAt
    };
}

public record BehoerdeDto(
    string  Name,
    string? Typ,
    string? Adresse1,
    string? Adresse2,
    string? Adresse3,
    string? Plz,
    string? Ort,
    string? Telefon,
    string? Email,
    string? Iban,
    string? QrIban,
    string? Bic,
    string? BankName,
    bool?   IsActive
);

using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Ämter-Stammdaten (Betreibungsämter, Sozialämter etc.) als Empfänger von
/// Lohnabtretungen. Einmal erfasst, mehrfach nutzbar.
/// Sachbearbeiter-Stamm pro Behörde (Walter 02.08.2026).
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
        var list = await q
            .OrderBy(b => b.Name)
            .Select(b => new
            {
                id                 = b.Id,
                name               = b.Name,
                typ                = b.Typ,
                kantonCode         = b.KantonCode,
                adresse1           = b.Adresse1,
                adresse2           = b.Adresse2,
                adresse3           = b.Adresse3,
                plz                = b.Plz,
                ort                = b.Ort,
                telefon            = b.Telefon,
                handy              = b.Handy,
                email              = b.Email,
                kontaktperson      = b.Kontaktperson,
                kontaktpersonRolle = b.KontaktpersonRolle,
                erreichbarkeit     = b.Erreichbarkeit,
                webseite           = b.Webseite,
                iban               = b.Iban,
                qrIban             = b.QrIban,
                kontoinhaber       = b.KontoinhaberBehoerde != null
                    ? b.KontoinhaberBehoerde.Name
                    : b.Kontoinhaber,
                kontoinhaberBehoerdeId   = b.KontoinhaberBehoerdeId,
                kontoinhaberBehoerdeName = b.KontoinhaberBehoerde != null
                    ? b.KontoinhaberBehoerde.Name
                    : null,
                bic                = b.Bic,
                bankName           = b.BankName,
                isActive           = b.IsActive,
                createdAt          = b.CreatedAt,
                sachbearbeiterCount = b.Sachbearbeiter.Count(s => s.IsActive),
                sachbearbeiterNames = b.Sachbearbeiter
                    .Where(s => s.IsActive)
                    .OrderBy(s => s.Name)
                    .Select(s => s.Name)
                    .Take(4)
                    .ToList()
            })
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var b = await _db.Behoerden
            .Include(x => x.KontoinhaberBehoerde)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound();
        return Ok(MapToDto(b));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] BehoerdeDto dto)
    {
        var err = await ValidateAsync(dto);
        if (err != null) return BadRequest(err);

        var entry = new Behoerde
        {
            Name      = dto.Name.Trim(),
            Typ       = (dto.Typ ?? "BETREIBUNGSAMT").Trim().ToUpper(),
            KantonCode         = NormalizeKanton(dto.KantonCode),
            Adresse1  = dto.Adresse1?.Trim(),
            Adresse2  = dto.Adresse2?.Trim(),
            Adresse3  = dto.Adresse3?.Trim(),
            Plz       = dto.Plz?.Trim(),
            Ort       = dto.Ort?.Trim(),
            Telefon   = dto.Telefon?.Trim(),
            Handy     = dto.Handy?.Trim(),
            Email     = dto.Email?.Trim(),
            Kontaktperson      = dto.Kontaktperson?.Trim(),
            KontaktpersonRolle = dto.KontaktpersonRolle?.Trim(),
            Erreichbarkeit     = dto.Erreichbarkeit?.Trim(),
            Webseite           = dto.Webseite?.Trim(),
            Iban      = NormalizeIban(dto.Iban),
            QrIban    = NormalizeIban(dto.QrIban),
            Kontoinhaber = null, // UI: nur noch FK auf andere Behörde
            KontoinhaberBehoerdeId = dto.KontoinhaberBehoerdeId,
            Bic       = dto.Bic?.Trim(),
            BankName  = dto.BankName?.Trim(),
            IsActive  = dto.IsActive ?? true,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        _db.Behoerden.Add(entry);
        await _db.SaveChangesAsync();
        await _db.Entry(entry).Reference(e => e.KontoinhaberBehoerde).LoadAsync();
        return Ok(MapToDto(entry));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] BehoerdeDto dto)
    {
        var entry = await _db.Behoerden.FindAsync(id);
        if (entry == null) return NotFound();

        var err = await ValidateAsync(dto, id);
        if (err != null) return BadRequest(err);

        entry.Name      = dto.Name.Trim();
        entry.Typ       = (dto.Typ ?? "BETREIBUNGSAMT").Trim().ToUpper();
        entry.KantonCode         = NormalizeKanton(dto.KantonCode);
        entry.Adresse1  = dto.Adresse1?.Trim();
        entry.Adresse2  = dto.Adresse2?.Trim();
        entry.Adresse3  = dto.Adresse3?.Trim();
        entry.Plz       = dto.Plz?.Trim();
        entry.Ort       = dto.Ort?.Trim();
        entry.Telefon   = dto.Telefon?.Trim();
        entry.Handy     = dto.Handy?.Trim();
        entry.Email     = dto.Email?.Trim();
        entry.Kontaktperson      = dto.Kontaktperson?.Trim();
        entry.KontaktpersonRolle = dto.KontaktpersonRolle?.Trim();
        entry.Erreichbarkeit     = dto.Erreichbarkeit?.Trim();
        entry.Webseite           = dto.Webseite?.Trim();
        entry.Iban      = NormalizeIban(dto.Iban);
        entry.QrIban    = NormalizeIban(dto.QrIban);
        entry.Kontoinhaber = null;
        entry.KontoinhaberBehoerdeId = dto.KontoinhaberBehoerdeId;
        entry.Bic       = dto.Bic?.Trim();
        entry.BankName  = dto.BankName?.Trim();
        entry.IsActive  = dto.IsActive ?? true;
        entry.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _db.Entry(entry).Reference(e => e.KontoinhaberBehoerde).LoadAsync();
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
            entry.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();
            return Ok(new { softDeleted = true });
        }
        _db.Behoerden.Remove(entry);
        await _db.SaveChangesAsync();
        return Ok(new { softDeleted = false });
    }

    // ── Sachbearbeiter-Stamm ─────────────────────────────────────────────

    [HttpGet("{behoerdeId:int}/sachbearbeiter")]
    public async Task<IActionResult> GetSachbearbeiter(int behoerdeId, [FromQuery] bool includeInactive = false)
    {
        if (!await _db.Behoerden.AnyAsync(b => b.Id == behoerdeId))
            return NotFound(new { error = "Behörde nicht gefunden." });

        var q = _db.BehoerdeSachbearbeiter.Where(s => s.BehoerdeId == behoerdeId);
        if (!includeInactive) q = q.Where(s => s.IsActive);
        var list = await q.OrderBy(s => s.Name).Select(s => MapSbDto(s)).ToListAsync();
        return Ok(list);
    }

    [HttpPost("{behoerdeId:int}/sachbearbeiter")]
    public async Task<IActionResult> CreateSachbearbeiter(int behoerdeId, [FromBody] BehoerdeSachbearbeiterDto dto)
    {
        if (!await _db.Behoerden.AnyAsync(b => b.Id == behoerdeId))
            return NotFound(new { error = "Behörde nicht gefunden." });
        var err = ValidateSb(dto);
        if (err != null) return BadRequest(err);

        var entry = new BehoerdeSachbearbeiter
        {
            BehoerdeId     = behoerdeId,
            Name           = dto.Name.Trim(),
            Rolle          = dto.Rolle?.Trim(),
            Telefon        = dto.Telefon?.Trim(),
            Handy          = dto.Handy?.Trim(),
            Email          = dto.Email?.Trim(),
            Erreichbarkeit = dto.Erreichbarkeit?.Trim(),
            Bemerkung      = dto.Bemerkung?.Trim(),
            IsActive       = dto.IsActive ?? true,
            CreatedAt      = DateTime.Now,
            UpdatedAt      = DateTime.Now
        };
        _db.BehoerdeSachbearbeiter.Add(entry);
        await _db.SaveChangesAsync();
        return Ok(MapSbDto(entry));
    }

    [HttpPut("{behoerdeId:int}/sachbearbeiter/{id:int}")]
    public async Task<IActionResult> UpdateSachbearbeiter(int behoerdeId, int id, [FromBody] BehoerdeSachbearbeiterDto dto)
    {
        var entry = await _db.BehoerdeSachbearbeiter
            .FirstOrDefaultAsync(s => s.Id == id && s.BehoerdeId == behoerdeId);
        if (entry == null) return NotFound();

        var err = ValidateSb(dto);
        if (err != null) return BadRequest(err);

        entry.Name           = dto.Name.Trim();
        entry.Rolle          = dto.Rolle?.Trim();
        entry.Telefon        = dto.Telefon?.Trim();
        entry.Handy          = dto.Handy?.Trim();
        entry.Email          = dto.Email?.Trim();
        entry.Erreichbarkeit = dto.Erreichbarkeit?.Trim();
        entry.Bemerkung      = dto.Bemerkung?.Trim();
        entry.IsActive       = dto.IsActive ?? true;
        entry.UpdatedAt      = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(MapSbDto(entry));
    }

    [HttpDelete("{behoerdeId:int}/sachbearbeiter/{id:int}")]
    public async Task<IActionResult> DeleteSachbearbeiter(int behoerdeId, int id)
    {
        var entry = await _db.BehoerdeSachbearbeiter
            .FirstOrDefaultAsync(s => s.Id == id && s.BehoerdeId == behoerdeId);
        if (entry == null) return NotFound();

        bool inUse = await _db.EmployeeLohnAssignments
            .AnyAsync(a => a.BehoerdeSachbearbeiterId == id);
        if (inUse)
        {
            entry.IsActive  = false;
            entry.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();
            return Ok(new { softDeleted = true });
        }
        _db.BehoerdeSachbearbeiter.Remove(entry);
        await _db.SaveChangesAsync();
        return Ok(new { softDeleted = false });
    }

    // ── Helpers ─────────────────────────────────────────────────────────
    private async Task<string?> ValidateAsync(BehoerdeDto dto, int? selfId = null)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Name ist erforderlich.";
        if (dto.Typ != null
            && dto.Typ != "BETREIBUNGSAMT"
            && dto.Typ != "SOZIALAMT"
            && dto.Typ != "STEUERAMT"
            && dto.Typ != "ANDERE")
            return "Typ: erlaubt sind BETREIBUNGSAMT, SOZIALAMT, STEUERAMT, ANDERE.";
        // Bei STEUERAMT muss der Kanton bekannt sein, damit das QST-Formular
        // automatisch das richtige Steueramt zur Filiale finden kann.
        if (string.Equals(dto.Typ, "STEUERAMT", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(dto.KantonCode))
            return "Kanton-Code ist bei STEUERAMT erforderlich (z.B. LU, AG).";
        if (dto.KontoinhaberBehoerdeId.HasValue)
        {
            if (selfId.HasValue && dto.KontoinhaberBehoerdeId.Value == selfId.Value)
                return "Kontoinhaber darf nicht dieselbe Behörde sein — bitte die Hauptstelle wählen (z.B. ORS Zürich).";
            var ok = await _db.Behoerden.AnyAsync(b =>
                b.Id == dto.KontoinhaberBehoerdeId.Value && b.IsActive);
            if (!ok) return "Gewählte Kontoinhaber-Behörde nicht gefunden oder inaktiv.";
        }
        return null;
    }

    private static string? ValidateSb(BehoerdeSachbearbeiterDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Name des Sachbearbeiters ist erforderlich.";
        return null;
    }

    private static string? NormalizeIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return null;
        // IBAN: Leerzeichen entfernen, uppercase
        return iban.Replace(" ", "").ToUpper();
    }

    private static string? NormalizeKanton(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return code.Trim().ToUpper();
    }

    private static object MapToDto(Behoerde b) => new
    {
        id                 = b.Id,
        name               = b.Name,
        typ                = b.Typ,
        kantonCode         = b.KantonCode,
        adresse1           = b.Adresse1,
        adresse2           = b.Adresse2,
        adresse3           = b.Adresse3,
        plz                = b.Plz,
        ort                = b.Ort,
        telefon            = b.Telefon,
        handy              = b.Handy,
        email              = b.Email,
        kontaktperson      = b.Kontaktperson,
        kontaktpersonRolle = b.KontaktpersonRolle,
        erreichbarkeit     = b.Erreichbarkeit,
        webseite           = b.Webseite,
        iban               = b.Iban,
        qrIban             = b.QrIban,
        kontoinhaber       = b.KontoinhaberBehoerde?.Name ?? b.Kontoinhaber,
        kontoinhaberBehoerdeId   = b.KontoinhaberBehoerdeId,
        kontoinhaberBehoerdeName = b.KontoinhaberBehoerde?.Name,
        bic                = b.Bic,
        bankName           = b.BankName,
        isActive           = b.IsActive,
        createdAt          = b.CreatedAt
    };

    private static object MapSbDto(BehoerdeSachbearbeiter s) => new
    {
        id             = s.Id,
        behoerdeId     = s.BehoerdeId,
        name           = s.Name,
        rolle          = s.Rolle,
        telefon        = s.Telefon,
        handy          = s.Handy,
        email          = s.Email,
        erreichbarkeit = s.Erreichbarkeit,
        bemerkung      = s.Bemerkung,
        isActive       = s.IsActive,
        createdAt      = s.CreatedAt
    };
}

public record BehoerdeDto(
    string  Name,
    string? Typ,
    string? KantonCode,
    string? Adresse1,
    string? Adresse2,
    string? Adresse3,
    string? Plz,
    string? Ort,
    string? Telefon,
    string? Handy,
    string? Email,
    string? Kontaktperson,
    string? KontaktpersonRolle,
    string? Erreichbarkeit,
    string? Webseite,
    string? Iban,
    string? QrIban,
    string? Bic,
    string? BankName,
    bool?   IsActive,
    string? Kontoinhaber = null,
    int?    KontoinhaberBehoerdeId = null
);

public record BehoerdeSachbearbeiterDto(
    string  Name,
    string? Rolle,
    string? Telefon,
    string? Handy,
    string? Email,
    string? Erreichbarkeit,
    string? Bemerkung,
    bool?   IsActive
);

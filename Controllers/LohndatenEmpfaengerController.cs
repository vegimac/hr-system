using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Lohndatenempfänger (Walter-Vorgabe 06.08.2026, Mirus-Vorbild):
/// zentraler Empfänger-Katalog + Zuordnung pro Filiale mit Mitglied-/
/// Subnummer. Gepflegt aus dem Filial-Tab «Empfänger» (kein eigener
/// Systemsteuerungs-Punkt — beim Zuordnen kann ein neuer Empfänger
/// gleich miterfasst werden).
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/lohndaten-empfaenger")]
public class LohndatenEmpfaengerController : ControllerBase
{
    private readonly AppDbContext _db;
    public LohndatenEmpfaengerController(AppDbContext db) => _db = db;

    // ── Katalog ───────────────────────────────────────────────────────────

    /// <summary>Alle Empfänger (Katalog), inkl. Anzahl Zuordnungen.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool all = false)
    {
        var list = await _db.LohndatenEmpfaengers.AsNoTracking()
            .Where(e => all || e.IsActive)
            .OrderBy(e => e.Art).ThenBy(e => e.Bezeichnung)
            .Select(e => new
            {
                e.Id, e.Art, e.Bezeichnung, e.Zusatz, e.UidNummer,
                e.Strasse, e.Postfach, e.Plz, e.Ort, e.KantonCode,
                e.Kassennummer, e.SupportEmail, e.Bemerkung, e.IsActive,
                zuordnungen = e.Zuordnungen.Count,
            })
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LohndatenEmpfaengerDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Bezeichnung))
            return BadRequest(new { error = "BEZEICHNUNG_FEHLT" });
        var e = new LohndatenEmpfaenger();
        Apply(e, dto);
        _db.LohndatenEmpfaengers.Add(e);
        await _db.SaveChangesAsync();
        return Ok(new { e.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] LohndatenEmpfaengerDto dto)
    {
        var e = await _db.LohndatenEmpfaengers.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        Apply(e, dto);
        e.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { e.Id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool force = false)
    {
        var e = await _db.LohndatenEmpfaengers
            .Include(x => x.Zuordnungen)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        if (e.Zuordnungen.Count > 0 && !force)
            return Conflict(new
            {
                error = "EMPFAENGER_IN_VERWENDUNG",
                message = $"Empfänger «{e.Bezeichnung}» ist {e.Zuordnungen.Count} Filiale(n) zugeordnet.",
                zuordnungen = e.Zuordnungen.Count,
            });
        _db.LohndatenEmpfaengers.Remove(e); // Cascade löscht Zuordnungen
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    private static void Apply(LohndatenEmpfaenger e, LohndatenEmpfaengerDto dto)
    {
        e.Art          = string.IsNullOrWhiteSpace(dto.Art) ? "ANDERE" : dto.Art.Trim().ToUpperInvariant();
        e.Bezeichnung  = (dto.Bezeichnung ?? "").Trim();
        e.Zusatz       = Norm(dto.Zusatz);
        e.UidNummer    = Norm(dto.UidNummer);
        e.Strasse      = Norm(dto.Strasse);
        e.Postfach     = Norm(dto.Postfach);
        e.Plz          = Norm(dto.Plz);
        e.Ort          = Norm(dto.Ort);
        e.KantonCode   = Norm(dto.KantonCode)?.ToUpperInvariant();
        e.Kassennummer = Norm(dto.Kassennummer);
        e.SupportEmail = Norm(dto.SupportEmail);
        e.Bemerkung    = Norm(dto.Bemerkung);
        if (dto.IsActive.HasValue) e.IsActive = dto.IsActive.Value;
    }

    private static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Zuordnungen Empfänger ↔ Filiale (Mitglied-/Subnummer pro Filiale).</summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/companyprofiles/{cpId:int}/empfaenger")]
public class CompanyProfileEmpfaengerController : ControllerBase
{
    private readonly AppDbContext _db;
    public CompanyProfileEmpfaengerController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetForBranch(int cpId)
    {
        var list = await _db.CompanyProfileEmpfaengers.AsNoTracking()
            .Where(z => z.CompanyProfileId == cpId)
            .OrderBy(z => z.Empfaenger!.Art).ThenBy(z => z.Empfaenger!.Bezeichnung)
            .Select(z => new
            {
                z.Id, z.EmpfaengerId, z.Mitgliednummer, z.Subnummer,
                z.GueltigAb, z.Bemerkung, z.IsActive,
                art          = z.Empfaenger!.Art,
                bezeichnung  = z.Empfaenger!.Bezeichnung,
                zusatz       = z.Empfaenger!.Zusatz,
                strasse      = z.Empfaenger!.Strasse,
                postfach     = z.Empfaenger!.Postfach,
                plz          = z.Empfaenger!.Plz,
                ort          = z.Empfaenger!.Ort,
                kantonCode   = z.Empfaenger!.KantonCode,
                kassennummer = z.Empfaenger!.Kassennummer,
                supportEmail = z.Empfaenger!.SupportEmail,
            })
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int cpId, [FromBody] CpEmpfaengerDto dto)
    {
        var cpExists = await _db.CompanyProfiles.AnyAsync(c => c.Id == cpId);
        if (!cpExists) return NotFound(new { error = "BRANCH_NOT_FOUND" });
        var empExists = await _db.LohndatenEmpfaengers.AnyAsync(e => e.Id == dto.EmpfaengerId);
        if (!empExists) return NotFound(new { error = "EMPFAENGER_NOT_FOUND" });
        var dup = await _db.CompanyProfileEmpfaengers
            .AnyAsync(z => z.CompanyProfileId == cpId && z.EmpfaengerId == dto.EmpfaengerId);
        if (dup) return Conflict(new { error = "EMPFAENGER_BEREITS_ZUGEORDNET" });

        var z = new CompanyProfileEmpfaenger
        {
            CompanyProfileId = cpId,
            EmpfaengerId     = dto.EmpfaengerId,
        };
        ApplyZuordnung(z, dto);
        _db.CompanyProfileEmpfaengers.Add(z);
        await _db.SaveChangesAsync();
        return Ok(new { z.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int cpId, int id, [FromBody] CpEmpfaengerDto dto)
    {
        var z = await _db.CompanyProfileEmpfaengers
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyProfileId == cpId);
        if (z == null) return NotFound();
        ApplyZuordnung(z, dto);
        z.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { z.Id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int cpId, int id)
    {
        var z = await _db.CompanyProfileEmpfaengers
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyProfileId == cpId);
        if (z == null) return NotFound();
        _db.CompanyProfileEmpfaengers.Remove(z);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    private static void ApplyZuordnung(CompanyProfileEmpfaenger z, CpEmpfaengerDto dto)
    {
        z.Mitgliednummer = string.IsNullOrWhiteSpace(dto.Mitgliednummer) ? null : dto.Mitgliednummer.Trim();
        z.Subnummer      = string.IsNullOrWhiteSpace(dto.Subnummer) ? null : dto.Subnummer.Trim();
        z.Bemerkung      = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        z.GueltigAb      = DateOnly.TryParse(dto.GueltigAb, out var ab) ? ab : null;
        if (dto.IsActive.HasValue) z.IsActive = dto.IsActive.Value;
    }
}

public class LohndatenEmpfaengerDto
{
    public string? Art { get; set; }
    public string? Bezeichnung { get; set; }
    public string? Zusatz { get; set; }
    public string? UidNummer { get; set; }
    public string? Strasse { get; set; }
    public string? Postfach { get; set; }
    public string? Plz { get; set; }
    public string? Ort { get; set; }
    public string? KantonCode { get; set; }
    public string? Kassennummer { get; set; }
    public string? SupportEmail { get; set; }
    public string? Bemerkung { get; set; }
    public bool? IsActive { get; set; }
}

public class CpEmpfaengerDto
{
    public int EmpfaengerId { get; set; }
    public string? Mitgliednummer { get; set; }
    public string? Subnummer { get; set; }
    public string? Bemerkung { get; set; }
    /// <summary>ISO yyyy-MM-dd (native Datumsfelder) — leer = seit jeher.</summary>
    public string? GueltigAb { get; set; }
    public bool? IsActive { get; set; }
}

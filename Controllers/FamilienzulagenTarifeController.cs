using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// CRUD für kantonale Familienzulagen-Tarife (FAK-Sätze).
///
/// Im Gegensatz zur Quellensteuer ist die Familienzulage NACH STANDORT
/// der Filiale zu berechnen, nicht nach Wohnort des MA. Der Lohnlauf
/// liest daher den Tarif zur <c>CompanyProfile.KantonCode</c> der
/// Filiale (siehe Phase B).
///
/// Pflege erfolgt über die Systemeinstellungen analog zu den QST-Tarifen.
/// Lesen ist für jeden Authenticated erlaubt (Lohnlauf braucht's).
/// Schreiben nur für admin und superuser.
/// </summary>
[Authorize]
[ApiController]
[Route("api/familienzulagen-tarife")]
public class FamilienzulagenTarifeController : ControllerBase
{
    private readonly AppDbContext _db;
    public FamilienzulagenTarifeController(AppDbContext db) => _db = db;

    /// <summary>
    /// Liefert alle Tarife (alle Kantone, alle Gültigkeitsperioden).
    /// Frontend-UI gruppiert/filtert clientseitig.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? kantonCode, [FromQuery] DateOnly? effective)
    {
        var q = _db.FamilienzulagenTarife.AsQueryable();
        if (!string.IsNullOrWhiteSpace(kantonCode))
            q = q.Where(t => t.KantonCode == kantonCode.ToUpper());

        // Optional: nur die zum Stichtag gültigen Einträge zurückgeben
        if (effective.HasValue)
        {
            var d = effective.Value;
            q = q.Where(t => t.IsActive
                          && t.ValidFrom <= d
                          && (t.ValidTo == null || t.ValidTo >= d));
        }

        var list = await q
            .OrderBy(t => t.KantonCode)
            .ThenByDescending(t => t.ValidFrom)
            .Select(t => MapToDto(t))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOne(int id)
    {
        var t = await _db.FamilienzulagenTarife.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        return Ok(MapToDto(t));
    }

    [HttpPost]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Create([FromBody] FamilienzulagenTarifDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(new { error = err });

        var kanton = dto.KantonCode!.Trim().ToUpper();
        var validFrom = dto.ValidFrom!.Value;

        // Doppelt-Eintrag pro (Kanton, ValidFrom) verhindern
        var dupe = await _db.FamilienzulagenTarife.AnyAsync(t =>
            t.KantonCode == kanton && t.ValidFrom == validFrom);
        if (dupe)
            return BadRequest(new { error = $"Für Kanton {kanton} existiert bereits ein Tarif mit Gültig-ab {validFrom:dd.MM.yyyy}. Bitte bearbeiten statt neu anlegen." });

        var entry = new FamilienzulagenTarif
        {
            KantonCode                     = kanton,
            ValidFrom                      = validFrom,
            ValidTo                        = dto.ValidTo,
            KinderzulageSatz1              = dto.KinderzulageSatz1,
            KinderzulageSatz2              = dto.KinderzulageSatz2,
            KinderzulageSatz2AbAlter       = dto.KinderzulageSatz2AbAlter,
            AusbildungszulageSatz1         = dto.AusbildungszulageSatz1,
            AusbildungszulageSatz2         = dto.AusbildungszulageSatz2,
            AusbildungszulageSatz2AbAlter  = dto.AusbildungszulageSatz2AbAlter,
            SchwelleSatz2AnzahlKinder      = dto.SchwelleSatz2AnzahlKinder,
            MindesterwerbseinkommenJahr  = dto.MindesterwerbseinkommenJahr,
            MindesterwerbseinkommenMonat = dto.MindesterwerbseinkommenMonat,
            GeburtszulageBetrag          = dto.GeburtszulageBetrag,
            AdoptionszulageBetrag        = dto.AdoptionszulageBetrag,
            AltersGrenzeKinder         = dto.AltersGrenzeKinder ?? 16,
            AltersGrenzeAusbildung     = dto.AltersGrenzeAusbildung ?? 25,
            Quelle                     = string.IsNullOrWhiteSpace(dto.Quelle) ? null : dto.Quelle.Trim(),
            Bemerkung                  = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim(),
            IsActive                   = dto.IsActive ?? true,
            CreatedAt                  = DateTime.UtcNow,
            UpdatedAt                  = DateTime.UtcNow,
        };
        _db.FamilienzulagenTarife.Add(entry);
        await _db.SaveChangesAsync();
        return Ok(MapToDto(entry));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Update(int id, [FromBody] FamilienzulagenTarifDto dto)
    {
        var err = Validate(dto);
        if (err != null) return BadRequest(new { error = err });

        var entry = await _db.FamilienzulagenTarife.FirstOrDefaultAsync(t => t.Id == id);
        if (entry == null) return NotFound();

        var newKanton = dto.KantonCode!.Trim().ToUpper();
        var newValidFrom = dto.ValidFrom!.Value;

        // Kollision mit anderem Eintrag (Kanton, ValidFrom)?
        if (newKanton != entry.KantonCode || newValidFrom != entry.ValidFrom)
        {
            var dupe = await _db.FamilienzulagenTarife.AnyAsync(t =>
                t.Id != id
                && t.KantonCode == newKanton
                && t.ValidFrom == newValidFrom);
            if (dupe)
                return BadRequest(new { error = $"Für Kanton {newKanton} existiert bereits ein Tarif mit Gültig-ab {newValidFrom:dd.MM.yyyy}." });
        }

        entry.KantonCode                     = newKanton;
        entry.ValidFrom                      = newValidFrom;
        entry.ValidTo                        = dto.ValidTo;
        entry.KinderzulageSatz1              = dto.KinderzulageSatz1;
        entry.KinderzulageSatz2              = dto.KinderzulageSatz2;
        entry.KinderzulageSatz2AbAlter       = dto.KinderzulageSatz2AbAlter;
        entry.AusbildungszulageSatz1         = dto.AusbildungszulageSatz1;
        entry.AusbildungszulageSatz2         = dto.AusbildungszulageSatz2;
        entry.AusbildungszulageSatz2AbAlter  = dto.AusbildungszulageSatz2AbAlter;
        entry.SchwelleSatz2AnzahlKinder      = dto.SchwelleSatz2AnzahlKinder;
        entry.MindesterwerbseinkommenJahr  = dto.MindesterwerbseinkommenJahr;
        entry.MindesterwerbseinkommenMonat = dto.MindesterwerbseinkommenMonat;
        entry.GeburtszulageBetrag          = dto.GeburtszulageBetrag;
        entry.AdoptionszulageBetrag        = dto.AdoptionszulageBetrag;
        entry.AltersGrenzeKinder         = dto.AltersGrenzeKinder ?? entry.AltersGrenzeKinder;
        entry.AltersGrenzeAusbildung     = dto.AltersGrenzeAusbildung ?? entry.AltersGrenzeAusbildung;
        entry.Quelle                     = string.IsNullOrWhiteSpace(dto.Quelle) ? null : dto.Quelle.Trim();
        entry.Bemerkung                  = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        if (dto.IsActive.HasValue) entry.IsActive = dto.IsActive.Value;
        entry.UpdatedAt                  = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(MapToDto(entry));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await _db.FamilienzulagenTarife.FirstOrDefaultAsync(t => t.Id == id);
        if (entry == null) return NotFound();
        _db.FamilienzulagenTarife.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Helpers ────────────────────────────────────────────────────────
    private static string? Validate(FamilienzulagenTarifDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.KantonCode) || dto.KantonCode.Trim().Length != 2)
            return "Kanton-Code (2 Zeichen, z.B. LU) ist Pflicht.";
        if (!dto.ValidFrom.HasValue)
            return "Gültig-ab Datum ist Pflicht.";
        if (dto.ValidTo.HasValue && dto.ValidTo.Value < dto.ValidFrom.Value)
            return "Gültig-bis darf nicht vor Gültig-ab liegen.";
        if (dto.KinderzulageSatz1.HasValue && dto.KinderzulageSatz1.Value < 0)
            return "Kinderzulage-Satz1 darf nicht negativ sein.";
        if (dto.KinderzulageSatz2.HasValue && dto.KinderzulageSatz2.Value < 0)
            return "Kinderzulage-Satz2 darf nicht negativ sein.";
        if (dto.AusbildungszulageSatz1.HasValue && dto.AusbildungszulageSatz1.Value < 0)
            return "Ausbildungszulage-Satz1 darf nicht negativ sein.";
        if (dto.AusbildungszulageSatz2.HasValue && dto.AusbildungszulageSatz2.Value < 0)
            return "Ausbildungszulage-Satz2 darf nicht negativ sein.";
        if ((dto.AltersGrenzeKinder ?? 16) < 0 || (dto.AltersGrenzeKinder ?? 16) > 25)
            return "Altersgrenze Kinder muss zwischen 0 und 25 liegen.";
        if ((dto.AltersGrenzeAusbildung ?? 25) < (dto.AltersGrenzeKinder ?? 16))
            return "Altersgrenze Ausbildung muss ≥ Altersgrenze Kinder sein.";
        return null;
    }

    private static object MapToDto(FamilienzulagenTarif t) => new
    {
        id                              = t.Id,
        kantonCode                      = t.KantonCode,
        validFrom                       = t.ValidFrom,
        validTo                         = t.ValidTo,
        kinderzulageSatz1               = t.KinderzulageSatz1,
        kinderzulageSatz2               = t.KinderzulageSatz2,
        kinderzulageSatz2AbAlter        = t.KinderzulageSatz2AbAlter,
        ausbildungszulageSatz1          = t.AusbildungszulageSatz1,
        ausbildungszulageSatz2          = t.AusbildungszulageSatz2,
        ausbildungszulageSatz2AbAlter   = t.AusbildungszulageSatz2AbAlter,
        schwelleSatz2AnzahlKinder       = t.SchwelleSatz2AnzahlKinder,
        mindesterwerbseinkommenJahr  = t.MindesterwerbseinkommenJahr,
        mindesterwerbseinkommenMonat = t.MindesterwerbseinkommenMonat,
        geburtszulageBetrag          = t.GeburtszulageBetrag,
        adoptionszulageBetrag        = t.AdoptionszulageBetrag,
        altersGrenzeKinder          = t.AltersGrenzeKinder,
        altersGrenzeAusbildung      = t.AltersGrenzeAusbildung,
        quelle                      = t.Quelle,
        bemerkung                   = t.Bemerkung,
        isActive                    = t.IsActive,
        createdAt                   = t.CreatedAt,
        updatedAt                   = t.UpdatedAt,
    };
}

public record FamilienzulagenTarifDto(
    string?    KantonCode,
    DateOnly?  ValidFrom,
    DateOnly?  ValidTo,
    decimal?   KinderzulageSatz1,
    decimal?   KinderzulageSatz2,
    int?       KinderzulageSatz2AbAlter,
    decimal?   AusbildungszulageSatz1,
    decimal?   AusbildungszulageSatz2,
    int?       AusbildungszulageSatz2AbAlter,
    int?       SchwelleSatz2AnzahlKinder,
    decimal?   MindesterwerbseinkommenJahr,
    decimal?   MindesterwerbseinkommenMonat,
    decimal?   GeburtszulageBetrag,
    decimal?   AdoptionszulageBetrag,
    int?       AltersGrenzeKinder,
    int?       AltersGrenzeAusbildung,
    string?    Quelle,
    string?    Bemerkung,
    bool?      IsActive
);

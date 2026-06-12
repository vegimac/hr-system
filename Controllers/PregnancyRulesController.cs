using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Verwaltung des globalen Mutterschafts-Regelwerks (Walter 10.06.2026).
/// Wird vom PregnancyController zur Fristenberechnung gelesen.
///
/// Schreibrechte: admin.
/// </summary>
[ApiController]
[Route("api/pregnancy-rules")]
public class PregnancyRulesController : HrControllerBase
{
    public PregnancyRulesController(AppDbContext db) : base(db) { }

    public record PregnancyRuleDto(
        int Id, string Code, string Bezeichnung, string? Beschreibung,
        string? Gesetz, string BerechnungBasis,
        int OffsetMonate, int OffsetWochen, string Richtung,
        // Variante B — Phasen-Ende + Lohn/Staffel
        string? BasisEnde, int? OffsetEndeMonate, int? OffsetEndeWochen, string? RichtungEnde,
        decimal? LohnersatzPct, decimal? MaxBetragProTag, string? StaffelText,
        bool IstArbeitsverbot, int SortOrder, bool Aktiv);

    private static PregnancyRuleDto ToDto(PregnancyRule r) => new(
        r.Id, r.Code, r.Bezeichnung, r.Beschreibung,
        r.Gesetz, r.BerechnungBasis,
        r.OffsetMonate, r.OffsetWochen, r.Richtung,
        r.BasisEnde, r.OffsetEndeMonate, r.OffsetEndeWochen, r.RichtungEnde,
        r.LohnersatzPct, r.MaxBetragProTag, r.StaffelText,
        r.IstArbeitsverbot, r.SortOrder, r.Aktiv);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var rules = await _db.PregnancyRules
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
            .ToListAsync();
        return Ok(rules.Select(ToDto));
    }

    public record UpsertPregnancyRuleDto(
        string? Code, string? Bezeichnung, string? Beschreibung, string? Gesetz,
        string? BerechnungBasis, int? OffsetMonate, int? OffsetWochen, string? Richtung,
        string? BasisEnde, int? OffsetEndeMonate, int? OffsetEndeWochen, string? RichtungEnde,
        decimal? LohnersatzPct, decimal? MaxBetragProTag, string? StaffelText,
        bool? IstArbeitsverbot, int? SortOrder, bool? Aktiv);

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Create([FromBody] UpsertPregnancyRuleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code)
         || string.IsNullOrWhiteSpace(dto.Bezeichnung))
            return BadRequest(new { error = "Code und Bezeichnung sind Pflicht." });

        var code = dto.Code.Trim().ToUpperInvariant();
        if (await _db.PregnancyRules.AnyAsync(r => r.Code == code))
            return Conflict(new { error = $"Code '{code}' existiert bereits." });

        var rule = new PregnancyRule
        {
            Code             = code,
            Bezeichnung      = dto.Bezeichnung!.Trim(),
            Beschreibung     = dto.Beschreibung,
            Gesetz           = dto.Gesetz,
            BerechnungBasis  = (dto.BerechnungBasis ?? "ET").ToUpperInvariant(),
            OffsetMonate     = dto.OffsetMonate ?? 0,
            OffsetWochen     = dto.OffsetWochen ?? 0,
            Richtung         = (dto.Richtung ?? "VORHER").ToUpperInvariant(),
            BasisEnde        = dto.BasisEnde?.ToUpperInvariant(),
            OffsetEndeMonate = dto.OffsetEndeMonate,
            OffsetEndeWochen = dto.OffsetEndeWochen,
            RichtungEnde     = dto.RichtungEnde?.ToUpperInvariant(),
            LohnersatzPct    = dto.LohnersatzPct,
            MaxBetragProTag  = dto.MaxBetragProTag,
            StaffelText      = dto.StaffelText,
            IstArbeitsverbot = dto.IstArbeitsverbot ?? false,
            SortOrder        = dto.SortOrder ?? 99,
            Aktiv            = dto.Aktiv ?? true,
            CreatedAt        = DateTime.UtcNow
        };
        var validation = ValidateEnums(rule);
        if (validation != null) return BadRequest(new { error = validation });

        _db.PregnancyRules.Add(rule);
        await _db.SaveChangesAsync();
        return Ok(ToDto(rule));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Update(int id, [FromBody] UpsertPregnancyRuleDto dto)
    {
        var rule = await _db.PregnancyRules.FindAsync(id);
        if (rule is null) return NotFound();

        if (dto.Bezeichnung      is not null) rule.Bezeichnung      = dto.Bezeichnung;
        if (dto.Beschreibung     is not null) rule.Beschreibung     = dto.Beschreibung;
        if (dto.Gesetz           is not null) rule.Gesetz           = dto.Gesetz;
        if (dto.BerechnungBasis  is not null) rule.BerechnungBasis  = dto.BerechnungBasis.ToUpperInvariant();
        if (dto.OffsetMonate     is not null) rule.OffsetMonate     = dto.OffsetMonate.Value;
        if (dto.OffsetWochen     is not null) rule.OffsetWochen     = dto.OffsetWochen.Value;
        if (dto.Richtung         is not null) rule.Richtung         = dto.Richtung.ToUpperInvariant();
        if (dto.IstArbeitsverbot is not null) rule.IstArbeitsverbot = dto.IstArbeitsverbot.Value;
        if (dto.SortOrder        is not null) rule.SortOrder        = dto.SortOrder.Value;
        if (dto.Aktiv            is not null) rule.Aktiv            = dto.Aktiv.Value;

        // Variante B: Phasen-Ende + Lohn/Staffel. Leerer String → NULL (Feld
        // explizit löschen). null im DTO → unverändert lassen.
        if (dto.BasisEnde        is not null) rule.BasisEnde        = string.IsNullOrWhiteSpace(dto.BasisEnde) ? null : dto.BasisEnde.ToUpperInvariant();
        if (dto.OffsetEndeMonate is not null) rule.OffsetEndeMonate = dto.OffsetEndeMonate;
        if (dto.OffsetEndeWochen is not null) rule.OffsetEndeWochen = dto.OffsetEndeWochen;
        if (dto.RichtungEnde     is not null) rule.RichtungEnde     = string.IsNullOrWhiteSpace(dto.RichtungEnde) ? null : dto.RichtungEnde.ToUpperInvariant();
        if (dto.LohnersatzPct    is not null) rule.LohnersatzPct    = dto.LohnersatzPct;
        if (dto.MaxBetragProTag  is not null) rule.MaxBetragProTag  = dto.MaxBetragProTag;
        if (dto.StaffelText      is not null) rule.StaffelText      = string.IsNullOrWhiteSpace(dto.StaffelText) ? null : dto.StaffelText;

        var validation = ValidateEnums(rule);
        if (validation != null) return BadRequest(new { error = validation });

        await _db.SaveChangesAsync();
        return Ok(ToDto(rule));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var rule = await _db.PregnancyRules.FindAsync(id);
        if (rule is null) return NotFound();
        _db.PregnancyRules.Remove(rule);
        await _db.SaveChangesAsync();
        return Ok();
    }

    private static string? ValidateEnums(PregnancyRule r)
    {
        if (r.BerechnungBasis is not ("ET" or "GEBURT" or "MELDUNG"))
            return "berechnungBasis muss ET, GEBURT oder MELDUNG sein.";
        if (r.Richtung is not ("VORHER" or "NACHHER"))
            return "richtung muss VORHER oder NACHHER sein.";
        if (r.BasisEnde != null && r.BasisEnde is not ("ET" or "GEBURT" or "MELDUNG"))
            return "basisEnde muss ET, GEBURT, MELDUNG oder leer sein.";
        if (r.RichtungEnde != null && r.RichtungEnde is not ("VORHER" or "NACHHER"))
            return "richtungEnde muss VORHER, NACHHER oder leer sein.";
        return null;
    }
}

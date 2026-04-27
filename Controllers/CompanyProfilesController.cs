using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompanyProfilesController : ControllerBase
{
    private readonly AppDbContext _context;

    public CompanyProfilesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        // Einheitliche Sortierung für ALLE Stellen, an denen Filialen
        // gelistet werden (Dashboard, Filialen-Page, Branch-Selektor,
        // Lohn-Page, Zuweisungs-Dropdowns). Primär nach Restaurant-Code
        // numerisch, sekundär nach Branch-/Firmenname.
        var profiles = await _context.CompanyProfiles
            .ToListAsync();

        profiles = profiles
            .OrderBy(p => RestaurantCodeSortKey(p.RestaurantCode))
            .ThenBy(p => p.RestaurantCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.BranchName ?? p.CompanyName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(profiles);
    }

    /// <summary>
    /// Restaurant-Codes sind i.d.R. numerisch (z.B. "101", "205"). Falls
    /// ein Code nicht als Zahl parsbar ist (oder fehlt), wandert er ans
    /// Ende — sekundär alphabetisch sortiert.
    /// </summary>
    private static int RestaurantCodeSortKey(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return int.MaxValue;
        return int.TryParse(code.Trim(), out var n) ? n : int.MaxValue;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var profile = await _context.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == id);

        if (profile == null)
            return NotFound();

        return Ok(profile);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CompanyProfile profile)
    {
        _context.CompanyProfiles.Add(profile);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, profile);
    }

    // PATCH /api/companyprofiles/{id}/nighthours
    [HttpPatch("{id:int}/nighthours")]
    public async Task<IActionResult> UpdateNightHours(int id, [FromBody] NightHoursDto dto)
    {
        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.NightStartTime = dto.NightStartTime;
        profile.NightEndTime   = dto.NightEndTime;
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record NightHoursDto(string NightStartTime, string NightEndTime);

    // PATCH /api/companyprofiles/{id}/alv
    [HttpPatch("{id:int}/alv")]
    public async Task<IActionResult> UpdateAlvDaten(int id, [FromBody] AlvDatenDto dto)
    {
        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.BurNummer      = dto.BurNummer;
        profile.BranchenCode   = dto.BranchenCode;
        profile.AhvKasse       = dto.AhvKasse;
        profile.BvgVersicherer = dto.BvgVersicherer;
        profile.IstGav         = dto.IstGav;
        profile.GavName        = dto.GavName;
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record AlvDatenDto(
        string? BurNummer,
        string? BranchenCode,
        string? AhvKasse,
        string? BvgVersicherer,
        bool    IstGav,
        string? GavName
    );

    // PATCH /api/companyprofiles/{id}/thirteenth-payouts
    [HttpPatch("{id:int}/thirteenth-payouts")]
    public async Task<IActionResult> UpdateThirteenthPayouts(int id, [FromBody] ThirteenthPayoutsDto dto)
    {
        if (dto.PayoutsPerYear != 12 && dto.PayoutsPerYear != 4
            && dto.PayoutsPerYear != 2 && dto.PayoutsPerYear != 1)
            return BadRequest(new { message = "Erlaubte Werte: 12, 4, 2 oder 1." });

        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.ThirteenthMonthPayoutsPerYear = dto.PayoutsPerYear;
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record ThirteenthPayoutsDto(int PayoutsPerYear);

    // PATCH /api/companyprofiles/{id}/auto-ferien-geld-dezember
    // Schaltet die automatische Jahresend-Auszahlung des Ferien-Geld-Saldos
    // (UTP/MTP) im Dezember an oder aus.
    [HttpPatch("{id:int}/auto-ferien-geld-dezember")]
    public async Task<IActionResult> UpdateAutoFerienGeldDezember(int id, [FromBody] AutoFerienGeldDezemberDto dto)
    {
        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();
        profile.AutoFerienGeldAuszahlungDezember = dto.Aktiv;
        await _context.SaveChangesAsync();
        return Ok(profile);
    }

    public record AutoFerienGeldDezemberDto(bool Aktiv);

    // PATCH /api/companyprofiles/{id}/lgav
    [HttpPatch("{id:int}/lgav")]
    public async Task<IActionResult> UpdateLgav(int id, [FromBody] LgavDto dto)
    {
        if (dto.LgavTriggerMonat < 1 || dto.LgavTriggerMonat > 12)
            return BadRequest(new { message = "LgavTriggerMonat muss zwischen 1 und 12 liegen." });
        if (dto.LgavBeitragVoll      < 0 || dto.LgavBeitragVoll      > 9999m)
            return BadRequest(new { message = "LgavBeitragVoll muss zwischen 0 und 9999 liegen." });
        if (dto.LgavBeitragReduziert < 0 || dto.LgavBeitragReduziert > 9999m)
            return BadRequest(new { message = "LgavBeitragReduziert muss zwischen 0 und 9999 liegen." });

        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.LgavAktiv            = dto.LgavAktiv;
        profile.LgavTriggerMonat     = dto.LgavTriggerMonat;
        profile.LgavBeitragVoll      = Math.Round(dto.LgavBeitragVoll,      2);
        profile.LgavBeitragReduziert = Math.Round(dto.LgavBeitragReduziert, 2);
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record LgavDto(
        bool    LgavAktiv,
        int     LgavTriggerMonat,
        decimal LgavBeitragVoll,
        decimal LgavBeitragReduziert);

    // PATCH /api/companyprofiles/{id}/karenz
    [HttpPatch("{id:int}/karenz")]
    public async Task<IActionResult> UpdateKarenz(int id, [FromBody] KarenzDto dto)
    {
        var basis = (dto.KarenzjahrBasis ?? "ARBEITSJAHR").Trim().ToUpperInvariant();
        if (basis != "ARBEITSJAHR" && basis != "KALENDERJAHR")
            return BadRequest(new { message = "Erlaubte Werte für KarenzjahrBasis: ARBEITSJAHR oder KALENDERJAHR." });

        if (dto.KarenzTageMax < 0 || dto.KarenzTageMax > 365)
            return BadRequest(new { message = "KarenzTageMax (Krank) muss zwischen 0 und 365 liegen." });

        // Unfall-Tage: wenn nicht mitgeliefert, bestehenden Wert (bzw. Default 2) beibehalten.
        if (dto.KarenzTageMaxUnfall.HasValue &&
            (dto.KarenzTageMaxUnfall.Value < 0 || dto.KarenzTageMaxUnfall.Value > 365))
            return BadRequest(new { message = "KarenzTageMaxUnfall muss zwischen 0 und 365 liegen." });

        if (dto.BvgWartefristMonate.HasValue &&
            (dto.BvgWartefristMonate.Value < 0 || dto.BvgWartefristMonate.Value > 24))
            return BadRequest(new { message = "BvgWartefristMonate muss zwischen 0 und 24 liegen." });

        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.KarenzjahrBasis            = basis;
        profile.KarenzTageMax              = Math.Round(dto.KarenzTageMax, 2);
        if (dto.KarenzTageMaxUnfall.HasValue)
            profile.KarenzTageMaxUnfall    = Math.Round(dto.KarenzTageMaxUnfall.Value, 2);
        if (dto.BvgWartefristMonate.HasValue)
            profile.BvgWartefristMonate    = dto.BvgWartefristMonate.Value;
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record KarenzDto(
        string?  KarenzjahrBasis,
        decimal  KarenzTageMax,
        decimal? KarenzTageMaxUnfall,
        int?     BvgWartefristMonate);
}
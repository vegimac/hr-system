using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Feiertage pro Filiale (Walter 07.10.2026) — Pflege nur noch im Filial-Detail
/// «Arbeitszeit &amp; Feiertage»; der Manager-Dienstplan zeigt sie nur noch an.
/// Tabelle dienstplan_feiertag (national / Kanton / Filiale), dazu «dem Sonntag gleichgestellt».
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superuser,user")]
[Route("api/feiertage")]
public class FeiertageController : HrControllerBase
{
    public FeiertageController(AppDbContext db) : base(db) { }

    record Filiale(int Id, string? KantonCode, string Name);

    IActionResult? NurAdminBeiNational(string scope) =>
        scope == "NATIONAL" && !User.IsInRole("admin")
            ? StatusCode(403, new { error = "NUR_ADMIN", message = "Nationale Feiertage gelten für alle Filialen und können nur von einem Admin geändert werden." })
            : null;

    async Task<(IActionResult? Fehler, Filiale? F)> FilialeAsync(int cp)
    {
        if (cp <= 0) return (BadRequest(new { error = "FILIALE_FEHLT", message = "Filiale fehlt." }), null);
        if (!await CanAccessBranchAsync(cp)) return (Forbid(), null);
        var c = await _db.CompanyProfiles.AsNoTracking().Where(x => x.Id == cp)
            .Select(x => new { x.Id, x.KantonCode, x.BranchName, x.RestaurantCode }).FirstOrDefaultAsync();
        if (c == null) return (NotFound(new { error = "FILIALE_NICHT_GEFUNDEN", message = "Filiale nicht gefunden." }), null);
        return (null, new Filiale(c.Id, string.IsNullOrWhiteSpace(c.KantonCode) ? null : c.KantonCode.Trim().ToUpperInvariant(),
            $"{c.RestaurantCode} {c.BranchName}".Trim()));
    }

    async Task<List<DienstplanFeiertag>> GueltigAsync(Filiale f, int jahr)
    {
        var von = new DateOnly(jahr, 1, 1);
        var bis = new DateOnly(jahr, 12, 31);
        return (await _db.DienstplanFeiertage.AsNoTracking()
                .Where(x => x.Datum >= von && x.Datum <= bis).ToListAsync())
            .Where(x => FeiertagVorschlag.GiltFuer(x, f.Id, f.KantonCode))
            .OrderBy(x => x.Datum).ToList();
    }

    [HttpGet]
    public async Task<IActionResult> Liste([FromQuery] int companyProfileId, [FromQuery] int? year)
    {
        var (fehler, f) = await FilialeAsync(companyProfileId);
        if (fehler != null) return fehler;
        int jahr = year ?? DateTime.Today.Year;
        var liste = await GueltigAsync(f!, jahr);
        return Ok(new
        {
            kanton = f!.KantonCode,
            filiale = f.Name,
            jahr,
            feiertage = liste.Select(x => new
            {
                x.Id,
                datum = x.Datum.ToString("yyyy-MM-dd"),
                x.Bezeichnung,
                x.Scope,
                x.KantonCode,
                sonntagsgleich = FeiertagVorschlag.IstSonntagsgleich(x),
                sonntagsgleichFest = x.Datum.Month == 8 && x.Datum.Day == 1,
            }),
        });
    }

    public record NeuDto(int CompanyProfileId, string? Datum, string? Bezeichnung, string? Scope, bool Sonntagsgleich);

    [Authorize(Roles = "admin,superuser")]
    [HttpPost]
    public async Task<IActionResult> Neu([FromBody] NeuDto dto)
    {
        var (fehler, f) = await FilialeAsync(dto.CompanyProfileId);
        if (fehler != null) return fehler;
        if (!DateOnly.TryParse(dto.Datum, out var datum))
            return BadRequest(new { error = "DATUM_UNGUELTIG", message = "Datum fehlt." });
        if (string.IsNullOrWhiteSpace(dto.Bezeichnung))
            return BadRequest(new { error = "BEZEICHNUNG_FEHLT", message = "Bezeichnung fehlt." });
        var (pruef, eintrag) = Eintrag(f!, datum, dto.Bezeichnung, dto.Scope, dto.Sonntagsgleich);
        if (pruef != null) return pruef;
        if (NurAdminBeiNational(eintrag!.Scope) is { } nurAdmin) return nurAdmin;
        if ((await GueltigAsync(f!, datum.Year)).Any(x => x.Datum == datum))
            return Conflict(new { error = "SCHON_ERFASST", message = $"Am {datum:dd.MM.yyyy} ist für diese Filiale schon ein Feiertag erfasst." });
        _db.DienstplanFeiertage.Add(eintrag!);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, id = eintrag!.Id });
    }

    (IActionResult?, DienstplanFeiertag?) Eintrag(Filiale f, DateOnly datum, string bezeichnung, string? scopeRoh, bool sonntagsgleich)
    {
        var scope = (scopeRoh ?? "KANTON").ToUpperInvariant();
        if (scope is not ("NATIONAL" or "KANTON" or "FILIALE"))
            return (BadRequest(new { error = "SCOPE_UNGUELTIG", message = "Geltung ungültig." }), null);
        if (scope == "KANTON" && f.KantonCode == null)
            return (BadRequest(new { error = "KANTON_FEHLT", message = "Für diese Filiale ist kein Kanton in den Stammdaten erfasst." }), null);
        return (null, new DienstplanFeiertag
        {
            Datum = datum,
            Bezeichnung = bezeichnung.Trim(),
            Scope = scope,
            KantonCode = scope == "KANTON" ? f.KantonCode : null,
            CompanyProfileId = scope == "FILIALE" ? f.Id : null,
            Sonntagsgleich = sonntagsgleich || (datum.Month == 8 && datum.Day == 1),
            CreatedAt = DateTime.Now,
        });
    }

    public record AendernDto(int CompanyProfileId, string? Bezeichnung, bool? Sonntagsgleich);

    [Authorize(Roles = "admin,superuser")]
    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Aendern(int id, [FromBody] AendernDto dto)
    {
        var (fehler, f) = await FilialeAsync(dto.CompanyProfileId);
        if (fehler != null) return fehler;
        var x = await _db.DienstplanFeiertage.FindAsync(id);
        if (x == null || !FeiertagVorschlag.GiltFuer(x, f!.Id, f.KantonCode)) return NotFound();
        if (NurAdminBeiNational(x.Scope) is { } nurAdmin) return nurAdmin;
        if (!string.IsNullOrWhiteSpace(dto.Bezeichnung)) x.Bezeichnung = dto.Bezeichnung.Trim();
        if (dto.Sonntagsgleich.HasValue) x.Sonntagsgleich = dto.Sonntagsgleich.Value || (x.Datum.Month == 8 && x.Datum.Day == 1);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [Authorize(Roles = "admin,superuser")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Loeschen(int id, [FromQuery] int companyProfileId)
    {
        var (fehler, f) = await FilialeAsync(companyProfileId);
        if (fehler != null) return fehler;
        var x = await _db.DienstplanFeiertage.FindAsync(id);
        if (x == null || !FeiertagVorschlag.GiltFuer(x, f!.Id, f.KantonCode)) return NotFound();
        if (NurAdminBeiNational(x.Scope) is { } nurAdmin) return nurAdmin;
        _db.DienstplanFeiertage.Remove(x);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpGet("vorschlag")]
    public async Task<IActionResult> Vorschlag([FromQuery] int companyProfileId, [FromQuery] int year)
    {
        var (fehler, f) = await FilialeAsync(companyProfileId);
        if (fehler != null) return fehler;
        if (year < 2000 || year > 2100) return BadRequest(new { error = "JAHR", message = "Jahr ungültig." });
        var erfasst = (await GueltigAsync(f!, year)).Select(x => x.Datum).ToHashSet();
        return Ok(new
        {
            kanton = f!.KantonCode,
            jahr = year,
            kandidaten = FeiertagVorschlag.Fuer(year, f.KantonCode).Select(k => new
            {
                datum = k.Datum.ToString("yyyy-MM-dd"),
                k.Bezeichnung,
                vorgewaehlt = k.Vorgewaehlt && !erfasst.Contains(k.Datum),
                k.Sonntagsgleich,
                scope = k.National ? "NATIONAL" : f.KantonCode != null ? "KANTON" : "FILIALE",
                schonErfasst = erfasst.Contains(k.Datum),
            }),
        });
    }

    public record UebernehmenDto(int CompanyProfileId, List<NeuDto>? Eintraege);

    [Authorize(Roles = "admin,superuser")]
    [HttpPost("uebernehmen")]
    public async Task<IActionResult> Uebernehmen([FromBody] UebernehmenDto dto)
    {
        var (fehler, f) = await FilialeAsync(dto.CompanyProfileId);
        if (fehler != null) return fehler;
        int neu = 0, uebersprungen = 0;
        var jahre = new Dictionary<int, HashSet<DateOnly>>();
        foreach (var e in dto.Eintraege ?? new())
        {
            if (!DateOnly.TryParse(e.Datum, out var datum) || string.IsNullOrWhiteSpace(e.Bezeichnung)) { uebersprungen++; continue; }
            if (!jahre.TryGetValue(datum.Year, out var erfasst))
                jahre[datum.Year] = erfasst = (await GueltigAsync(f!, datum.Year)).Select(x => x.Datum).ToHashSet();
            if (erfasst.Contains(datum)) { uebersprungen++; continue; }
            var scope = e.Scope?.ToUpperInvariant() == "NATIONAL" && !User.IsInRole("admin")
                ? (f!.KantonCode != null ? "KANTON" : "FILIALE")
                : e.Scope;
            var (pruef, eintrag) = Eintrag(f!, datum, e.Bezeichnung, scope, e.Sonntagsgleich);
            if (pruef != null) return pruef;
            _db.DienstplanFeiertage.Add(eintrag!);
            erfasst.Add(datum);
            neu++;
        }
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, neu, uebersprungen });
    }
}

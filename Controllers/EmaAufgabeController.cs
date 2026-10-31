using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Vorbereitete EMA-Zeilen BVG und Quellensteuer (Walter 30.10.2026).
/// Der Anwender gibt die erkannte Zeile zur Übermittlung frei. Die ELM-Datei ist Etappe E7.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/ema-aufgabe")]
[MaEintrag(typeof(EmaFreigabe))]
public class EmaAufgabeController : HrControllerBase
{
    public EmaAufgabeController(AppDbContext db) : base(db) { }

    public sealed record EintragDto(int EmployeeId, string? Grund, DateOnly Datum);
    public sealed record FreigabeDto(int CompanyProfileId, string Domaene, List<EintragDto> Eintraege);

    [HttpGet("preview")]
    public async Task<IActionResult> Preview([FromQuery] int companyProfileId)
    {
        if (companyProfileId <= 0) return BadRequest(new { error = "Filiale fehlt." });
        if (!await CanAccessBranchAsync(companyProfileId)) return Forbid();
        var zeilen = await EmaAufgabeService.LadenAsync(_db, companyProfileId);
        object Map(EmaAufgabeService.Zeile z) => new
        {
            z.Aufgabe.EmployeeId,
            vorname = z.Vorname,
            name = z.Name,
            domaene = z.Aufgabe.Domaene,
            art = z.Aufgabe.Art,
            grund = z.Aufgabe.Grund,
            text = z.Aufgabe.Text,
            zusatz = z.Aufgabe.Zusatz,
            datum = z.Aufgabe.Stichtag.ToString("dd.MM.yyyy"),
            datumIso = z.Aufgabe.Stichtag.ToString("yyyy-MM-dd"),
            gemeldet = z.FreigabeId == null ? null : new { id = z.FreigabeId, am = z.FreigegebenAm, von = z.FreigegebenVon },
        };
        return Ok(new
        {
            bvg = zeilen.Where(z => z.Aufgabe.Domaene == EmaAufgabeRegel.Bvg).Select(Map),
            qst = zeilen.Where(z => z.Aufgabe.Domaene == EmaAufgabeRegel.Qst).Select(Map),
        });
    }

    [HttpPost("freigeben")]
    public async Task<IActionResult> Freigeben([FromBody] FreigabeDto dto)
    {
        if (dto.Domaene is not (EmaAufgabeRegel.Bvg or EmaAufgabeRegel.Qst))
            return BadRequest(new { error = "Domäne fehlt (BVG oder QST)." });
        if (dto.CompanyProfileId <= 0) return BadRequest(new { error = "Filiale fehlt." });
        if (!await CanAccessBranchAsync(dto.CompanyProfileId)) return Forbid();
        var eintraege = dto.Eintraege ?? new();
        if (eintraege.Count == 0) return Ok(new { neu = 0, schon = 0, ungueltig = 0 });

        var zeilen = await EmaAufgabeService.LadenAsync(_db, dto.CompanyProfileId);
        var uid = GetCurrentUserId();
        var wer = await ActorNameAsync(uid);
        int neu = 0, schon = 0, ungueltig = 0;
        foreach (var e in eintraege)
        {
            var zeile = zeilen.FirstOrDefault(z => z.Aufgabe.EmployeeId == e.EmployeeId
                && z.Aufgabe.Domaene == dto.Domaene && z.Aufgabe.Grund == e.Grund && z.Aufgabe.Stichtag == e.Datum);
            if (zeile == null) { ungueltig++; continue; }
            if (zeile.FreigabeId != null) { schon++; continue; }
            _db.EmaFreigaben.Add(new EmaFreigabe
            {
                EmployeeId = e.EmployeeId,
                CompanyProfileId = dto.CompanyProfileId,
                Domaene = dto.Domaene,
                Grund = zeile.Aufgabe.Grund,
                Stichtag = zeile.Aufgabe.Stichtag,
                ErfasstAm = DateTime.Now,
                ErfasstVonUserId = uid,
                ErfasstVon = wer,
            });
            neu++;
        }
        await _db.SaveChangesAsync();
        return Ok(new { neu, schon, ungueltig });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Loeschen(int id)
    {
        var m = await _db.EmaFreigaben.FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return NotFound();
        if (!await CanAccessBranchAsync(m.CompanyProfileId)) return Forbid();
        _db.EmaFreigaben.Remove(m);
        await _db.SaveChangesAsync();
        return Ok(new { geloescht = 1 });
    }

    private async Task<string?> ActorNameAsync(int? uid)
    {
        if (uid != null)
        {
            var u = await _db.AppUsers.Where(x => x.Id == uid.Value)
                .Select(x => new { x.FirstName, x.LastName, x.Username })
                .FirstOrDefaultAsync();
            if (u != null)
            {
                var full = $"{u.FirstName} {u.LastName}".Trim();
                return string.IsNullOrWhiteSpace(full) ? u.Username : full;
            }
        }
        return User.Identity?.Name;
    }
}

using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HrSystem.Controllers;

/// <summary>
/// BVG-Versicherungspflicht pro Person (Walter 04.10.2026): versichert ja/nein ab/bis.
/// GET liefert zusätzlich den Vorschlag (Vertrag bzw. Lohnmonate, inkl. 13. ML).
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/bvg-pflicht")]
[Authorize(Roles = "admin,superuser,user")]
public class EmployeeBvgPflichtController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LohnEditLockService _editLock;
    public EmployeeBvgPflichtController(AppDbContext db, LohnEditLockService editLock)
    { _db = db; _editLock = editLock; }

    public record UpsertDto(bool Versichert, DateOnly GueltigAb, DateOnly? GueltigBis,
                            string? Quelle, decimal? Jahreslohn, string? Bemerkung);

    [HttpGet]
    public async Task<IActionResult> List(int employeeId, [FromQuery] DateOnly? stichtag)
    {
        var tag = stichtag ?? DateOnly.FromDateTime(DateTime.Today);
        var eintraege = await _db.EmployeeBvgPflichten.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .OrderByDescending(e => e.GueltigAb)
            .ToListAsync();
        var aktuell = eintraege.FirstOrDefault(e => e.Ueberlappt(tag, tag));
        var vorschlag = await new BvgPflichtService(_db).VorschlagAsync(employeeId, tag);
        return Ok(new
        {
            stichtag = tag,
            aktuell = aktuell == null ? null : new { aktuell.Id, aktuell.Versichert, aktuell.GueltigAb, aktuell.GueltigBis, aktuell.Quelle },
            vorschlag = vorschlag == null ? null : new
            {
                vorschlag.Versichert, vorschlag.Jahreslohn, vorschlag.Grundlage, vorschlag.Schwelle,
                abweichend = vorschlag.Versichert != null && vorschlag.Versichert != aktuell?.Versichert,
            },
            eintraege = eintraege.Select(e => new { e.Id, e.Versichert, e.GueltigAb, e.GueltigBis, e.Quelle,
                                                     e.Jahreslohn, e.Bemerkung, e.CreatedAt, isCurrent = e.Ueberlappt(tag, tag) }),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, [FromBody] UpsertDto dto)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId)) return NotFound();
        var fehler = Pruefe(dto);
        if (fehler != null) return BadRequest(new { error = "UNGUELTIG", message = fehler });
        var gesperrt = await PruefeEditLockAsync(employeeId, dto.GueltigAb);
        if (gesperrt != null) return gesperrt;

        if (await _db.EmployeeBvgPflichten.AnyAsync(e => e.EmployeeId == employeeId && e.GueltigAb == dto.GueltigAb))
            return Conflict(new { error = "BEREITS_AB_DATUM", message = $"Ab {dto.GueltigAb:dd.MM.yyyy} gibt es schon einen Eintrag — bitte diesen bearbeiten." });

        var offene = await _db.EmployeeBvgPflichten
            .Where(e => e.EmployeeId == employeeId && e.GueltigAb < dto.GueltigAb
                     && (e.GueltigBis == null || e.GueltigBis >= dto.GueltigAb))
            .ToListAsync();
        foreach (var o in offene) o.GueltigBis = dto.GueltigAb.AddDays(-1);

        var e = new EmployeeBvgPflicht
        {
            EmployeeId = employeeId, CreatedAt = DateTime.Now, CreatedBy = GetCurrentUserId(),
        };
        Uebernehme(e, dto);
        _db.EmployeeBvgPflichten.Add(e);
        await _db.SaveChangesAsync();
        return Ok(new { id = e.Id, vorgaengerBeendet = offene.Count });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int employeeId, int id, [FromBody] UpsertDto dto)
    {
        var e = await _db.EmployeeBvgPflichten.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId);
        if (e == null) return NotFound();
        var fehler = Pruefe(dto);
        if (fehler != null) return BadRequest(new { error = "UNGUELTIG", message = fehler });
        var gesperrt = await PruefeEditLockAsync(employeeId, dto.GueltigAb < e.GueltigAb ? dto.GueltigAb : e.GueltigAb);
        if (gesperrt != null) return gesperrt;
        Uebernehme(e, dto);
        await _db.SaveChangesAsync();
        return Ok(new { id = e.Id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int employeeId, int id)
    {
        var e = await _db.EmployeeBvgPflichten.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId);
        if (e == null) return NotFound();
        var gesperrt = await PruefeEditLockAsync(employeeId, e.GueltigAb);
        if (gesperrt != null) return gesperrt;
        _db.EmployeeBvgPflichten.Remove(e);
        await _db.SaveChangesAsync();
        return Ok();
    }

    private static readonly string[] Quellen =
        { EmployeeBvgPflicht.QuelleHand, EmployeeBvgPflicht.QuelleVorschlag, EmployeeBvgPflicht.QuelleMirus };

    private static void Uebernehme(EmployeeBvgPflicht e, UpsertDto dto)
    {
        e.Versichert = dto.Versichert;
        e.GueltigAb = dto.GueltigAb;
        e.GueltigBis = dto.GueltigBis;
        var q = (dto.Quelle ?? "").Trim().ToUpperInvariant();
        e.Quelle = Quellen.Contains(q) ? q : EmployeeBvgPflicht.QuelleHand;
        e.Jahreslohn = dto.Jahreslohn is > 0 ? Math.Round(dto.Jahreslohn.Value, 2) : null;
        e.Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
    }

    private static string? Pruefe(UpsertDto dto)
    {
        if (dto.GueltigAb == default) return "«Gültig ab» fehlt.";
        if (dto.GueltigBis != null && dto.GueltigBis < dto.GueltigAb) return "«Gültig bis» liegt vor «Gültig ab».";
        return null;
    }

    private async Task<IActionResult?> PruefeEditLockAsync(int employeeId, DateOnly gueltigAb)
    {
        var branchId = await _db.Employments
            .Where(e => e.EmployeeId == employeeId && e.IsActive)
            .OrderByDescending(e => e.ContractStartDate)
            .Select(e => (int?)e.CompanyProfileId)
            .FirstOrDefaultAsync();
        var firstAllowed = branchId.HasValue ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value) : null;
        if (firstAllowed.HasValue && gueltigAb < firstAllowed.Value)
            return Conflict(new
            {
                error = "LOHN_EDIT_LOCKED",
                message = $"«Gültig ab {gueltigAb:dd.MM.yyyy}» liegt in einer bereits in Verarbeitung befindlichen Lohnperiode. Frühestes erlaubtes «Gültig ab»: {firstAllowed.Value:dd.MM.yyyy}.",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });
        return null;
    }

    private int? GetCurrentUserId()
    {
        var s = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(s, out var id) ? id : null;
    }
}

using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Zivilstand-Historie pro MA (Walter 04.09.2026) — Liste, Stand am Stichtag,
/// Einträge ergänzen/korrigieren/löschen (reine Stammdaten-Historie, keine
/// Lohndaten; die QST-Versionen bleiben unberührt).
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/employees/{employeeId:int}/zivilstand")]
public class EmployeeZivilstandController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ZivilstandHistorieService _svc;
    public EmployeeZivilstandController(AppDbContext db, ZivilstandHistorieService svc) { _db = db; _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> List(int employeeId)
    {
        var list = await _db.EmployeeZivilstandHistories.AsNoTracking()
            .Where(h => h.EmployeeId == employeeId)
            .OrderBy(h => h.GueltigAb == null ? 0 : 1).ThenBy(h => h.GueltigAb).ThenBy(h => h.Id)
            .ToListAsync();
        var result = new List<object>();
        for (int i = 0; i < list.Count; i++)
        {
            var h = list[i];
            DateOnly? bis = (i + 1 < list.Count && list[i + 1].GueltigAb.HasValue) ? list[i + 1].GueltigAb!.Value.AddDays(-1) : null;
            result.Add(new { h.Id, zivilstand = h.Zivilstand, gueltigAb = h.GueltigAb?.ToString("yyyy-MM-dd"), gueltigBis = bis?.ToString("yyyy-MM-dd"), h.Bemerkung });
        }
        return Ok(result);
    }

    [HttpGet("am")]
    public async Task<IActionResult> Am(int employeeId, [FromQuery] string? datum)
    {
        var d = DateOnly.TryParse(datum, out var x) ? x : DateOnly.FromDateTime(DateTime.Today);
        var (z, seit, ausHist) = await _svc.AmAsync(employeeId, d);
        return Ok(new { zivilstand = z, seit = seit?.ToString("yyyy-MM-dd"), ausHistorie = ausHist, stichtag = d.ToString("yyyy-MM-dd") });
    }

    public sealed class EntryDto { public string? Zivilstand { get; set; } public string? GueltigAb { get; set; } public string? Bemerkung { get; set; } }

    [HttpPost]
    public async Task<IActionResult> Add(int employeeId, [FromBody] EntryDto dto)
    {
        var z = ZivilstandHistorieService.Norm(dto.Zivilstand);
        if (z.Length == 0) return BadRequest(new { error = "ZIVILSTAND_FEHLT" });
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId)) return NotFound();
        DateOnly? ab = null;
        if (!string.IsNullOrWhiteSpace(dto.GueltigAb))
        {
            if (!DateOnly.TryParse(dto.GueltigAb, out var d)) return BadRequest(new { error = "DATUM_UNGUELTIG" });
            ab = d;
        }
        var gleich = await _db.EmployeeZivilstandHistories.FirstOrDefaultAsync(h => h.EmployeeId == employeeId && h.GueltigAb == ab);
        if (gleich != null) { gleich.Zivilstand = z; gleich.Bemerkung = dto.Bemerkung?.Trim(); }
        else _db.EmployeeZivilstandHistories.Add(new EmployeeZivilstandHistory { EmployeeId = employeeId, Zivilstand = z, GueltigAb = ab, Bemerkung = dto.Bemerkung?.Trim() });
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int employeeId, int id, [FromBody] EntryDto dto)
    {
        var h = await _db.EmployeeZivilstandHistories.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId);
        if (h == null) return NotFound();
        var z = ZivilstandHistorieService.Norm(dto.Zivilstand);
        if (z.Length > 0) h.Zivilstand = z;
        if (dto.GueltigAb != null)
        {
            if (dto.GueltigAb == "") h.GueltigAb = null;
            else if (DateOnly.TryParse(dto.GueltigAb, out var d)) h.GueltigAb = d;
            else return BadRequest(new { error = "DATUM_UNGUELTIG" });
        }
        if (dto.Bemerkung != null) h.Bemerkung = dto.Bemerkung.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int employeeId, int id)
    {
        var h = await _db.EmployeeZivilstandHistories.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId);
        if (h == null) return NotFound();
        _db.EmployeeZivilstandHistories.Remove(h);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}

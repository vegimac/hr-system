using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/ahv-beitragspflicht")]
[Authorize]
public class AhvBeitragspflichtController : ControllerBase
{
    private readonly AppDbContext _db;
    public AhvBeitragspflichtController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var kopf = await _db.AhvBeitragspflichten.AsNoTracking().FirstOrDefaultAsync(a => a.Id == 1);
        var rows = await _db.AhvBeitragspflichtUebergaenge.AsNoTracking()
            .OrderBy(u => u.JahrgangVon ?? int.MinValue).ThenBy(u => u.JahrgangBis ?? int.MaxValue)
            .ToListAsync();
        if (kopf == null)
        {
            var g = AhvBeitragspflichtStand.Gesetzlich;
            return Ok(new
            {
                beginn = g.Beginn,
                endeMaenner = g.EndeMaenner,
                endeFrauen = g.EndeFrauen,
                uebergaenge = g.Uebergaenge.Select(u => new { von = u.Von, bis = u.Bis, jahre = u.Jahre, monate = u.Monate }),
            });
        }
        return Ok(new
        {
            beginn = kopf.BeginnAlter,
            endeMaenner = kopf.EndeMaenner,
            endeFrauen = kopf.EndeFrauen,
            uebergaenge = rows.Select(u => new { von = u.JahrgangVon, bis = u.JahrgangBis, jahre = u.Jahre, monate = u.Monate }),
        });
    }

    [HttpPut]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Put([FromBody] AhvBeitragspflichtDto dto)
    {
        var fehler = Pruefe(dto);
        if (fehler != null)
            return BadRequest(new { error = "AHV_BEITRAGSPFLICHT", message = fehler });

        var kopf = await _db.AhvBeitragspflichten.FirstOrDefaultAsync(a => a.Id == 1);
        if (kopf == null)
        {
            kopf = new AhvBeitragspflicht { Id = 1 };
            _db.AhvBeitragspflichten.Add(kopf);
        }
        kopf.BeginnAlter = dto.Beginn;
        kopf.EndeMaenner = dto.EndeMaenner;
        kopf.EndeFrauen = dto.EndeFrauen;
        kopf.GeaendertAm = DateTime.Now;

        var alt = await _db.AhvBeitragspflichtUebergaenge.ToListAsync();
        _db.AhvBeitragspflichtUebergaenge.RemoveRange(alt);
        foreach (var z in dto.Uebergaenge)
        {
            _db.AhvBeitragspflichtUebergaenge.Add(new AhvBeitragspflichtUebergang
            {
                JahrgangVon = z.Von,
                JahrgangBis = z.Bis,
                Jahre = z.Jahre,
                Monate = z.Monate,
            });
        }
        await _db.SaveChangesAsync();
        return await Get();
    }

    private static string? Pruefe(AhvBeitragspflichtDto dto)
    {
        if (dto.Beginn is < 1 or > 99)
            return "Beginn der Beitragspflicht liegt zwischen 1 und 99.";
        if (dto.EndeMaenner is < 1 or > 99 || dto.EndeMaenner <= dto.Beginn)
            return "Ende der Beitragspflicht für Männer muss nach dem Beginn liegen.";
        if (dto.EndeFrauen is < 1 or > 99 || dto.EndeFrauen <= dto.Beginn)
            return "Ende der Beitragspflicht für Frauen muss nach dem Beginn liegen.";

        var zeilen = dto.Uebergaenge ?? new List<AhvUebergangDto>();
        var spannen = new List<(int Von, int Bis)>();
        foreach (var z in zeilen)
        {
            if (z.Jahre is < 1 or > 99 || z.Monate is < 0 or > 11)
                return "Übergang: Jahre zwischen 1 und 99, Monate zwischen 0 und 11.";
            if (z.Von != null && z.Bis != null && z.Von > z.Bis)
                return "Übergang: Jahrgang von liegt nach Jahrgang bis.";
            if (z.Von is < 1900 or > 2100 || z.Bis is < 1900 or > 2100)
                return "Übergang: Jahrgang zwischen 1900 und 2100.";
            spannen.Add((z.Von ?? int.MinValue, z.Bis ?? int.MaxValue));
        }
        spannen.Sort((a, b) => a.Von.CompareTo(b.Von));
        for (var i = 1; i < spannen.Count; i++)
        {
            if (spannen[i].Von <= spannen[i - 1].Bis)
                return "Übergänge überschneiden sich.";
        }
        return null;
    }
}

public class AhvBeitragspflichtDto
{
    public int Beginn { get; set; }
    public int EndeMaenner { get; set; }
    public int EndeFrauen { get; set; }
    public List<AhvUebergangDto> Uebergaenge { get; set; } = new();
}

public class AhvUebergangDto
{
    public int? Von { get; set; }
    public int? Bis { get; set; }
    public int Jahre { get; set; }
    public int Monate { get; set; }
}

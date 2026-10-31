using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// HR-Bereich → GastroSocial → «BVG-Abgleich: Versichertenliste» (Walter 31.10.2026).
/// Liste der beruflichen Vorsorge gegen die BVG-Pflicht der Sidebar-Filiale.
/// Geschrieben wird erst mit «Ergebnis übernehmen»: versichert ab dem Datum der Liste.
/// Wer nicht auf der Liste steht, bleibt unverändert.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/imports/bvg-versichertenliste")]
public class BvgVersichertenAbgleichController : ControllerBase
{
    private readonly AppDbContext _db;
    public BvgVersichertenAbgleichController(AppDbContext db) => _db = db;

    public sealed record EintragDto(int EmployeeId, string? Aktion, DateOnly GueltigAb, DateOnly? GueltigBis, string? Bezeichnung);
    public sealed record UebernehmenDto(int CompanyProfileId, List<EintragDto>? Eintraege);

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze(
        [FromForm] IFormFile file,
        [FromForm] int companyProfileId = 0,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Datei fehlt." });
        if (companyProfileId <= 0)
            return BadRequest(new { error = "Bitte zuerst links eine Filiale wählen." });

        var filialen = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.RestaurantCode, c.City, c.BranchName })
            .ToListAsync(ct);
        var branch = filialen.FirstOrDefault(c => c.Id == companyProfileId);
        if (branch == null)
            return BadRequest(new { error = "Filiale nicht gefunden." });
        string FilialName(int id)
        {
            var f = filialen.FirstOrDefault(x => x.Id == id);
            if (f == null) return "anderer Filiale";
            var n = !string.IsNullOrWhiteSpace(f.BranchName) ? f.BranchName! : f.City ?? "";
            return string.IsNullOrWhiteSpace(f.RestaurantCode) ? n : $"{f.RestaurantCode} {n}".Trim();
        }

        List<BvgVersichertenAbgleich.Zeile> zeilen;
        try
        {
            await using var stream = file.OpenReadStream();
            zeilen = BvgVersichertenAbgleich.LiesExcel(stream, file.FileName);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Datei konnte nicht gelesen werden: {ex.Message}" });
        }
        if (zeilen.Count == 0)
            return BadRequest(new { error = "Keine Datenzeilen gefunden. Erwartet: Versichertenliste mit «Name», «Vorname», «AHV-Nummer» und «Gültig ab»." });

        var heute = DateTime.Today;
        var heuteD = DateOnly.FromDateTime(heute);
        var emps = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden)
            .Select(e => new
            {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.SocialSecurityNumber, e.DateOfBirth,
                e.ExitDate, e.IsActive, e.IsPayrollExcluded,
                Vertraege = e.Employments.Select(em => new { em.CompanyProfileId, em.ContractStartDate, em.ContractEndDate }).ToList(),
            })
            .ToListAsync(ct);
        var pflicht = await _db.EmployeeBvgPflichten.AsNoTracking()
            .Where(p => p.Versichert)
            .Select(p => new { p.EmployeeId, p.GueltigAb, p.GueltigBis })
            .ToListAsync(ct);
        var pflichtBy = pflicht.ToLookup(p => p.EmployeeId);

        var alle = emps.Select(e =>
        {
            var hier = e.Vertraege.Where(v => v.CompanyProfileId == companyProfileId).ToList();
            bool Laeuft(DateTime? ende) => ende == null || ende.Value.Date >= heute;
            bool nichtAusgetreten = e.IsActive && (e.ExitDate == null || e.ExitDate.Value.Date >= heute);
            var anderswo = e.Vertraege
                .Where(v => v.CompanyProfileId != companyProfileId && v.CompanyProfileId != null && Laeuft(v.ContractEndDate))
                .Select(v => v.CompanyProfileId!.Value).Distinct().ToList();
            var rows = pflichtBy[e.Id].OrderByDescending(p => p.GueltigAb).ToList();
            var deckt = rows.FirstOrDefault(p => p.GueltigAb <= heuteD && (p.GueltigBis == null || p.GueltigBis >= heuteD));
            var span = deckt ?? rows.FirstOrDefault();
            return new BvgVersichertenAbgleich.Ma
            {
                Id = e.Id,
                Nummer = e.EmployeeNumber,
                Vorname = e.FirstName,
                Nachname = e.LastName,
                Ahv = e.SocialSecurityNumber,
                Geburtsdatum = e.DateOfBirth,
                InFiliale = hier.Count > 0,
                AktivInFiliale = nichtAusgetreten && hier.Any(v => Laeuft(v.ContractEndDate)),
                OhneLohn = e.IsPayrollExcluded,
                AndereFiliale = anderswo.Count > 0 ? string.Join(", ", anderswo.Select(FilialName)) : null,
                VersichertAb = span?.GueltigAb,
                VersichertBis = span?.GueltigBis,
            };
        }).ToList();

        var res = BvgVersichertenAbgleich.Vergleiche(zeilen, alle, heute);
        var uebernahme = res.Festlegen.Concat(res.Beenden).Concat(res.Abweichend)
            .Where(t => t.EmployeeId != null && t.GueltigAb != null && t.Aktion != null)
            .Select(t => new
            {
                employeeId = t.EmployeeId,
                aktion = t.Aktion,
                gueltigAb = t.GueltigAb!.Value.ToString("yyyy-MM-dd"),
                gueltigBis = t.GueltigBis?.ToString("yyyy-MM-dd"),
                bezeichnung = t.Bezeichnung,
                name = t.OneCrewName ?? t.Name,
            })
            .ToList();
        return Ok(new { filiale = FilialName(companyProfileId), ergebnis = res, uebernahme });
    }

    [HttpPost("uebernehmen")]
    public async Task<IActionResult> Uebernehmen([FromBody] UebernehmenDto dto)
    {
        if (dto.CompanyProfileId <= 0)
            return BadRequest(new { error = "Bitte zuerst links eine Filiale wählen." });
        var eintraege = (dto.Eintraege ?? new()).Where(e => e.EmployeeId > 0 && e.GueltigAb != default).ToList();
        if (eintraege.Count == 0)
            return BadRequest(new { error = "Nichts zu übernehmen." });

        var ids = eintraege.Select(e => e.EmployeeId).Distinct().ToList();
        var inFiliale = (await _db.Employments.AsNoTracking()
                .Where(v => v.CompanyProfileId == dto.CompanyProfileId && ids.Contains(v.EmployeeId))
                .Select(v => v.EmployeeId)
                .Distinct()
                .ToListAsync())
            .ToHashSet();
        var bestehend = await _db.EmployeeBvgPflichten
            .Where(p => ids.Contains(p.EmployeeId))
            .ToListAsync();
        var userId = GetUserId();
        int neu = 0, beendet = 0, datum = 0;
        foreach (var e in eintraege)
        {
            if (!inFiliale.Contains(e.EmployeeId)) continue;
            var eigene = bestehend.Where(p => p.EmployeeId == e.EmployeeId).ToList();
            if (e.Aktion == BvgVersichertenAbgleich.Datum)
            {
                var offen = eigene.FirstOrDefault(p => p.Versichert && p.GueltigBis == null && p.GueltigAb > e.GueltigAb);
                if (offen == null) continue;
                foreach (var o in eigene.Where(p => p != offen && p.GueltigAb < e.GueltigAb && (p.GueltigBis == null || p.GueltigBis >= e.GueltigAb)))
                    o.GueltigBis = e.GueltigAb.AddDays(-1);
                offen.GueltigAb = e.GueltigAb;
                offen.Quelle = EmployeeBvgPflicht.QuelleListe;
                var planName = Plan(e.Bezeichnung);
                if (planName != null) offen.Bemerkung = planName;
                datum++;
                continue;
            }
            if (e.Aktion == BvgVersichertenAbgleich.Beenden)
            {
                if (e.GueltigBis == null) continue;
                var offen = eigene.FirstOrDefault(p => p.Versichert && p.GueltigBis == null && p.GueltigAb <= e.GueltigBis);
                if (offen == null) continue;
                offen.GueltigBis = e.GueltigBis;
                beendet++;
                continue;
            }
            if (e.Aktion != BvgVersichertenAbgleich.Anlegen) continue;
            if (eigene.Any(p => p.Versichert)) continue;
            foreach (var o in eigene.Where(p => p.GueltigAb < e.GueltigAb && (p.GueltigBis == null || p.GueltigBis >= e.GueltigAb)))
                o.GueltigBis = e.GueltigAb.AddDays(-1);
            var plan = Plan(e.Bezeichnung);
            var row = new EmployeeBvgPflicht
            {
                EmployeeId = e.EmployeeId,
                Versichert = true,
                GueltigAb = e.GueltigAb,
                GueltigBis = e.GueltigBis,
                Quelle = EmployeeBvgPflicht.QuelleListe,
                Bemerkung = plan,
                CreatedAt = DateTime.Now,
                CreatedBy = userId,
            };
            _db.EmployeeBvgPflichten.Add(row);
            bestehend.Add(row);
            neu++;
        }
        await _db.SaveChangesAsync();
        return Ok(new { neu, beendet, datum });
    }

    private static string? Plan(string? bezeichnung)
    {
        var plan = string.IsNullOrWhiteSpace(bezeichnung) ? null : bezeichnung.Trim();
        if (plan != null && plan.Length > 200) plan = plan[..200];
        return plan;
    }

    private int? GetUserId()
    {
        var s = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(s, out var id) ? id : null;
    }
}

using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Gemerkte An-/Abmeldungen bei GastroSocial (Walter 13.10.2026).
/// • AKIS-Seite: «Im AKISnet gemeldet ✓» → <c>gemeldet</c>, rückgängig → DELETE.
/// • AHV-Abgleich: Altbestand aus der GastroSocial-Liste → <c>abgleich-uebernehmen</c>.
/// Gespeichert wird nur, was nach <see cref="GastroSocialMeldungRegel"/> wirklich eine An- oder
/// Abmeldung des MA in dieser Filiale ist — der Browser kann keine beliebigen Einträge anlegen.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/gastrosocial-meldung")]
[MaEintrag(typeof(GastroSocialMeldung))]
public class GastroSocialMeldungController : HrControllerBase
{
    public GastroSocialMeldungController(AppDbContext db) : base(db) { }

    public sealed record EintragDto(int EmployeeId, string? Art, DateOnly Datum);
    public sealed record GemeldetDto(int CompanyProfileId, string Art, List<EintragDto> Eintraege);
    public sealed record UebernehmenDto(int CompanyProfileId, List<EintragDto> Eintraege);

    [HttpPost("gemeldet")]
    public async Task<IActionResult> Gemeldet([FromBody] GemeldetDto dto)
    {
        if (dto.Art is not (GastroSocialMeldungRegel.Anmeldung or GastroSocialMeldungRegel.Abmeldung or GastroSocialMeldungRegel.Kantonswechsel))
            return BadRequest(new { error = "Art fehlt (ANMELDUNG, ABMELDUNG oder KTWECHSEL)." });
        var eintraege = (dto.Eintraege ?? new()).Select(e => e with { Art = dto.Art }).ToList();
        return await SpeichernAsync(dto.CompanyProfileId, eintraege, GastroSocialMeldung.QuelleAkis);
    }

    [HttpPost("abgleich-uebernehmen")]
    public async Task<IActionResult> AbgleichUebernehmen([FromBody] UebernehmenDto dto) =>
        await SpeichernAsync(dto.CompanyProfileId, dto.Eintraege ?? new(), GastroSocialMeldung.QuelleAbgleich);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Loeschen(int id)
    {
        var m = await _db.GastroSocialMeldungen.FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return NotFound();
        if (!await CanAccessBranchAsync(m.CompanyProfileId)) return Forbid();
        _db.GastroSocialMeldungen.Remove(m);
        await _db.SaveChangesAsync();
        return Ok(new { geloescht = 1 });
    }

    private async Task<IActionResult> SpeichernAsync(int cpId, List<EintragDto> eintraege, string quelle)
    {
        if (cpId <= 0) return BadRequest(new { error = "Filiale fehlt." });
        if (!await CanAccessBranchAsync(cpId)) return Forbid();
        if (eintraege.Count == 0) return Ok(new { neu = 0, schonGemeldet = 0, ungueltig = 0 });

        var empIds = eintraege.Select(e => e.EmployeeId).Distinct().ToList();
        var ereignisse = await EreignisseAsync(_db, cpId, empIds);
        var bestehend = (await _db.GastroSocialMeldungen
                .Where(m => m.CompanyProfileId == cpId && empIds.Contains(m.EmployeeId))
                .Select(m => new { m.EmployeeId, m.CompanyProfileId, m.Art, m.Stichtag })
                .ToListAsync())
            .Select(m => new GastroSocialMeldungRegel.Meldung(m.EmployeeId, m.CompanyProfileId, m.Art, m.Stichtag))
            .ToList();

        var uid = GetCurrentUserId();
        var wer = await ActorNameAsync(uid);
        int neu = 0, schon = 0, ungueltig = 0;
        foreach (var e in eintraege)
        {
            var ereignis = ereignisse.FirstOrDefault(x => x.EmployeeId == e.EmployeeId && x.Art == e.Art && x.Datum == e.Datum);
            if (ereignis == null) { ungueltig++; continue; }
            if (GastroSocialMeldungRegel.Passende(ereignis, bestehend) != null) { schon++; continue; }
            var m = new GastroSocialMeldung
            {
                EmployeeId = e.EmployeeId,
                CompanyProfileId = cpId,
                Art = ereignis.Art,
                Stichtag = ereignis.Datum,
                Quelle = quelle,
                ErfasstAm = DateTime.Now,
                ErfasstVonUserId = uid,
                ErfasstVon = wer,
            };
            _db.GastroSocialMeldungen.Add(m);
            bestehend.Add(new GastroSocialMeldungRegel.Meldung(m.EmployeeId, cpId, m.Art, m.Stichtag));
            neu++;
        }
        await _db.SaveChangesAsync();
        return Ok(new { neu, schonGemeldet = schon, ungueltig });
    }

    /// <summary>Alle An-/Abmelde-Ereignisse der genannten MA in dieser Filiale.</summary>
    internal static async Task<List<GastroSocialMeldungRegel.Ereignis>> EreignisseAsync(
        AppDbContext db, int cpId, IReadOnlyCollection<int> empIds)
    {
        var vertraege = await db.Employments.AsNoTracking()
            .Where(v => empIds.Contains(v.EmployeeId) && v.CompanyProfileId != null)
            .Select(v => new GastroSocialMeldungRegel.Vertrag(v.EmployeeId, v.CompanyProfileId, v.ContractStartDate, v.ContractEndDate))
            .ToListAsync();
        var austritte = await db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id) && e.ExitDate != null)
            .Select(e => new { e.Id, e.ExitDate, e.KuendigungPer })
            .ToListAsync();
        var aus = GastroSocialMeldungRegel.Arbeitsort(vertraege, await GastroSocialFilialen.LadenAsync(db));
        var res = GastroSocialMeldungRegel.Anmeldungen(vertraege)
            .Where(e => e.CompanyProfileId == cpId && !aus.IstUnterdrueckt(e)).ToList();
        res.AddRange(GastroSocialMeldungRegel.Abmeldungen(vertraege,
                austritte.Select(a => new GastroSocialMeldungRegel.PersonAustritt(a.Id, a.ExitDate, a.KuendigungPer)))
            .Where(e => e.CompanyProfileId == cpId && !aus.IstUnterdrueckt(e)));
        res.AddRange(aus.Ereignisse.Where(e => e.CompanyProfileId == cpId));
        return res;
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

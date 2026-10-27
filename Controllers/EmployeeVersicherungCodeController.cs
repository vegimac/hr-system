using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HrSystem.Controllers;

/// <summary>
/// Versicherungs-Codes eines Mitarbeiters (Walter 07.09.2026, Swissdec-Lösungen):
/// UVG / UVGZ / KTG / BVG, versioniert (ab/bis). Ohne Eintrag gilt der Standard
/// aus den SV-Sätzen — GET liefert deshalb pro Art auch den EFFEKTIVEN Code und
/// die wählbaren Codes (aus social_insurance_rate.loesungs_code).
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/versicherung-codes")]
[Authorize(Roles = "admin,superuser,user")]
public class EmployeeVersicherungCodeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LohnEditLockService _editLock;
    public EmployeeVersicherungCodeController(AppDbContext db, LohnEditLockService editLock)
    { _db = db; _editLock = editLock; }

    public record UpsertDto(string Art, string? Code, DateOnly ValidFrom, DateOnly? ValidTo,
                            decimal? BeitragFixAn, decimal? BeitragFixAg, string? Bemerkung,
                            string? BvgEintrittsgrund = null, bool? BvgVollArbeitsfaehig = null, decimal? BvgBasisManuell = null,
                            bool Zusaetzlich = false,   // true = bisherige Codes derselben Art bleiben (z.B. KTG 11 + 12)
                            int? EmpfaengerId = null);  // UVG: welcher Versicherer für diese Person

    private static readonly string[] Arten = { "UVG", "UVGZ", "KTG", "BVG", "AHV" };

    [HttpGet]
    public async Task<IActionResult> List(int employeeId, [FromQuery] DateOnly? stichtag)
    {
        var tag = stichtag ?? DateOnly.FromDateTime(DateTime.Today);
        var eintraege = await _db.EmployeeVersicherungCodes.AsNoTracking()
            .Where(v => v.EmployeeId == employeeId)
            .OrderBy(v => v.Art).ThenByDescending(v => v.ValidFrom)
            .ToListAsync();
        var branchId = await GetEmployeeBranchAsync(employeeId);
        var renten = await RentenalterAsync(employeeId, branchId);
        var saetze = await _db.SocialInsuranceRates.AsNoTracking()
            .Where(r => r.IsActive && r.LoesungsCode != null
                     && (r.CompanyProfileId == null || r.CompanyProfileId == branchId))
            .ToListAsync();

        var arten = Arten.Select(art =>
        {
            var svCodes = EmployeeVersicherungCode.SvCodesFuer(art);
            var optionen = art == EmployeeVersicherungCode.ArtAhv
                // AHV/ALV kennt keine Lösungs-Codes in den SV-Sätzen — Abweichungen: Sonderfall, Freibetrag-Verzicht.
                ? new[]
                {
                    new { code = EmployeeVersicherungCode.CodeSonderfall, name = "AHV/ALV-Sonderfall – nicht beitragspflichtig (z.B. Versicherung im Ausland, A1)", istStandard = false },
                    new { code = EmployeeVersicherungCode.CodeFreibetragVerzicht, name = "MA wünscht Verzicht auf AHV-Freibetrag 1'400/Mt. (erst ab Referenzalter, voller Lohn)", istStandard = false },
                }.ToList()
                : saetze.Where(s => svCodes.Contains(s.Code, StringComparer.OrdinalIgnoreCase))
                .GroupBy(s => s.LoesungsCode!.ToUpperInvariant())
                .Select(g => new {
                    code = g.Key,
                    name = g.OrderByDescending(x => x.ValidFrom).First().Name,
                    istStandard = g.Any(x => x.IsDefaultCode),
                })
                .OrderBy(o => o.code).ToList();
            var amTag = eintraege.Where(e => e.Art == art && e.GiltAm(tag)).ToList();
            var explizit = art == "BVG"
                ? (PayrollCalculations.WaehleBvgFix(amTag) ?? amTag.FirstOrDefault())
                : amTag.FirstOrDefault();
            var alleCodes = amTag.Where(e => !string.IsNullOrWhiteSpace(e.Code)).Select(e => e.Code!).Distinct().ToList();
            return new
            {
                art,
                effektiverCode = PayrollCalculations.EffektiverCode(art, eintraege, saetze, tag),
                weitereCodes = alleCodes.Skip(1).ToList(),
                herkunft = explizit?.Code != null ? "manuell" : art == EmployeeVersicherungCode.ArtAhv ? "beitragspflichtig" : (optionen.Any(o => o.istStandard) ? "standard" : (optionen.Count == 0 ? "keine Lösungen erfasst" : "ohne Code")),
                explizit = explizit == null ? null : new { explizit.Id, explizit.Code, explizit.ValidFrom, explizit.ValidTo, explizit.BeitragFixAn, explizit.BeitragFixAg, explizit.Bemerkung,
                                                           explizit.BvgEintrittsgrund, explizit.BvgVollArbeitsfaehig, explizit.BvgBasisManuell, explizit.EmpfaengerId },
                optionen,
            };
        }).ToList();

        return Ok(new
        {
            stichtag = tag,
            arten,
            eintraege = eintraege.Select(e => new { e.Id, e.Art, e.Code, e.ValidFrom, e.ValidTo, e.BeitragFixAn, e.BeitragFixAg, e.Bemerkung, e.CreatedAt,
                                                    e.BvgEintrittsgrund, e.BvgVollArbeitsfaehig, e.BvgBasisManuell, e.EmpfaengerId,
                                                    isCurrent = e.GiltAm(tag) }),
            uvgVersicherer = await OffeneUvgVersichererAsync(branchId, tag),
            imRentenalter = renten.Erreicht,
            verzichtHinweis = renten.Hinweis,
            rentenalterStichtag = renten.Stichtag,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, [FromBody] UpsertDto dto)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId)) return NotFound();
        var fehler = Pruefe(dto);
        if (fehler != null) return BadRequest(new { error = "UNGUELTIG", message = fehler });
        var verzicht = await PruefeVerzichtAlterAsync(employeeId, dto);
        if (verzicht != null) return verzicht;
        var versichererFehler = await PruefeUvgVersichererAsync(employeeId, dto);
        if (versichererFehler != null) return BadRequest(new { error = "UVG_VERSICHERER", message = versichererFehler });

        var lock1 = await PruefeEditLockAsync(employeeId, dto.ValidFrom);
        if (lock1 != null) return lock1;

        var neuCode = Norm(dto.Code);
        var offeneQ = _db.EmployeeVersicherungCodes
            .Where(v => v.EmployeeId == employeeId && v.Art == dto.Art
                     && (v.ValidTo == null || v.ValidTo >= dto.ValidFrom) && v.ValidFrom < dto.ValidFrom);
        if (dto.Art == EmployeeVersicherungCode.ArtAhv && neuCode != null)
            offeneQ = offeneQ.Where(v => v.Code == neuCode);
        var offene = dto.Zusaetzlich ? new List<EmployeeVersicherungCode>() : await offeneQ.ToListAsync();
        foreach (var o in offene) o.ValidTo = dto.ValidFrom.AddDays(-1);
        var gleich = await _db.EmployeeVersicherungCodes
            .AnyAsync(v => v.EmployeeId == employeeId && v.Art == dto.Art && v.ValidFrom == dto.ValidFrom && v.Code == neuCode);
        if (gleich) return Conflict(new { error = "CODE_BEREITS_AB_DATUM", message = $"Für {dto.Art} gibt es bereits einen Eintrag ab {dto.ValidFrom:dd.MM.yyyy}." });

        var e = new EmployeeVersicherungCode
        {
            EmployeeId = employeeId, Art = dto.Art, Code = Norm(dto.Code),
            ValidFrom = dto.ValidFrom, ValidTo = dto.ValidTo,
            BeitragFixAn = dto.Art == "BVG" && dto.BeitragFixAn is > 0 ? dto.BeitragFixAn : null,
            BeitragFixAg = dto.Art == "BVG" && dto.BeitragFixAg is > 0 ? dto.BeitragFixAg : null,
            Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim(),
            EmpfaengerId = dto.Art == "UVG" ? dto.EmpfaengerId : null,
            CreatedAt = DateTime.Now, CreatedBy = GetCurrentUserId(),
        };
        UebernehmeBvg(e, dto);
        _db.EmployeeVersicherungCodes.Add(e);
        await _db.SaveChangesAsync();
        return Ok(new { id = e.Id, vorgaengerBeendet = offene.Count });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int employeeId, int id, [FromBody] UpsertDto dto)
    {
        var e = await _db.EmployeeVersicherungCodes.FirstOrDefaultAsync(v => v.Id == id && v.EmployeeId == employeeId);
        if (e == null) return NotFound();
        var fehler = Pruefe(dto);
        if (fehler != null) return BadRequest(new { error = "UNGUELTIG", message = fehler });
        var verzicht = await PruefeVerzichtAlterAsync(employeeId, dto);
        if (verzicht != null) return verzicht;
        var versichererFehler = await PruefeUvgVersichererAsync(employeeId, dto);
        if (versichererFehler != null) return BadRequest(new { error = "UVG_VERSICHERER", message = versichererFehler });
        var lock1 = await PruefeEditLockAsync(employeeId, dto.ValidFrom < e.ValidFrom ? dto.ValidFrom : e.ValidFrom);
        if (lock1 != null) return lock1;

        e.Art = dto.Art; e.Code = Norm(dto.Code); e.ValidFrom = dto.ValidFrom; e.ValidTo = dto.ValidTo;
        e.BeitragFixAn = dto.Art == "BVG" && dto.BeitragFixAn is > 0 ? dto.BeitragFixAn : null;
        e.BeitragFixAg = dto.Art == "BVG" && dto.BeitragFixAg is > 0 ? dto.BeitragFixAg : null;
        e.Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        e.EmpfaengerId = dto.Art == "UVG" ? dto.EmpfaengerId : null;
        UebernehmeBvg(e, dto);
        await _db.SaveChangesAsync();
        return Ok(new { id = e.Id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int employeeId, int id)
    {
        var e = await _db.EmployeeVersicherungCodes.FirstOrDefaultAsync(v => v.Id == id && v.EmployeeId == employeeId);
        if (e == null) return NotFound();
        var lock1 = await PruefeEditLockAsync(employeeId, e.ValidFrom);
        if (lock1 != null) return lock1;
        _db.EmployeeVersicherungCodes.Remove(e);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── Helfer ──────────────────────────────────────────────────────────
    private static string? Norm(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    private static void UebernehmeBvg(EmployeeVersicherungCode e, UpsertDto dto)
    {
        var bvg = dto.Art == "BVG";
        e.BvgEintrittsgrund    = bvg && !string.IsNullOrWhiteSpace(dto.BvgEintrittsgrund) ? dto.BvgEintrittsgrund.Trim() : null;
        e.BvgVollArbeitsfaehig = bvg ? dto.BvgVollArbeitsfaehig : null;
        e.BvgBasisManuell      = bvg && dto.BvgBasisManuell is > 0 ? dto.BvgBasisManuell : null;
    }

    private static string? Pruefe(UpsertDto dto)
    {
        if (!Arten.Contains(dto.Art)) return "Art muss UVG, UVGZ, KTG, BVG oder AHV sein.";
        if (dto.Art == EmployeeVersicherungCode.ArtAhv)
        {
            var ahv = (dto.Code ?? "").Trim().ToUpperInvariant();
            if (ahv != EmployeeVersicherungCode.CodeSonderfall && ahv != EmployeeVersicherungCode.CodeFreibetragVerzicht)
                return "Bei AHV nur SONDERFALL (nicht beitragspflichtig) oder VERZICHT (kein Freibetrag 1'400) — Normalfall = kein Eintrag.";
        }
        if (dto.ValidTo != null && dto.ValidTo < dto.ValidFrom) return "«Gültig bis» liegt vor «Gültig ab».";
        var hatCode = !string.IsNullOrWhiteSpace(dto.Code);
        var hatFix  = dto.Art == "BVG" && (dto.BeitragFixAn is > 0 || dto.BeitragFixAg is > 0);
        if (!hatCode && !hatFix) return dto.Art == "BVG" ? "Code oder fester Beitrag angeben." : "Code angeben.";
        return null;
    }

    private async Task<IActionResult?> PruefeEditLockAsync(int employeeId, DateOnly validFrom)
    {
        var branchId = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = branchId.HasValue ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value) : null;
        if (firstAllowed.HasValue && validFrom < firstAllowed.Value)
            return Conflict(new
            {
                error = "LOHN_EDIT_LOCKED",
                message = $"'Gültig ab {validFrom:dd.MM.yyyy}' liegt in einer bereits in Verarbeitung befindlichen Lohnperiode. Frühestes erlaubtes 'Gültig ab': {firstAllowed.Value:dd.MM.yyyy}.",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });
        return null;
    }

    private async Task<string?> PruefeUvgVersichererAsync(int employeeId, UpsertDto dto)
    {
        if (dto.Art != "UVG" || dto.EmpfaengerId is not int id) return null;
        var art = await _db.LohndatenEmpfaengers.AsNoTracking()
            .Where(e => e.Id == id).Select(e => e.Art).FirstOrDefaultAsync();
        if (art == null) return "UVG-Versicherer nicht gefunden.";
        if (!string.Equals(art, "UVG", StringComparison.OrdinalIgnoreCase))
            return "Die Zuweisung muss ein UVG-Versicherer sein.";
        var branchId = await GetEmployeeBranchAsync(employeeId);
        if (branchId == null) return "Die Person hat keine Filiale — den UVG-Versicherer dort zuerst zuordnen.";
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var zeilen = await _db.CompanyProfileEmpfaengers.AsNoTracking()
            .Where(z => z.CompanyProfileId == branchId && z.EmpfaengerId == id && z.IsActive)
            .Select(z => new { z.GueltigAb, z.GueltigBis })
            .ToListAsync();
        var offen = zeilen.Any(z => (z.GueltigAb == null || z.GueltigAb <= heute)
                                 && (z.GueltigBis == null || z.GueltigBis >= heute));
        if (!offen) return "Diesen UVG-Versicherer zuerst der Filiale zuordnen.";
        return null;
    }

    private sealed record UvgVersichererWahl(int Id, string Bezeichnung, string? Kassennummer);

    private async Task<List<UvgVersichererWahl>> OffeneUvgVersichererAsync(int? branchId, DateOnly tag)
    {
        if (branchId == null) return new List<UvgVersichererWahl>();
        var zeilen = await _db.CompanyProfileEmpfaengers.AsNoTracking()
            .Where(z => z.CompanyProfileId == branchId && z.IsActive && z.Empfaenger!.Art == "UVG")
            .Select(z => new { z.EmpfaengerId, z.Empfaenger!.Bezeichnung, z.Empfaenger.Kassennummer, z.GueltigAb, z.GueltigBis })
            .ToListAsync();
        return zeilen
            .Where(z => (z.GueltigAb == null || z.GueltigAb <= tag) && (z.GueltigBis == null || z.GueltigBis >= tag))
            .GroupBy(z => z.EmpfaengerId)
            .Select(g => new UvgVersichererWahl(g.Key, g.First().Bezeichnung, g.First().Kassennummer))
            .OrderBy(x => x.Bezeichnung)
            .ToList();
    }

    private sealed record RentenalterLage(bool Erreicht, string? Hinweis, DateOnly Stichtag);

    /// <summary>
    /// Verzicht auf den Freibetrag nur, wenn am Stichtag das Referenzalter erreicht
    /// ist. AHV 21 (Frauen-Übergang) und «ab dem Folgemonat» kommen aus
    /// <see cref="PayrollCalculations.HatReferenzalterErreicht"/>, derselbe Stand
    /// wie der Lohnlauf. Stichtag = älteste offene Lohnperiode der Filiale.
    /// </summary>
    private async Task<RentenalterLage> RentenalterAsync(int employeeId, int? branchId)
    {
        var stichtag = await RentenalterStichtagAsync(branchId);
        var emp = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new { e.DateOfBirth, e.Gender })
            .FirstOrDefaultAsync();
        if (emp?.DateOfBirth == null)
            return new RentenalterLage(false, "Verzicht auf den Freibetrag braucht ein Geburtsdatum.", stichtag);
        var stand = await AhvBeitragspflichtStand.LadenAsync(_db);
        var erreicht = PayrollCalculations.HatReferenzalterErreicht(
            emp.Gender, emp.DateOfBirth.Value, stichtag.Year, stichtag.Month, stand);
        return erreicht
            ? new RentenalterLage(true, null, stichtag)
            : new RentenalterLage(false, "Verzicht auf den Freibetrag erst ab dem Folgemonat des Referenzalters.", stichtag);
    }

    private async Task<IActionResult?> PruefeVerzichtAlterAsync(int employeeId, UpsertDto dto)
    {
        if (dto.Art != EmployeeVersicherungCode.ArtAhv) return null;
        if (!string.Equals((dto.Code ?? "").Trim(), EmployeeVersicherungCode.CodeFreibetragVerzicht, StringComparison.OrdinalIgnoreCase))
            return null;
        var branchId = await GetEmployeeBranchAsync(employeeId);
        var lage = await RentenalterAsync(employeeId, branchId);
        if (lage.Erreicht) return null;
        return BadRequest(new { error = "NICHT_IM_RENTENALTER", message = lage.Hinweis });
    }

    private async Task<DateOnly> RentenalterStichtagAsync(int? branchId)
    {
        var q = _db.PayrollPerioden.AsNoTracking().Where(p => p.Status != "abgeschlossen");
        if (branchId.HasValue)
            q = q.Where(p => p.CompanyProfileId == branchId.Value);
        var earliest = await q.OrderBy(p => p.PeriodFrom).Select(p => (DateOnly?)p.PeriodFrom).FirstOrDefaultAsync();
        return earliest ?? DateOnly.FromDateTime(DateTime.Today);
    }

    private async Task<int?> GetEmployeeBranchAsync(int employeeId)
    {
        return await _db.Employments
            .Where(e => e.EmployeeId == employeeId && e.IsActive)
            .OrderByDescending(e => e.ContractStartDate)
            .Select(e => (int?)e.CompanyProfileId)
            .FirstOrDefaultAsync();
    }

    private int? GetCurrentUserId()
    {
        var s = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(s, out var id) ? id : null;
    }
}

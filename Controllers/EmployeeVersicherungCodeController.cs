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
                            bool Zusaetzlich = false);   // true = bisherige Codes derselben Art bleiben (z.B. KTG 11 + 12)

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
        var saetze = await _db.SocialInsuranceRates.AsNoTracking()
            .Where(r => r.IsActive && r.LoesungsCode != null
                     && (r.CompanyProfileId == null || r.CompanyProfileId == branchId))
            .ToListAsync();

        var arten = Arten.Select(art =>
        {
            var svCodes = EmployeeVersicherungCode.SvCodesFuer(art);
            var optionen = art == EmployeeVersicherungCode.ArtAhv
                // AHV/ALV kennt keine Lösungs-Codes in den SV-Sätzen — einzige Abweichung ist der Sonderfall.
                ? new[] { new { code = EmployeeVersicherungCode.CodeSonderfall, name = "AHV/ALV-Sonderfall – nicht beitragspflichtig (z.B. Versicherung im Ausland, A1)", istStandard = false } }.ToList()
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
                                                           explizit.BvgEintrittsgrund, explizit.BvgVollArbeitsfaehig, explizit.BvgBasisManuell },
                optionen,
            };
        }).ToList();

        return Ok(new
        {
            stichtag = tag,
            arten,
            eintraege = eintraege.Select(e => new { e.Id, e.Art, e.Code, e.ValidFrom, e.ValidTo, e.BeitragFixAn, e.BeitragFixAg, e.Bemerkung, e.CreatedAt,
                                                    e.BvgEintrittsgrund, e.BvgVollArbeitsfaehig, e.BvgBasisManuell,
                                                    isCurrent = e.GiltAm(tag) }),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, [FromBody] UpsertDto dto)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId)) return NotFound();
        var fehler = Pruefe(dto);
        if (fehler != null) return BadRequest(new { error = "UNGUELTIG", message = fehler });

        var lock1 = await PruefeEditLockAsync(employeeId, dto.ValidFrom);
        if (lock1 != null) return lock1;

        // Vorgänger derselben Art automatisch beenden (Neubeginn − 1 Tag) — Walter-Regel.
        // Ausnahme «zusätzlich»: mehrere Codes gleichzeitig (Swissdec KTG/UVGZ 11 + 12).
        var offene = dto.Zusaetzlich ? new List<EmployeeVersicherungCode>() : await _db.EmployeeVersicherungCodes
            .Where(v => v.EmployeeId == employeeId && v.Art == dto.Art
                     && (v.ValidTo == null || v.ValidTo >= dto.ValidFrom) && v.ValidFrom < dto.ValidFrom)
            .ToListAsync();
        foreach (var o in offene) o.ValidTo = dto.ValidFrom.AddDays(-1);
        var neuCode = Norm(dto.Code);
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
            CreatedAt = DateTime.UtcNow, CreatedBy = GetCurrentUserId(),
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
        var lock1 = await PruefeEditLockAsync(employeeId, dto.ValidFrom < e.ValidFrom ? dto.ValidFrom : e.ValidFrom);
        if (lock1 != null) return lock1;

        e.Art = dto.Art; e.Code = Norm(dto.Code); e.ValidFrom = dto.ValidFrom; e.ValidTo = dto.ValidTo;
        e.BeitragFixAn = dto.Art == "BVG" && dto.BeitragFixAn is > 0 ? dto.BeitragFixAn : null;
        e.BeitragFixAg = dto.Art == "BVG" && dto.BeitragFixAg is > 0 ? dto.BeitragFixAg : null;
        e.Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
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
        if (dto.Art == EmployeeVersicherungCode.ArtAhv && !string.Equals(dto.Code?.Trim(), EmployeeVersicherungCode.CodeSonderfall, StringComparison.OrdinalIgnoreCase))
            return "Bei AHV ist nur der Code SONDERFALL (nicht beitragspflichtig) möglich — Normalfall = kein Eintrag.";
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

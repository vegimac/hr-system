using System.Security.Claims;
using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.Elm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// BFS Lohnstrukturerhebung (Walter 13.08.2026) — Phase 1: Status, Prüfmaske,
/// Mappings, MA-Ergänzungsfelder. Der definitive XLS-Export (Spalten A–AS)
/// folgt als Phase 2, NACHDEM Datenstruktur + Zuordnungen geprüft sind.
///
/// Kein LohnEditLock nötig: reine Statistik-/Katalog-Daten, keine
/// lohnwirksamen Änderungen (Whitelist im EditLock-Audit).
/// </summary>
[ApiController]
[Route("api/lse")]
[Authorize(Roles = "admin,superuser")]
public class LseController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LseDatenService _daten;

    public LseController(AppDbContext db, LseDatenService daten)
    {
        _db = db;
        _daten = daten;
    }

    private string Actor()
        => User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "?";

    // ── Versionen ─────────────────────────────────────────────────────────
    [HttpGet("versions")]
    public async Task<IActionResult> GetVersions()
    {
        var vs = await _db.LseVersions.AsNoTracking()
            .OrderByDescending(v => v.SurveyYear)
            .Select(v => new { v.Id, v.SurveyYear, v.SpecVersion, v.IsActive })
            .ToListAsync();
        return Ok(vs);
    }

    /// <summary>Code-Listen/Labels der Version (für Dropdowns im Frontend).</summary>
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig([FromQuery] int year)
    {
        var v = await _daten.GetVersionAsync(year);
        if (v == null) return NotFound(new { error = "VERSION_FEHLT", message = $"Keine aktive LSE-Version für {year}." });
        return Content(v.ConfigJson, "application/json");
    }

    // ── Startmaske: Kennzahlen ────────────────────────────────────────────
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus([FromQuery] int year, [FromQuery] int? companyProfileId)
    {
        try
        {
            var r = await _daten.BuildAsync(year, companyProfileId);
            return Ok(new
            {
                surveyYear = r.SurveyYear,
                specVersion = r.SpecVersion,
                referenzMonat = 10,
                total = r.Rows.Count,
                vollstaendig = r.Rows.Count(x => x.Status == "GRUEN"),
                zuPruefen = r.Rows.Count(x => x.Status == "ORANGE"),
                fehlend = r.Rows.Count(x => x.Status == "ROT"),
                unmappedLohnarten = r.UnmappedLohnarten.Count,
                unmappedStellung = r.UnmappedStellung.Count,
                unmappedVertrag = r.UnmappedVertrag.Count,
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = "VERSION_FEHLT", message = ex.Message });
        }
    }

    // ── Prüfmaske: alle Zeilen mit Status + Fehlern ──────────────────────
    [HttpGet("pruefung")]
    public async Task<IActionResult> GetPruefung([FromQuery] int year, [FromQuery] int? companyProfileId)
    {
        try
        {
            var r = await _daten.BuildAsync(year, companyProfileId);
            return Ok(new
            {
                surveyYear = r.SurveyYear,
                specVersion = r.SpecVersion,
                rows = r.Rows.Select(x => new
                {
                    x.EmployeeId,
                    x.Name,
                    x.EmployeeNumber,
                    x.Filiale,
                    x.Status,
                    fehler = x.Fehler,
                    hinweise = x.Hinweise.Distinct().ToList(),
                    werte = x.Werte,
                }),
                unmappedLohnarten = r.UnmappedLohnarten,
                unmappedStellung = r.UnmappedStellung,
                unmappedVertrag = r.UnmappedVertrag,
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = "VERSION_FEHLT", message = ex.Message });
        }
    }

    // ── Lohnarten-Mapping ─────────────────────────────────────────────────
    [HttpGet("mapping/lohnarten")]
    public async Task<IActionResult> GetLohnartMapping([FromQuery] int year, [FromQuery] int? companyProfileId)
    {
        var vorhanden = await _db.LseLohnartMappings.AsNoTracking()
            .OrderBy(m => m.LohnartCode).ToListAsync();
        // Im Erhebungsjahr tatsächlich vorkommende, noch nicht zugeordnete
        // Lohnarten mitliefern — der Benutzer ordnet zu und bestätigt.
        List<LseDatenService.UnmappedLohnart> offen = new();
        try
        {
            var r = await _daten.BuildAsync(year, companyProfileId);
            offen = r.UnmappedLohnarten;
        }
        catch (InvalidOperationException) { /* Version fehlt → nur Bestand zeigen */ }
        return Ok(new
        {
            kategorien = LseDatenService.BfsKategorien,
            mappings = vorhanden.Select(m => new
            {
                m.Id, m.LohnartCode, m.Bezeichnung, m.BfsKategorie,
                gueltigAb = m.GueltigAb?.ToString("yyyy-MM-dd"),
                gueltigBis = m.GueltigBis?.ToString("yyyy-MM-dd"),
                m.Confirmed,
            }),
            offen,
        });
    }

    public class LohnartMappingDto
    {
        public string LohnartCode { get; set; } = "";
        public string? Bezeichnung { get; set; }
        public string? BfsKategorie { get; set; }
        public string? GueltigAb { get; set; }
        public string? GueltigBis { get; set; }
    }

    [HttpPut("mapping/lohnarten")]
    public async Task<IActionResult> PutLohnartMapping([FromBody] LohnartMappingDto dto)
    {
        var code = (dto.LohnartCode ?? "").Trim();
        if (code.Length == 0) return BadRequest(new { error = "CODE_FEHLT" });
        if (!string.IsNullOrWhiteSpace(dto.BfsKategorie)
            && !LseDatenService.BfsKategorien.Contains(dto.BfsKategorie))
            return BadRequest(new { error = "KATEGORIE_UNGUELTIG", message = $"Unbekannte BFS-Kategorie «{dto.BfsKategorie}»." });

        var m = await _db.LseLohnartMappings.FirstOrDefaultAsync(x => x.LohnartCode == code);
        if (m == null) { m = new LseLohnartMapping { LohnartCode = code }; _db.LseLohnartMappings.Add(m); }
        m.Bezeichnung = string.IsNullOrWhiteSpace(dto.Bezeichnung) ? m.Bezeichnung : dto.Bezeichnung!.Trim();
        m.BfsKategorie = string.IsNullOrWhiteSpace(dto.BfsKategorie) ? null : dto.BfsKategorie;
        m.GueltigAb = DateOnly.TryParse(dto.GueltigAb, out var ga) ? ga : null;
        m.GueltigBis = DateOnly.TryParse(dto.GueltigBis, out var gb) ? gb : null;
        m.Confirmed = m.BfsKategorie != null;   // bestätigt = bewusst zugeordnet
        m.UpdatedAt = DateTime.Now;
        m.UpdatedBy = Actor();
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, m.Id });
    }

    // ── Code-Mapping Stellung/Vertrag ─────────────────────────────────────
    [HttpGet("mapping/codes")]
    public async Task<IActionResult> GetCodeMappings()
    {
        var ms = await _db.LseCodeMappings.AsNoTracking()
            .OrderBy(m => m.MappingTyp).ThenBy(m => m.SourceCode)
            .Select(m => new { m.Id, m.MappingTyp, m.SourceCode, m.BfsCode, m.Confirmed })
            .ToListAsync();
        return Ok(ms);
    }

    public class CodeMappingDto
    {
        public string MappingTyp { get; set; } = "";
        public string SourceCode { get; set; } = "";
        public int? BfsCode { get; set; }
    }

    [HttpPut("mapping/codes")]
    public async Task<IActionResult> PutCodeMapping([FromBody] CodeMappingDto dto)
    {
        if (dto.MappingTyp is not ("STELLUNG" or "VERTRAG"))
            return BadRequest(new { error = "TYP_UNGUELTIG" });
        var src = (dto.SourceCode ?? "").Trim();
        if (src.Length == 0) return BadRequest(new { error = "SOURCE_FEHLT" });
        // Wertebereich gegen die aktive Version prüfen (position 1–5, contract 1–7).
        if (dto.BfsCode.HasValue)
        {
            var max = dto.MappingTyp == "STELLUNG" ? 5 : 7;
            if (dto.BfsCode.Value < 1 || dto.BfsCode.Value > max)
                return BadRequest(new { error = "CODE_UNGUELTIG", message = $"BFS-Code muss zwischen 1 und {max} liegen." });
        }
        var m = await _db.LseCodeMappings
            .FirstOrDefaultAsync(x => x.MappingTyp == dto.MappingTyp && x.SourceCode == src);
        if (m == null) { m = new LseCodeMapping { MappingTyp = dto.MappingTyp, SourceCode = src }; _db.LseCodeMappings.Add(m); }
        m.BfsCode = dto.BfsCode;
        m.Confirmed = dto.BfsCode != null;
        m.UpdatedAt = DateTime.Now;
        m.UpdatedBy = Actor();
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, m.Id });
    }

    // ── MA-Ergänzungsfelder (Bereich «BFS / Statistik») ───────────────────
    [HttpGet("employee/{employeeId:int}")]
    public async Task<IActionResult> GetEmployeeLse(int employeeId)
    {
        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound();
        var lse = await _db.EmployeeLse.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId);
        var (em, firma) = await VertragHeute(employeeId);
        var heute = DateTime.Now;
        var stichtag = new DateOnly(heute.Year, heute.Month, DateTime.DaysInMonth(heute.Year, heute.Month));
        var regel = ElmStatistikCodes.FerienRegelFuer(
            em?.EmploymentModel, em?.SwissdecVertragsart, emp.DateOfBirth, stichtag,
            firma?.VacationSixWeeksFromAge ?? 50, firma?.DefaultVacationWeeks,
            firma?.DefaultVacationPercent5Weeks ?? 0m);
        int? stellungVorschlag = null;
        string? stellungVorschlagText = null;
        if (!string.IsNullOrWhiteSpace(em?.JobTitle))
        {
            var bfs = await _db.LseCodeMappings.AsNoTracking()
                .Where(m => m.MappingTyp == "STELLUNG")
                .ToListAsync();
            var code = bfs.FirstOrDefault(m => string.Equals(m.SourceCode, em!.JobTitle!.Trim(), StringComparison.OrdinalIgnoreCase))?.BfsCode;
            if (code is int nr)
            {
                var zeile = await _db.StatistikStellungen.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.LseCode == nr);
                stellungVorschlag = zeile?.Id;
                stellungVorschlagText = zeile?.Bezeichnung;
            }
        }
        return Ok(new
        {
            employeeId,
            education = lse?.Education,
            universityDegree = lse?.UniversityDegree,
            positionOverride = lse?.PositionOverride,
            practicedProfession = lse?.PracticedProfession,
            inHouseId = lse?.InHouseId,
            statistikAusbildungId = lse?.StatistikAusbildungId,
            statistikStellungId = lse?.StatistikStellungId,
            verliehenesPersonal = lse?.VerliehenesPersonal ?? false,
            stammpersonalOeffentlich = lse?.StammpersonalOeffentlich ?? false,
            flexProfiling = lse?.FlexProfiling,
            leaveEntitlementDays = lse?.LeaveEntitlementDays,
            ferienTageRegel = regel.Tage,
            ferienHinweis = regel.Text,
            ferienHandAbweisen = regel.HandAbweisen,
            stellungVorschlagId = stellungVorschlag,
            stellungVorschlagText,
        });
    }

    private async Task<(Employment? Em, CompanyProfile? Firma)> VertragHeute(int employeeId)
    {
        var jetzt = DateTime.Now;
        var em = await _db.Employments.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId
                     && e.ContractStartDate <= jetzt
                     && (e.ContractEndDate == null || e.ContractEndDate >= jetzt))
            .OrderByDescending(e => e.ContractStartDate)
            .FirstOrDefaultAsync();
        CompanyProfile? firma = null;
        if (em?.CompanyProfileId is int cp)
            firma = await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cp);
        return (em, firma);
    }

    public class EmployeeLseDto
    {
        public int? Education { get; set; }
        public int? UniversityDegree { get; set; }
        public int? PositionOverride { get; set; }
        public string? PracticedProfession { get; set; }
        public string? InHouseId { get; set; }
        public decimal? LeaveEntitlementDays { get; set; }
        public int? StatistikAusbildungId { get; set; }
        public int? StatistikStellungId { get; set; }
        public bool VerliehenesPersonal { get; set; }
        public bool StammpersonalOeffentlich { get; set; }
        public string? FlexProfiling { get; set; }
        /// <summary>Maske am Mitarbeiter: Katalog, Kennzeichen und Ferientage zusammen.</summary>
        public bool Vollstaendig { get; set; }
    }

    [HttpPut("employee/{employeeId:int}")]
    public async Task<IActionResult> PutEmployeeLse(int employeeId, [FromBody] EmployeeLseDto dto)
    {
        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound();
        if ((dto.PracticedProfession?.Length ?? 0) > 255)
            return BadRequest(new { error = "BERUF_ZU_LANG", message = "Ausgeübter Beruf: max. 255 Zeichen." });
        if ((dto.FlexProfiling?.Length ?? 0) > 255)
            return BadRequest(new { error = "PROFILING_ZU_LANG", message = "Das Profiling-Feld hat höchstens 255 Zeichen." });
        if (dto.LeaveEntitlementDays is < 0m or > 99m)
            return BadRequest(new { error = "FERIENTAGE_UNGUELTIG", message = "Ferientage: 0 bis 99 pro Jahr." });

        if (dto.Vollstaendig && dto.LeaveEntitlementDays is > 0m)
        {
            var (em, firma) = await VertragHeute(employeeId);
            var heute = DateTime.Now;
            var stichtag = new DateOnly(heute.Year, heute.Month, DateTime.DaysInMonth(heute.Year, heute.Month));
            var regel = ElmStatistikCodes.FerienRegelFuer(
                em?.EmploymentModel, em?.SwissdecVertragsart, emp.DateOfBirth, stichtag,
                firma?.VacationSixWeeksFromAge ?? 50, firma?.DefaultVacationWeeks,
                firma?.DefaultVacationPercent5Weeks ?? 0m);
            if (regel.HandAbweisen)
                return BadRequest(new { error = "FERIEN_WIDERSPRUCH", message = regel.Text });
        }

        var lse = await _db.EmployeeLse.FirstOrDefaultAsync(x => x.EmployeeId == employeeId);
        if (lse == null) { lse = new EmployeeLse { EmployeeId = employeeId }; _db.EmployeeLse.Add(lse); }

        if (dto.Vollstaendig)
        {
            if (dto.StatistikAusbildungId is int aid)
            {
                var row = await _db.StatistikAusbildungen.AsNoTracking().FirstOrDefaultAsync(a => a.Id == aid);
                if (row == null)
                    return BadRequest(new { error = "AUSBILDUNG_UNBEKANNT", message = "Diese Ausbildung gibt es im Katalog nicht." });
                lse.StatistikAusbildungId = row.Id;
                lse.Education = row.LseAusbildung;
                lse.UniversityDegree = row.LseTitel;
            }
            else
            {
                lse.StatistikAusbildungId = null;
                lse.Education = null;
                lse.UniversityDegree = null;
            }
            if (dto.StatistikStellungId is int sid)
            {
                var row = await _db.StatistikStellungen.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid);
                if (row == null)
                    return BadRequest(new { error = "STELLUNG_UNBEKANNT", message = "Diese berufliche Stellung gibt es im Katalog nicht." });
                lse.StatistikStellungId = row.Id;
                lse.PositionOverride = row.LseCode;
            }
            else
            {
                lse.StatistikStellungId = null;
                lse.PositionOverride = null;
            }
            lse.VerliehenesPersonal = dto.VerliehenesPersonal;
            lse.StammpersonalOeffentlich = dto.StammpersonalOeffentlich;
            lse.FlexProfiling = string.IsNullOrWhiteSpace(dto.FlexProfiling) ? null : dto.FlexProfiling.Trim();
            lse.LeaveEntitlementDays = dto.LeaveEntitlementDays;
        }
        else
        {
            if (dto.Education is < 1 or > 8)
                return BadRequest(new { error = "EDUCATION_UNGUELTIG", message = "Ausbildung: BFS-Code 1–8." });
            if (dto.UniversityDegree is < 1 or > 3)
                return BadRequest(new { error = "DEGREE_UNGUELTIG", message = "Hochschultitel: BFS-Code 1–3." });
            if (dto.PositionOverride is < 1 or > 5)
                return BadRequest(new { error = "POSITION_UNGUELTIG", message = "Berufliche Stellung: BFS-Code 1–5." });
            lse.Education = dto.Education;
            lse.UniversityDegree = dto.UniversityDegree;
            lse.PositionOverride = dto.PositionOverride;
            if (dto.Education is int edu)
            {
                var code = ElmStatistikCodes.Ausbildung(edu, dto.UniversityDegree);
                lse.StatistikAusbildungId = await _db.StatistikAusbildungen.AsNoTracking()
                    .Where(a => a.Code == code).Select(a => (int?)a.Id).FirstOrDefaultAsync();
            }
            else lse.StatistikAusbildungId = null;
            if (dto.PositionOverride is int pos)
            {
                lse.StatistikStellungId = await _db.StatistikStellungen.AsNoTracking()
                    .Where(s => s.LseCode == pos).Select(s => (int?)s.Id).FirstOrDefaultAsync();
            }
            else lse.StatistikStellungId = null;
        }

        lse.PracticedProfession = string.IsNullOrWhiteSpace(dto.PracticedProfession) ? null : dto.PracticedProfession.Trim();
        lse.UpdatedAt = DateTime.Now;
        lse.UpdatedBy = Actor();
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ── Phase 2 (bewusst noch nicht gebaut, Walter-Vorgabe 13.08.2026):
    //    XLS-Export + BFS-Vorschau erst nach geprüfter Datenstruktur. ──────
    [HttpGet("export")]
    public IActionResult Export()
        => StatusCode(501, new { error = "PHASE_2", message = "XLS-Export folgt, sobald Datenstruktur und Zuordnungen geprüft sind (Phase 2)." });

    [HttpGet("vorschau")]
    public IActionResult Vorschau()
        => StatusCode(501, new { error = "PHASE_2", message = "BFS-Vorschau folgt, sobald Datenstruktur und Zuordnungen geprüft sind (Phase 2)." });
}

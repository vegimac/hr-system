using System.Security.Claims;
using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
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
        var lse = await _db.EmployeeLse.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId);
        return Ok(new
        {
            employeeId,
            education = lse?.Education,
            universityDegree = lse?.UniversityDegree,
            positionOverride = lse?.PositionOverride,
            practicedProfession = lse?.PracticedProfession,
            inHouseId = lse?.InHouseId,
        });
    }

    public class EmployeeLseDto
    {
        public int? Education { get; set; }
        public int? UniversityDegree { get; set; }
        public int? PositionOverride { get; set; }
        public string? PracticedProfession { get; set; }
        public string? InHouseId { get; set; }
    }

    [HttpPut("employee/{employeeId:int}")]
    public async Task<IActionResult> PutEmployeeLse(int employeeId, [FromBody] EmployeeLseDto dto)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId)) return NotFound();
        if (dto.Education is < 1 or > 8)
            return BadRequest(new { error = "EDUCATION_UNGUELTIG", message = "Ausbildung: BFS-Code 1–8." });
        if (dto.UniversityDegree is < 1 or > 3)
            return BadRequest(new { error = "DEGREE_UNGUELTIG", message = "Hochschultitel: BFS-Code 1–3." });
        if (dto.PositionOverride is < 1 or > 5)
            return BadRequest(new { error = "POSITION_UNGUELTIG", message = "Berufliche Stellung: BFS-Code 1–5." });
        if ((dto.PracticedProfession?.Length ?? 0) > 255)
            return BadRequest(new { error = "BERUF_ZU_LANG", message = "Ausgeübter Beruf: max. 255 Zeichen." });

        var lse = await _db.EmployeeLse.FirstOrDefaultAsync(x => x.EmployeeId == employeeId);
        if (lse == null) { lse = new EmployeeLse { EmployeeId = employeeId }; _db.EmployeeLse.Add(lse); }
        lse.Education = dto.Education;
        lse.UniversityDegree = dto.UniversityDegree;
        lse.PositionOverride = dto.PositionOverride;
        lse.PracticedProfession = string.IsNullOrWhiteSpace(dto.PracticedProfession) ? null : dto.PracticedProfession.Trim();
        lse.InHouseId = string.IsNullOrWhiteSpace(dto.InHouseId) ? null : dto.InHouseId.Trim();
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

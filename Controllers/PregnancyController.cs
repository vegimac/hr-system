using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Mutterschafts-Verwaltung pro Mitarbeiterin (Walter 10.06.2026).
/// Schwangerschaften werden gespeichert; Fristen werden bei jedem GET aus
/// dem Regelwerk berechnet (nicht gecached) — so wirken Regeländerungen
/// sofort auf alle laufenden Schwangerschaften.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superuser")]
[Route("api/pregnancies")]
public class PregnancyController : HrControllerBase
{
    private readonly PregnancyPdfService _pdf;
    public PregnancyController(AppDbContext db, PregnancyPdfService pdf) : base(db) { _pdf = pdf; }

    // ─── DTOs ──────────────────────────────────────────────────────────────
    public record PregnancyListDto(
        int Id, int EmployeeId, DateOnly Meldedatum, DateOnly ErrechneterTermin,
        DateOnly? Geburtsdatum, string? Bemerkung, bool IsActive,
        DateTime CreatedAt, DateTime? UpdatedAt);

    public record FristDto(
        string Code, string Bezeichnung, string? Beschreibung, string? Gesetz,
        DateOnly Datum, DateOnly? DatumEnde,
        decimal? LohnersatzPct, decimal? MaxBetragProTag, string? StaffelText,
        string Status, bool IstArbeitsverbot, int SortOrder);

    public record PregnancyDetailDto(
        PregnancyListDto Pregnancy,
        IReadOnlyList<FristDto> Fristen,
        KuendigungsschutzDto? Kuendigungsschutz);

    public record KuendigungsschutzDto(DateOnly Von, DateOnly Bis);

    public record CreatePregnancyDto(
        int EmployeeId, DateOnly Meldedatum, DateOnly ErrechneterTermin,
        string? Bemerkung);

    public record UpdatePregnancyDto(
        DateOnly? Meldedatum, DateOnly? ErrechneterTermin, DateOnly? Geburtsdatum,
        string? Bemerkung, bool? IsActive);

    // ─── Endpoints ─────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetByEmployee([FromQuery] int employeeId)
    {
        var list = await _db.EmployeePregnancies
            // Alt-Datenbestand: früher soft-gelöschte (IsActive=false) nicht mehr zeigen.
            .Where(p => p.EmployeeId == employeeId && p.IsActive)
            .OrderByDescending(p => p.ErrechneterTermin)
            .Select(p => new PregnancyListDto(
                p.Id, p.EmployeeId, p.Meldedatum, p.ErrechneterTermin,
                p.Geburtsdatum, p.Bemerkung, p.IsActive,
                p.CreatedAt, p.UpdatedAt))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var p = await _db.EmployeePregnancies.FindAsync(id);
        if (p is null) return NotFound();
        var rules = await _db.PregnancyRules
            .Where(r => r.Aktiv)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
            .ToListAsync();

        var today    = DateOnly.FromDateTime(DateTime.Today);
        var fristen  = rules.Select(r => CalcFrist(r, p, today)).ToList();

        // Kündigungsschutz-Block separat (Anzeige als Banner): von = Meldedatum,
        // bis = KUENDIG_SCHUTZ-Datum (16 Wochen nach Geburt/ET).
        KuendigungsschutzDto? ks = null;
        var kuendigungSchutz = fristen.FirstOrDefault(f => f.Code == "KUENDIG_SCHUTZ");
        if (kuendigungSchutz != null)
            ks = new KuendigungsschutzDto(p.Meldedatum, kuendigungSchutz.Datum);

        var dto = new PregnancyDetailDto(
            new PregnancyListDto(p.Id, p.EmployeeId, p.Meldedatum, p.ErrechneterTermin,
                p.Geburtsdatum, p.Bemerkung, p.IsActive,
                p.CreatedAt, p.UpdatedAt),
            fristen,
            ks);
        return Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePregnancyDto dto)
    {
        var empExists = await _db.Employees.AnyAsync(e => e.Id == dto.EmployeeId);
        if (!empExists) return BadRequest(new { error = "Mitarbeiter nicht gefunden." });

        var p = new EmployeePregnancy
        {
            EmployeeId        = dto.EmployeeId,
            Meldedatum        = dto.Meldedatum,
            ErrechneterTermin = dto.ErrechneterTermin,
            Bemerkung         = dto.Bemerkung,
            IsActive          = true,
            CreatedAt         = DateTime.UtcNow,
        };
        _db.EmployeePregnancies.Add(p);
        await _db.SaveChangesAsync();
        return Ok(new { p.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePregnancyDto dto)
    {
        var p = await _db.EmployeePregnancies.FindAsync(id);
        if (p is null) return NotFound();

        if (dto.Meldedatum           is not null) p.Meldedatum           = dto.Meldedatum.Value;
        if (dto.ErrechneterTermin    is not null) p.ErrechneterTermin    = dto.ErrechneterTermin.Value;
        if (dto.Geburtsdatum         is not null) p.Geburtsdatum         = dto.Geburtsdatum;
        if (dto.Bemerkung            is not null) p.Bemerkung            = dto.Bemerkung;
        if (dto.IsActive             is not null) p.IsActive             = dto.IsActive.Value;
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(p);
    }

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> GetPdf(int id)
    {
        var exists = await _db.EmployeePregnancies.AnyAsync(p => p.Id == id);
        if (!exists) return NotFound();
        var pdf = await _pdf.GenerateAsync(id);
        var emp = await _db.EmployeePregnancies
            .Where(p => p.Id == id)
            .Select(p => new { p.Employee!.FirstName, p.Employee.LastName, p.Employee.EmployeeNumber })
            .FirstOrDefaultAsync();
        var name = emp != null
            ? $"{emp.FirstName}_{emp.LastName}".Replace(" ", "_")
            : $"MA_{id}";
        return File(pdf, "application/pdf", $"Mutterschaft_{name}.pdf");
    }

    /// <summary>
    /// Walter-Vorgabe 13.06.2026: SOFT-Delete. Setzt IsActive=false, hält
    /// den Datensatz aber im System (konsistent mit dem Model-Kommentar
    /// und dem Rest des Systems — Lohnabhängige Daten werden NIE hart
    /// gelöscht). Eine versteckte Schwangerschaft kann später wieder
    /// aktiviert werden, taucht aber nicht mehr in den GETs auf.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        // Walter-Bug 09.07.2026: das frühere Soft-Delete (IsActive=false) liess
        // die Karte im Familie-Tab stehen (GET lieferte auch Inaktive) — nur der
        // Badge verschwand. «Löschen» heisst jetzt wirklich löschen; es gibt
        // keine abhängigen Tabellen (Fristen werden live gerechnet).
        var p = await _db.EmployeePregnancies.FindAsync(id);
        if (p is null) return NotFound();
        _db.EmployeePregnancies.Remove(p);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ─── Fristenberechnung ────────────────────────────────────────────────
    /// <summary>
    /// Walter-Vorgabe 13.06.2026: Berechnung an `PregnancyFristCalculator`
    /// ausgelagert. Hier nur noch die Übersetzung Berechnungs-Tupel →
    /// FristDto (mit Regel-Metadaten für die UI).
    /// </summary>
    private static FristDto CalcFrist(PregnancyRule r, EmployeePregnancy p, DateOnly today)
    {
        var f = PregnancyFristCalculator.Calculate(r, p, today);
        return new FristDto(
            r.Code, r.Bezeichnung, r.Beschreibung, r.Gesetz,
            f.Datum, f.DatumEnde,
            r.LohnersatzPct, r.MaxBetragProTag, r.StaffelText,
            f.Status, r.IstArbeitsverbot, r.SortOrder);
    }
}

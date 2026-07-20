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
        DateTime CreatedAt, DateTime? UpdatedAt,
        // Walter 16.07.2026: Beginn Schwangerschaft = ET − 280 Tage (berechnet).
        DateOnly SchwangerschaftsBeginn,
        // Walter 20.07.2026: verknüpfte Arztbestätigung (errechneter Termin).
        int? ArztbestaetigungDokumentId,
        string? ArztbestaetigungDokumentName);

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
        string? Bemerkung,
        int? ArztbestaetigungDokumentId);

    public record UpdatePregnancyDto(
        DateOnly? Meldedatum, DateOnly? ErrechneterTermin, DateOnly? Geburtsdatum,
        string? Bemerkung, bool? IsActive,
        // Nur setzen wenn true — sonst bleibt die Verknüpfung (Geburt-PUT
        // sendet kein Dokument und darf die FK nicht löschen).
        bool? SetArztbestaetigungDokument,
        int? ArztbestaetigungDokumentId);

    // ─── Endpoints ─────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetByEmployee([FromQuery] int employeeId)
    {
        // Datums-Regelwerk 13.07.2026: erst roh laden, DANN im Speicher mappen
        // (AddDays für den Schwangerschafts-Beginn gehört nicht in die EF-Projektion).
        var rows = await _db.EmployeePregnancies
            .AsNoTracking()
            .Include(p => p.ArztbestaetigungDokument)
            // Alt-Datenbestand: früher soft-gelöschte (IsActive=false) nicht mehr zeigen.
            .Where(p => p.EmployeeId == employeeId && p.IsActive)
            .OrderByDescending(p => p.ErrechneterTermin)
            .ToListAsync();
        var list = rows.Select(ToListDto).ToList();
        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var p = await _db.EmployeePregnancies
            .Include(x => x.ArztbestaetigungDokument)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();
        var rules = await _db.PregnancyRules
            .Where(r => r.Aktiv)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
            .ToListAsync();

        var today    = DateOnly.FromDateTime(DateTime.Today);
        var fristen  = rules.Select(r => CalcFrist(r, p, today)).ToList();

        // Kündigungsschutz-Block separat (Anzeige als Banner) — Walter-Vorgabe
        // 16.07.2026: von = BEGINN der Schwangerschaft (ET − 280 Tage, nicht
        // Meldedatum), bis = 16 Wochen nach Geburt (effektives Geburtsdatum,
        // solange keines erfasst ist: errechneter Termin). Direkt berechnet,
        // NICHT aus dem konfigurierbaren Regelwerk — der Schutz nach OR 336c
        // ist gesetzlich fix.
        var ks = new KuendigungsschutzDto(
            PregnancyFristCalculator.SchwangerschaftsBeginn(p),
            PregnancyFristCalculator.KuendigungsschutzEnde(p));

        var dto = new PregnancyDetailDto(ToListDto(p), fristen, ks);
        return Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePregnancyDto dto)
    {
        var empExists = await _db.Employees.AnyAsync(e => e.Id == dto.EmployeeId);
        if (!empExists) return BadRequest(new { error = "Mitarbeiter nicht gefunden." });

        var dokErr = await ValidateDokAsync(dto.EmployeeId, dto.ArztbestaetigungDokumentId);
        if (dokErr is not null) return dokErr;

        var p = new EmployeePregnancy
        {
            EmployeeId                 = dto.EmployeeId,
            Meldedatum                 = dto.Meldedatum,
            ErrechneterTermin          = dto.ErrechneterTermin,
            Bemerkung                  = dto.Bemerkung,
            ArztbestaetigungDokumentId = dto.ArztbestaetigungDokumentId,
            IsActive                   = true,
            CreatedAt                  = DateTime.Now,
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
        if (dto.SetArztbestaetigungDokument == true)
        {
            var dokErr = await ValidateDokAsync(p.EmployeeId, dto.ArztbestaetigungDokumentId);
            if (dokErr is not null) return dokErr;
            p.ArztbestaetigungDokumentId = dto.ArztbestaetigungDokumentId;
        }
        p.UpdatedAt = DateTime.Now;
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

    private static PregnancyListDto ToListDto(EmployeePregnancy p) => new(
        p.Id, p.EmployeeId, p.Meldedatum, p.ErrechneterTermin,
        p.Geburtsdatum, p.Bemerkung, p.IsActive,
        p.CreatedAt, p.UpdatedAt,
        PregnancyFristCalculator.SchwangerschaftsBeginn(p),
        p.ArztbestaetigungDokumentId,
        p.ArztbestaetigungDokument?.FilenameOriginal);

    private async Task<IActionResult?> ValidateDokAsync(int employeeId, int? dokId)
    {
        if (dokId is null) return null;
        var ok = await _db.EmployeeDokumente
            .AnyAsync(d => d.Id == dokId.Value && d.EmployeeId == employeeId);
        if (!ok)
            return BadRequest(new { error = "Dokument gehört nicht zu diesem Mitarbeiter." });
        return null;
    }
}

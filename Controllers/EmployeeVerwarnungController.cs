using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Verwarnungs-Verlauf pro MA (Walter-Vorgabe 14.07.2026). GF (user) darf
/// erfassen und bearbeiten — er spricht die Verwarnung aus. Löschen =
/// ECHTES Löschen (Walter-Entscheid 15.07.2026, admin/superuser) — kein
/// Storno-Behalten mehr. Kein Lohnbezug → im EditLock-Audit whitelisted.
/// </summary>
[Authorize(Roles = "admin,superuser,user")]
[ApiController]
[Route("api/verwarnungen")]
public class EmployeeVerwarnungController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly HrSystem.Services.VerwarnungPdfService _pdf;
    public EmployeeVerwarnungController(AppDbContext db, HrSystem.Services.VerwarnungPdfService pdf)
    { _db = db; _pdf = pdf; }

    /// <summary>Die Ankreuz-Gründe des heutigen Papier-Formulars (14.07.2026).</summary>
    public static readonly string[] StandardGruende =
    {
        "Hygienevorschrift missachtet",
        "Qualitätsvorschrift missachtet",
        "Lebensmittelsicherheit missachtet",
        "Arbeitssicherheit missachtet",
        "Personenschutz missachtet",
        "Uniformrichtlinien missachtet",
        "Umgang mit Geld nicht korrekt",
        "Umgang mit Material nicht korrekt",
        "Umgang mit Geräten nicht korrekt",
        "Umgang mit Gästen nicht korrekt",
        "Umgang mit Vorgesetzten nicht korrekt",
        "Umgang mit Mitarbeitern nicht korrekt",
        "Disziplin",
        "Unentschuldigt zu spät erschienen",
        "Unentschuldigt nicht zur Arbeit erschienen",
        "Diebstahl"
    };

    public class VerwarnungDto
    {
        public DateOnly? Datum { get; set; }
        /// <summary>VERWARNUNG_1 | VERWARNUNG_2 | LETZTE</summary>
        public string Stufe { get; set; } = "VERWARNUNG_1";
        /// <summary>Angekreuzte Standard-Gründe (Mehrfachauswahl).</summary>
        public List<string> Gruende { get; set; } = new();
        public string? Beschreibung { get; set; }
        public int? DokumentId { get; set; }
    }

    public class StornoDto { public string? Grund { get; set; } }

    [HttpGet("gruende")]
    public IActionResult GetGruende() => Ok(StandardGruende);

    /// <summary>Find-or-create des Dokument-Typs «Verwarnung» — damit der
    /// Upload aus dem Verwarnungs-Modal ohne manuelle Typ-Wahl läuft.</summary>
    [HttpGet("dokument-typ")]
    public async Task<IActionResult> GetDokumentTyp()
    {
        var typ = await _db.DokumentTypen
            .FirstOrDefaultAsync(t => t.Name.ToLower().StartsWith("verwarnung"));
        if (typ == null)
        {
            // Kategorie: bevorzugt eine bestehende «Personal…»-Kategorie,
            // sonst die erste aktive.
            var kat = await _db.DokumentKategorien
                          .Where(k => k.Aktiv)
                          .OrderBy(k => k.Name.ToLower().Contains("personal") ? 0 : 1)
                          .ThenBy(k => k.SortOrder)
                          .FirstOrDefaultAsync();
            if (kat == null)
            {
                kat = new DokumentKategorie { Name = "Personalakte", SortOrder = 50 };
                _db.DokumentKategorien.Add(kat);
                await _db.SaveChangesAsync();
            }
            typ = new DokumentTyp { KategorieId = kat.Id, Name = "Verwarnung", SortOrder = 60 };
            _db.DokumentTypen.Add(typ);
            await _db.SaveChangesAsync();
        }
        return Ok(new { typ.Id, typ.Name });
    }

    [HttpGet("by-employee/{empId:int}")]
    public async Task<IActionResult> GetByEmployee(int empId)
    {
        var rows = await _db.EmployeeVerwarnungen.AsNoTracking()
            .Where(v => v.EmployeeId == empId)
            .OrderByDescending(v => v.Datum).ThenByDescending(v => v.Id)
            .Select(v => new
            {
                v.Id,
                datum = v.Datum,
                v.Stufe,
                v.Gruende,
                v.Beschreibung,
                v.DokumentId,
                dokumentName = v.Dokument != null ? v.Dokument.FilenameOriginal : null,
                v.Storniert,
                v.StornoGrund,
                v.ErstelltVon,
                erstelltAm = v.ErstelltAm
            })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpPost("{empId:int}")]
    public async Task<IActionResult> Create(int empId, [FromBody] VerwarnungDto dto)
    {
        var empExists = await _db.Employees.AnyAsync(e => e.Id == empId);
        if (!empExists) return NotFound(new { error = "EMP_NOT_FOUND" });

        var err = Validate(dto, out var stufe);
        if (err != null) return BadRequest(err);

        // Dokument OPTIONAL bei der Erfassung (Walter 15.07.2026, Formular-
        // Workflow: erfassen → Formular drucken → unterschreiben → Scan
        // nachreichen). Ohne Dokument zeigt die Zeile «Schreiben fehlt» in Rot.
        if (dto.DokumentId != null)
        {
            var docOk = await _db.EmployeeDokumente
                .AnyAsync(d => d.Id == dto.DokumentId.Value && d.EmployeeId == empId);
            if (!docOk)
                return BadRequest(new { error = "DOKUMENT_UNGUELTIG", message = "Das Dokument gehört nicht zu diesem Mitarbeiter." });
        }

        var v = new EmployeeVerwarnung
        {
            EmployeeId   = empId,
            Datum        = dto.Datum ?? DateOnly.FromDateTime(DateTime.Today),
            Stufe        = stufe,
            Gruende      = dto.Gruende.Count > 0 ? string.Join("\n", dto.Gruende) : null,
            Beschreibung = string.IsNullOrWhiteSpace(dto.Beschreibung) ? null : dto.Beschreibung.Trim(),
            DokumentId   = dto.DokumentId,
            ErstelltVon  = await GetActorNameAsync(),
            ErstelltAm   = DateTime.Now
        };
        _db.EmployeeVerwarnungen.Add(v);
        await _db.SaveChangesAsync();
        return Ok(new { v.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] VerwarnungDto dto)
    {
        var v = await _db.EmployeeVerwarnungen.FirstOrDefaultAsync(x => x.Id == id);
        if (v == null) return NotFound(new { error = "NOT_FOUND" });
        if (v.Storniert)
            return Conflict(new { error = "STORNIERT", message = "Stornierte Verwarnungen können nicht mehr bearbeitet werden." });

        var err = Validate(dto, out var stufe);
        if (err != null) return BadRequest(err);

        if (dto.DokumentId != null)
        {
            var docOk = await _db.EmployeeDokumente
                .AnyAsync(d => d.Id == dto.DokumentId.Value && d.EmployeeId == v.EmployeeId);
            if (!docOk)
                return BadRequest(new { error = "DOKUMENT_UNGUELTIG", message = "Das Dokument gehört nicht zu diesem Mitarbeiter." });
            v.DokumentId = dto.DokumentId;
        }

        if (dto.Datum.HasValue) v.Datum = dto.Datum.Value;
        v.Stufe        = stufe;
        v.Gruende      = dto.Gruende.Count > 0 ? string.Join("\n", dto.Gruende) : null;
        v.Beschreibung = string.IsNullOrWhiteSpace(dto.Beschreibung) ? null : dto.Beschreibung.Trim();
        v.GeaendertAm  = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { v.Id });
    }

    /// <summary>Verwarnungs-Formular als PDF (Walter 15.07.2026) — vorausgefüllt
    /// mit Stufe/Gründen/Bemerkung aus dem Modal. Speichert NICHTS: drucken,
    /// unterschreiben lassen (MA + Schichtführer), Scan als Dokument nachreichen.</summary>
    [HttpPost("{empId:int}/formular-pdf")]
    public async Task<IActionResult> FormularPdf(int empId, [FromBody] VerwarnungDto dto)
    {
        var e = await _db.Employees.AsNoTracking()
            .Where(x => x.Id == empId)
            .Select(x => new { x.FirstName, x.LastName, x.EmployeeNumber })
            .FirstOrDefaultAsync();
        if (e == null) return NotFound(new { error = "EMP_NOT_FOUND" });

        var err = Validate(dto, out var stufe);
        if (err != null) return BadRequest(err);

        // Filiale des juengsten Vertrags fuer den Briefkopf-Text.
        var cp = await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == empId && em.CompanyProfileId != null)
            .OrderByDescending(em => em.IsActive).ThenByDescending(em => em.ContractStartDate)
            .Select(em => em.CompanyProfile)
            .FirstOrDefaultAsync();

        string stufeLabel = stufe switch
        {
            "VERWARNUNG_2" => "2. Verwarnung",
            "LETZTE"       => "Letzte Verwarnung (Kündigungsandrohung)",
            _              => "1. Verwarnung"
        };

        try
        {
            var input = new HrSystem.Services.VerwarnungFormularInput(
                CompanyName:      cp?.CompanyName ?? "Schaub Restaurants GmbH",
                RestaurantName:   cp?.BranchName ?? cp?.FullDisplayName ?? "",
                MaName:           ($"{e.FirstName} {e.LastName}").Trim(),
                EmployeeNumber:   e.EmployeeNumber,
                Datum:            dto.Datum.HasValue ? dto.Datum.Value.ToDateTime(TimeOnly.MinValue) : DateTime.Today,
                StufeLabel:       stufeLabel,
                StufeKritisch:    stufe == "LETZTE",
                AlleGruende:      StandardGruende,
                GewaehlteGruende: dto.Gruende,
                Beschreibung:     dto.Beschreibung
            );
            var bytes = _pdf.Generate(input);
            return File(bytes, "application/pdf",
                $"Verwarnung_{e.LastName}_{e.FirstName}_{input.Datum:yyyyMMdd}.pdf".Replace(" ", "_"));
        }
        catch (Exception ex)
        {
            // Klartext statt stummem 500 (Lehre QST-Vorschlag 13.07.2026).
            return StatusCode(500, new { error = "FORMULAR_FEHLGESCHLAGEN",
                message = ex.GetBaseException().Message });
        }
    }

    /// <summary>Echtes Löschen (Walter-Entscheid 15.07.2026).
    /// Das verknüpfte Dokument bleibt in der Personalakte erhalten.
    /// GF (user) inkl. (Walter 20.07.2026).</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin,superuser,user")]
    public async Task<IActionResult> Delete(int id)
    {
        var v = await _db.EmployeeVerwarnungen.FirstOrDefaultAsync(x => x.Id == id);
        if (v == null) return NotFound(new { error = "NOT_FOUND" });
        _db.EmployeeVerwarnungen.Remove(v);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }

    private static object? Validate(VerwarnungDto dto, out string stufe)
    {
        stufe = (dto.Stufe ?? "VERWARNUNG_1").Trim().ToUpperInvariant();
        if (stufe is not ("VERWARNUNG_1" or "VERWARNUNG_2" or "LETZTE"))
            return new { error = "STUFE_UNGUELTIG", message = "Stufe muss VERWARNUNG_1, VERWARNUNG_2 oder LETZTE sein." };
        dto.Gruende = (dto.Gruende ?? new()).Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim()).Distinct().ToList();
        if (dto.Gruende.Count == 0 && string.IsNullOrWhiteSpace(dto.Beschreibung))
            return new { error = "GRUND_FEHLT", message = "Mindestens einen Grund ankreuzen oder eine Beschreibung erfassen." };
        return null;
    }

    private async Task<string> GetActorNameAsync()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(idStr, out var uid))
        {
            var u = await _db.AppUsers.AsNoTracking()
                .Where(x => x.Id == uid)
                .Select(x => new { x.FirstName, x.LastName, x.Username })
                .FirstOrDefaultAsync();
            if (u != null)
            {
                var full = $"{u.FirstName} {u.LastName}".Trim();
                return string.IsNullOrWhiteSpace(full) ? (u.Username ?? "?") : full;
            }
        }
        return "?";
    }
}

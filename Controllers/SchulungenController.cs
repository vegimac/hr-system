using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Schulungen &amp; Ausbildungen (Walter-Vorgabe 01.10.2026): Verzeichnis (System →
/// Verzeichnisse &amp; Vorgaben), Historie pro MA (Tab «Verfügbarkeit / Training») und
/// die read-only Übersicht «Schulungen» unter Auswertungen. Genau ein Nachweis pro
/// Eintrag: «in FRED» ohne Dokument oder ein verknüpftes Dokument.
/// </summary>
[ApiController]
public class SchulungenController : ControllerBase
{
    private readonly AppDbContext _db;
    public SchulungenController(AppDbContext db) { _db = db; }

    private static DateOnly Heute => DateOnly.FromDateTime(DateTime.Today);

    // ══ Verzeichnis ════════════════════════════════════════════════════════

    public class TypDto
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
        public int? RefreshMonate { get; set; }
        public int? FristTage { get; set; }
        public string? Zielgruppe { get; set; }
        public bool FredMoeglich { get; set; }
        public int? WarnenAbTage { get; set; }
        public bool Aktiv { get; set; } = true;
        public int SortOrder { get; set; }
        public string? Beschreibung { get; set; }
    }

    [HttpGet("api/schulung-typen")]
    public async Task<IActionResult> GetTypen()
    {
        var anzahl = await _db.EmployeeSchulungen.AsNoTracking()
            .GroupBy(s => s.SchulungTypId)
            .Select(g => new { g.Key, n = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.n);
        var typen = await _db.SchulungTypen.AsNoTracking()
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync();
        return Ok(typen.Select(t => new
        {
            t.Id, t.Code, t.Name, t.RefreshMonate, t.FristTage, t.Zielgruppe,
            zielgruppeLabel = SchulungStatus.ZielgruppeLabel(t.Zielgruppe),
            t.FredMoeglich, t.WarnenAbTage, t.Aktiv, t.SortOrder, t.Beschreibung,
            anzahlEintraege = anzahl.TryGetValue(t.Id, out var n) ? n : 0,
        }));
    }

    private string? PruefeTyp(TypDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Bitte einen Namen eingeben.";
        if (dto.RefreshMonate is < 1 or > 999) return "Auffrischung: 1 bis 999 Monate.";
        if (dto.FristTage is < 1 or > 3650) return "Frist ab Eintritt: mindestens 1 Tag.";
        if (dto.WarnenAbTage is < 0 or > 3650) return "«Warnen ab» muss zwischen 0 und 3650 Tagen liegen.";
        var z = (dto.Zielgruppe ?? "ALLE").Trim().ToUpperInvariant();
        if (!SchulungStatus.Zielgruppen.Contains(z)) return $"Unbekannte Zielgruppe «{dto.Zielgruppe}».";
        return null;
    }

    private static string CodeAusName(string name)
    {
        var map = new Dictionary<char, string> { ['ä'] = "AE", ['ö'] = "OE", ['ü'] = "UE", ['Ä'] = "AE", ['Ö'] = "OE", ['Ü'] = "UE", ['ß'] = "SS" };
        var sb = new System.Text.StringBuilder();
        foreach (var c in name.Trim())
        {
            if (map.TryGetValue(c, out var r)) sb.Append(r);
            else if (char.IsLetterOrDigit(c) && c < 128) sb.Append(char.ToUpperInvariant(c));
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        var code = sb.ToString().Trim('_');
        return code.Length > 30 ? code[..30] : (code.Length == 0 ? "SCHULUNG" : code);
    }

    private static void Uebernehmen(SchulungTyp t, TypDto dto)
    {
        t.Name = dto.Name!.Trim();
        t.RefreshMonate = dto.RefreshMonate;
        t.FristTage = dto.FristTage;
        t.Zielgruppe = (dto.Zielgruppe ?? "ALLE").Trim().ToUpperInvariant();
        t.FredMoeglich = dto.FredMoeglich;
        t.WarnenAbTage = dto.WarnenAbTage;
        t.Aktiv = dto.Aktiv;
        t.SortOrder = dto.SortOrder;
        t.Beschreibung = string.IsNullOrWhiteSpace(dto.Beschreibung) ? null : dto.Beschreibung.Trim();
        t.UpdatedAt = DateTime.Now;
    }

    [Authorize(Roles = "admin")]
    [HttpPost("api/schulung-typen")]
    public async Task<IActionResult> CreateTyp([FromBody] TypDto dto)
    {
        var fehler = PruefeTyp(dto);
        if (fehler != null) return BadRequest(new { error = "UNGUELTIG", message = fehler });

        var basis = string.IsNullOrWhiteSpace(dto.Code) ? CodeAusName(dto.Name!) : CodeAusName(dto.Code);
        var code = basis;
        for (int i = 2; await _db.SchulungTypen.AnyAsync(t => t.Code == code); i++) code = $"{basis}_{i}";

        var t = new SchulungTyp { Code = code, CreatedAt = DateTime.Now };
        Uebernehmen(t, dto);
        if (t.SortOrder == 0)
            t.SortOrder = (await _db.SchulungTypen.MaxAsync(x => (int?)x.SortOrder) ?? 0) + 10;
        _db.SchulungTypen.Add(t);
        await _db.SaveChangesAsync();
        return Ok(new { t.Id, t.Code });
    }

    [Authorize(Roles = "admin")]
    [HttpPut("api/schulung-typen/{id:int}")]
    public async Task<IActionResult> UpdateTyp(int id, [FromBody] TypDto dto)
    {
        var t = await _db.SchulungTypen.FindAsync(id);
        if (t == null) return NotFound();
        var fehler = PruefeTyp(dto);
        if (fehler != null) return BadRequest(new { error = "UNGUELTIG", message = fehler });
        Uebernehmen(t, dto);
        await _db.SaveChangesAsync();
        return Ok();
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("api/schulung-typen/{id:int}")]
    public async Task<IActionResult> DeleteTyp(int id)
    {
        var t = await _db.SchulungTypen.FindAsync(id);
        if (t == null) return NotFound();
        if (await _db.EmployeeSchulungen.AnyAsync(s => s.SchulungTypId == id))
            return Conflict(new { error = "IN_VERWENDUNG",
                message = "Für diese Schulung gibt es Einträge bei Mitarbeitenden. Statt löschen auf «inaktiv» setzen." });
        _db.SchulungTypen.Remove(t);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ══ Mitarbeiter ════════════════════════════════════════════════════════

    [HttpGet("api/schulungen/dokument-typ")]
    public async Task<IActionResult> GetDokumentTyp()
    {
        var typ = await SchulungAuswertung.EnsureDokumentTypAsync(_db);
        return Ok(new { typ.Id, typ.Name, kategorieId = typ.KategorieId });
    }

    private static object TypStandJson(SchulungAuswertung.TypStand s) => new
    {
        typId = s.Typ.Id,
        code = s.Typ.Code,
        name = s.Typ.Name,
        refreshMonate = s.Typ.RefreshMonate,
        fristTage = s.Typ.FristTage,
        zielgruppe = s.Typ.Zielgruppe,
        fredMoeglich = s.Typ.FredMoeglich,
        zustand = s.Ergebnis.Zustand.ToString(),
        zustandLabel = SchulungStatus.ZustandLabel(s.Ergebnis.Zustand),
        letzterId = s.Ergebnis.Letzter?.Id,
        letztesDatum = s.Ergebnis.Letzter?.Datum,
        letzteArt = s.Ergebnis.Letzter?.Art,
        gueltigBis = s.Ergebnis.GueltigBis,
        faelligAm = s.Ergebnis.FaelligAm,
        tageBis = s.Ergebnis.TageBis,
    };

    /// <summary>Training-Block im MA-Tab: Stand jeder Schulung + Historie.</summary>
    [HttpGet("api/schulungen/employee/{empId:int}")]
    public async Task<IActionResult> GetEmployee(int empId)
    {
        var stand = (await SchulungAuswertung.LadeAsync(_db, Heute, new[] { empId })).FirstOrDefault();
        if (stand == null) return NotFound();

        var historie = await (
            from s in _db.EmployeeSchulungen.AsNoTracking()
            join t in _db.SchulungTypen.AsNoTracking() on s.SchulungTypId equals t.Id
            join d in _db.EmployeeDokumente.AsNoTracking() on s.DokumentId equals d.Id into dj
            from d in dj.DefaultIfEmpty()
            where s.EmployeeId == empId
            orderby s.Datum descending, s.Id descending
            select new
            {
                s.Id, s.SchulungTypId, typName = t.Name, typCode = t.Code,
                s.Datum, s.Art, s.DokumentId,
                dokumentName = d != null ? (d.Bemerkung ?? d.FilenameOriginal) : null,
                s.Titel, s.Bemerkung, s.ErfasstVon, s.ErfasstAm,
            }).ToListAsync();

        return Ok(new
        {
            kontext = new
            {
                modell = stand.Kontext.Modell,
                funktion = stand.Kontext.JobGroupCode,
                einstufung = stand.Kontext.EducationLevelCode,
                eintritt = stand.Kontext.Eintritt,
            },
            schulungen = stand.Staende
                .Where(s => s.Ergebnis.Zustand != SchulungStatus.Zustand.NichtBetroffen || s.Ergebnis.Letzter != null)
                .Select(TypStandJson),
            historie,
        });
    }

    public class EintragDto
    {
        public int SchulungTypId { get; set; }
        public DateOnly? Datum { get; set; }
        /// <summary>FRED oder DOKUMENT.</summary>
        public string? Art { get; set; }
        public int? DokumentId { get; set; }
        public string? Titel { get; set; }
        public string? Bemerkung { get; set; }
    }

    private async Task<string?> ActorAsync()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(idStr, out var uid))
        {
            var u = await _db.AppUsers.Where(x => x.Id == uid)
                .Select(x => new { x.FirstName, x.LastName, x.Username }).FirstOrDefaultAsync();
            if (u != null)
            {
                var full = $"{u.FirstName} {u.LastName}".Trim();
                return string.IsNullOrWhiteSpace(full) ? u.Username : full;
            }
        }
        return User.FindFirstValue(ClaimTypes.Name);
    }

    /// <summary>Dokument gehört dem MA und hängt an keinem anderen Schulungs-Eintrag (1:1).</summary>
    private async Task<IActionResult?> PruefeDokumentAsync(int empId, int dokumentId, int? eigenerEintrag)
    {
        var gehoert = await _db.EmployeeDokumente.AnyAsync(d => d.Id == dokumentId && d.EmployeeId == empId);
        if (!gehoert) return BadRequest(new { error = "DOKUMENT", message = "Das Dokument gehört nicht zu diesem Mitarbeiter." });
        var belegt = await _db.EmployeeSchulungen.AnyAsync(s => s.DokumentId == dokumentId && s.Id != (eigenerEintrag ?? 0));
        if (belegt) return Conflict(new { error = "DOKUMENT_BELEGT",
            message = "Dieses Dokument ist bereits Nachweis einer anderen Schulung. Pro Eintrag gehört genau ein eigenes Dokument." });
        return null;
    }

    [HttpPost("api/schulungen/employee/{empId:int}")]
    public async Task<IActionResult> CreateEintrag(int empId, [FromBody] EintragDto dto)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == empId)) return NotFound();
        var typ = await _db.SchulungTypen.FindAsync(dto.SchulungTypId);
        if (typ == null) return BadRequest(new { error = "TYP", message = "Schulung nicht gefunden." });
        if (dto.Datum == null) return BadRequest(new { error = "DATUM", message = "Bitte das Datum der Schulung angeben." });
        if (dto.Datum > Heute) return BadRequest(new { error = "DATUM", message = "Das Datum liegt in der Zukunft — erfasst wird, was erledigt ist." });

        var art = (dto.Art ?? "").Trim().ToUpperInvariant();
        if (art == "FRED")
        {
            if (!typ.FredMoeglich)
                return BadRequest(new { error = "KEIN_FRED", message = $"«{typ.Name}» kann nicht in FRED abgehakt werden — bitte den Nachweis hochladen." });
            dto.DokumentId = null;
        }
        else if (art == "DOKUMENT")
        {
            if (dto.DokumentId == null)
                return BadRequest(new { error = "DOKUMENT", message = "Bitte den Nachweis hochladen oder ein Dokument wählen." });
            var f = await PruefeDokumentAsync(empId, dto.DokumentId.Value, null);
            if (f != null) return f;
        }
        else return BadRequest(new { error = "ART", message = "Nachweis: «in FRED» oder «Dokument»." });

        var e = new EmployeeSchulung
        {
            EmployeeId = empId,
            SchulungTypId = typ.Id,
            Datum = dto.Datum.Value,
            Art = art,
            DokumentId = dto.DokumentId,
            Titel = string.IsNullOrWhiteSpace(dto.Titel) ? null : dto.Titel.Trim(),
            Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim(),
            ErfasstVon = await ActorAsync(),
            ErfasstAm = DateTime.Now,
        };
        _db.EmployeeSchulungen.Add(e);
        await _db.SaveChangesAsync();
        return Ok(new { e.Id });
    }

    [HttpPut("api/schulungen/{id:int}")]
    public async Task<IActionResult> UpdateEintrag(int id, [FromBody] EintragDto dto)
    {
        var e = await _db.EmployeeSchulungen.FindAsync(id);
        if (e == null) return NotFound();
        if (dto.Datum == null) return BadRequest(new { error = "DATUM", message = "Bitte das Datum der Schulung angeben." });
        if (dto.Datum > Heute) return BadRequest(new { error = "DATUM", message = "Das Datum liegt in der Zukunft." });
        e.Datum = dto.Datum.Value;
        e.Titel = string.IsNullOrWhiteSpace(dto.Titel) ? null : dto.Titel.Trim();
        e.Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        await _db.SaveChangesAsync();
        return Ok();
    }

    public class DokumentDto
    {
        public int DokumentId { get; set; }
        /// <summary>true = bestehendes Dokument bewusst ersetzen (nach Rückfrage).</summary>
        public bool Ersetzen { get; set; }
    }

    /// <summary>Nachweis anhängen oder (nach Rückfrage) austauschen. Aus «in FRED» wird «Dokument».</summary>
    [HttpPatch("api/schulungen/{id:int}/dokument")]
    public async Task<IActionResult> SetDokument(int id, [FromBody] DokumentDto dto)
    {
        var e = await _db.EmployeeSchulungen.FindAsync(id);
        if (e == null) return NotFound();
        if (e.DokumentId == dto.DokumentId) return Ok();
        if (e.DokumentId != null && !dto.Ersetzen)
            return Conflict(new { error = "ERSETZEN_RUECKFRAGE",
                message = "Dieser Eintrag hat schon einen Nachweis. Soll er ersetzt werden? Das alte Dokument bleibt in der Ablage." });
        var f = await PruefeDokumentAsync(e.EmployeeId, dto.DokumentId, e.Id);
        if (f != null) return f;
        e.DokumentId = dto.DokumentId;
        e.Art = "DOKUMENT";
        await _db.SaveChangesAsync();
        return Ok();
    }

    [Authorize(Roles = "admin,superuser")]
    [HttpDelete("api/schulungen/{id:int}")]
    public async Task<IActionResult> DeleteEintrag(int id)
    {
        var e = await _db.EmployeeSchulungen.FindAsync(id);
        if (e == null) return NotFound();
        _db.EmployeeSchulungen.Remove(e);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ══ Übersicht (Auswertungen → Schulungen) ══════════════════════════════

    /// <summary>
    /// Read-only Matrix: alle MA mit laufendem/künftigem Vertrag × alle Schulungen.
    /// GF (Rolle user) sieht nur die eigenen Filialen. Bearbeitet wird im MA.
    /// </summary>
    [HttpGet("api/schulungen/uebersicht")]
    public async Task<IActionResult> Uebersicht([FromQuery] int? companyProfileId)
    {
        List<int>? nurFilialen = null;
        if (!(User.IsInRole("admin") || User.IsInRole("superuser")))
        {
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(idStr, out var uid);
            nurFilialen = await _db.UserBranchAccesses.AsNoTracking()
                .Where(a => a.UserId == uid).Select(a => a.CompanyProfileId).ToListAsync();
        }

        var typen = await SchulungAuswertung.TypenAsync(_db);
        var mas = await SchulungAuswertung.LadeAsync(_db, Heute, null, companyProfileId, nurFilialen);
        var ids = mas.Select(m => m.EmployeeId).ToList();
        var eidSso = await _db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.Eid, e.Sso }).ToDictionaryAsync(e => e.Id);
        var branches = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.RestaurantCode, c.City, c.BranchName, c.WorkLocation })
            .ToDictionaryAsync(c => c.Id);

        return Ok(new
        {
            typen = typen.Select(t => new { t.Id, t.Code, t.Name, t.RefreshMonate, t.Zielgruppe }),
            zeilen = mas.Select(m => new
            {
                employeeId = m.EmployeeId,
                vorname = m.Vorname,
                nachname = m.Nachname,
                employeeNumber = m.EmployeeNumber,
                companyProfileId = m.CompanyProfileId,
                filiale = m.CompanyProfileId.HasValue && branches.TryGetValue(m.CompanyProfileId.Value, out var b)
                    ? (!string.IsNullOrWhiteSpace(b.WorkLocation) ? b.WorkLocation : (b.City ?? b.BranchName)) : null,
                modell = m.Kontext.Modell,
                funktion = m.Kontext.JobGroupCode,
                einstufung = m.Kontext.EducationLevelCode,
                eid = eidSso.TryGetValue(m.EmployeeId, out var x) ? x.Eid : null,
                sso = eidSso.TryGetValue(m.EmployeeId, out var y) ? y.Sso : null,
                zellen = m.Staende.Select(TypStandJson),
            }),
        });
    }
}

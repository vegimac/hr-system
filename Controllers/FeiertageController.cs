using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Feiertage global (Walter 07.10.2026): Pflege nur in Systemeinstellungen → Lohn-Stammdaten →
/// «Feiertage» (nur admin). Filial-Detail und Manager-Dienstplan zeigen sie nur an.
/// Tabelle dienstplan_feiertag (national / Kanton / Filiale), dazu «dem Sonntag gleichgestellt».
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superuser,user")]
[Route("api/feiertage")]
public class FeiertageController : HrControllerBase
{
    public FeiertageController(AppDbContext db) : base(db) { }

    record Filiale(int Id, string? KantonCode, string Name);

    static string? Kanton(string? k) => string.IsNullOrWhiteSpace(k) ? null : k.Trim().ToUpperInvariant();

    async Task<List<Filiale>> FilialenAsync() =>
        (await _db.CompanyProfiles.AsNoTracking()
            .Select(x => new { x.Id, x.KantonCode, x.BranchName, x.RestaurantCode }).ToListAsync())
        .Select(c => new Filiale(c.Id, Kanton(c.KantonCode), $"{c.RestaurantCode} {c.BranchName}".Trim()))
        .OrderBy(f => f.Name).ToList();

    async Task<List<DienstplanFeiertag>> JahrAsync(int jahr)
    {
        var von = new DateOnly(jahr, 1, 1);
        var bis = new DateOnly(jahr, 12, 31);
        return await _db.DienstplanFeiertage.AsNoTracking()
            .Where(x => x.Datum >= von && x.Datum <= bis).OrderBy(x => x.Datum).ToListAsync();
    }

    /// <summary>Gilt der Eintrag im Kanton? (national, Kanton selbst oder Filiale in diesem Kanton)</summary>
    static bool GiltImKanton(DienstplanFeiertag f, string kanton, Dictionary<int, Filiale> filialen) => f.Scope switch
    {
        "NATIONAL" => true,
        "KANTON"   => string.Equals(f.KantonCode, kanton, StringComparison.OrdinalIgnoreCase),
        "FILIALE"  => f.CompanyProfileId is int cp && filialen.TryGetValue(cp, out var fi) && fi.KantonCode == kanton,
        _ => false,
    };

    /// <summary>Feiertage einer Filiale (Anzeige im Filial-Detail).</summary>
    [HttpGet]
    public async Task<IActionResult> Liste([FromQuery] int companyProfileId, [FromQuery] int? year)
    {
        if (companyProfileId <= 0) return BadRequest(new { error = "FILIALE_FEHLT", message = "Filiale fehlt." });
        if (!await CanAccessBranchAsync(companyProfileId)) return Forbid();
        var f = (await FilialenAsync()).FirstOrDefault(x => x.Id == companyProfileId);
        if (f == null) return NotFound(new { error = "FILIALE_NICHT_GEFUNDEN", message = "Filiale nicht gefunden." });
        int jahr = year ?? DateTime.Today.Year;
        var liste = (await JahrAsync(jahr)).Where(x => FeiertagVorschlag.GiltFuer(x, f.Id, f.KantonCode));
        return Ok(new
        {
            kanton = f.KantonCode,
            filiale = f.Name,
            jahr,
            feiertage = liste.Select(x => new
            {
                x.Id,
                datum = x.Datum.ToString("yyyy-MM-dd"),
                x.Bezeichnung,
                x.Scope,
                x.KantonCode,
                sonntagsgleich = FeiertagVorschlag.IstSonntagsgleich(x),
            }),
        });
    }

    /// <summary>Globale Übersicht: alle Einträge eines Jahres, optional gefiltert nach Kanton.</summary>
    [HttpGet("alle")]
    public async Task<IActionResult> Alle([FromQuery] int? year, [FromQuery] string? kanton)
    {
        int jahr = year ?? DateTime.Today.Year;
        var filialen = await FilialenAsync();
        var proId = filialen.ToDictionary(f => f.Id);
        var k = Kanton(kanton);
        var liste = (await JahrAsync(jahr)).Where(x => k == null || GiltImKanton(x, k, proId)).ToList();
        var kantone = filialen.Where(f => f.KantonCode != null).GroupBy(f => f.KantonCode!).OrderBy(g => g.Key)
            .Select(g => new
            {
                code = g.Key,
                filialen = g.Select(f => f.Name).ToList(),
                kantonsliste = FeiertagVorschlag.KantonslisteVorhanden(g.Key),
            }).ToList();
        return Ok(new
        {
            jahr,
            kanton = k,
            kantone,
            filialen = filialen.Select(f => new { f.Id, f.Name, kanton = f.KantonCode }),
            ohneKanton = filialen.Where(f => f.KantonCode == null).Select(f => f.Name).ToList(),
            feiertage = liste.Select(x => new
            {
                x.Id,
                datum = x.Datum.ToString("yyyy-MM-dd"),
                x.Bezeichnung,
                x.Scope,
                x.KantonCode,
                x.CompanyProfileId,
                filiale = x.CompanyProfileId is int cp && proId.TryGetValue(cp, out var fi) ? fi.Name : null,
                sonntagsgleich = FeiertagVorschlag.IstSonntagsgleich(x),
                sonntagsgleichFest = x.Datum.Month == 8 && x.Datum.Day == 1,
            }),
        });
    }

    public record NeuDto(string? Datum, string? Bezeichnung, string? Scope, string? KantonCode, int? CompanyProfileId, bool Sonntagsgleich);

    (IActionResult?, DienstplanFeiertag?) Eintrag(DateOnly datum, string? bezeichnung, string? scopeRoh, string? kantonRoh,
        int? companyProfileId, bool sonntagsgleich, List<Filiale> filialen)
    {
        if (string.IsNullOrWhiteSpace(bezeichnung))
            return (BadRequest(new { error = "BEZEICHNUNG_FEHLT", message = "Bezeichnung fehlt." }), null);
        var scope = (scopeRoh ?? "KANTON").ToUpperInvariant();
        if (scope is not ("NATIONAL" or "KANTON" or "FILIALE"))
            return (BadRequest(new { error = "SCOPE_UNGUELTIG", message = "Geltung ungültig." }), null);
        var kanton = Kanton(kantonRoh);
        if (scope == "KANTON" && (kanton == null || kanton.Length != 2))
            return (BadRequest(new { error = "KANTON_FEHLT", message = "Bitte einen Kanton wählen." }), null);
        if (scope == "FILIALE" && !filialen.Any(f => f.Id == companyProfileId))
            return (BadRequest(new { error = "FILIALE_FEHLT", message = "Bitte eine Filiale wählen." }), null);
        return (null, new DienstplanFeiertag
        {
            Datum = datum,
            Bezeichnung = bezeichnung.Trim(),
            Scope = scope,
            KantonCode = scope == "KANTON" ? kanton : null,
            CompanyProfileId = scope == "FILIALE" ? companyProfileId : null,
            Sonntagsgleich = sonntagsgleich || (datum.Month == 8 && datum.Day == 1),
            CreatedAt = DateTime.Now,
        });
    }

    /// <summary>Gibt es an diesem Tag schon einen Eintrag, der sich mit dem neuen überschneidet?</summary>
    static bool Ueberschneidet(DienstplanFeiertag neu, IEnumerable<DienstplanFeiertag> vorhanden, Dictionary<int, Filiale> filialen)
    {
        foreach (var x in vorhanden.Where(v => v.Datum == neu.Datum))
        {
            if (x.Scope == "NATIONAL" || neu.Scope == "NATIONAL") return true;
            string? kNeu = neu.Scope == "KANTON" ? neu.KantonCode
                : neu.CompanyProfileId is int a && filialen.TryGetValue(a, out var fa) ? fa.KantonCode : null;
            string? kAlt = x.Scope == "KANTON" ? x.KantonCode
                : x.CompanyProfileId is int b && filialen.TryGetValue(b, out var fb) ? fb.KantonCode : null;
            if (neu.Scope == "FILIALE" && x.Scope == "FILIALE") { if (neu.CompanyProfileId == x.CompanyProfileId) return true; continue; }
            if (kNeu != null && kNeu == kAlt) return true;
        }
        return false;
    }

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<IActionResult> Neu([FromBody] NeuDto dto)
    {
        if (!DateOnly.TryParse(dto.Datum, out var datum))
            return BadRequest(new { error = "DATUM_UNGUELTIG", message = "Datum fehlt." });
        var filialen = await FilialenAsync();
        var (pruef, eintrag) = Eintrag(datum, dto.Bezeichnung, dto.Scope, dto.KantonCode, dto.CompanyProfileId, dto.Sonntagsgleich, filialen);
        if (pruef != null) return pruef;
        if (Ueberschneidet(eintrag!, await JahrAsync(datum.Year), filialen.ToDictionary(f => f.Id)))
            return Conflict(new { error = "SCHON_ERFASST", message = $"Am {datum:dd.MM.yyyy} ist dafür schon ein Feiertag erfasst." });
        _db.DienstplanFeiertage.Add(eintrag!);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, id = eintrag!.Id });
    }

    public record AendernDto(string? Bezeichnung, bool? Sonntagsgleich);

    [Authorize(Roles = "admin")]
    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Aendern(int id, [FromBody] AendernDto dto)
    {
        var x = await _db.DienstplanFeiertage.FindAsync(id);
        if (x == null) return NotFound();
        if (!string.IsNullOrWhiteSpace(dto.Bezeichnung)) x.Bezeichnung = dto.Bezeichnung.Trim();
        if (dto.Sonntagsgleich.HasValue) x.Sonntagsgleich = dto.Sonntagsgleich.Value || (x.Datum.Month == 8 && x.Datum.Day == 1);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Loeschen(int id)
    {
        var x = await _db.DienstplanFeiertage.FindAsync(id);
        if (x == null) return NotFound();
        _db.DienstplanFeiertage.Remove(x);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Jahresvorschlag für einen Kanton (Art. 20a ArG, soweit hinterlegt).</summary>
    [HttpGet("vorschlag")]
    public async Task<IActionResult> Vorschlag([FromQuery] string? kanton, [FromQuery] int year)
    {
        var k = Kanton(kanton);
        if (k == null || k.Length != 2) return BadRequest(new { error = "KANTON_FEHLT", message = "Bitte einen Kanton wählen." });
        if (year < 2000 || year > 2100) return BadRequest(new { error = "JAHR", message = "Jahr ungültig." });
        var erfasst = (await JahrAsync(year))
            .Where(x => x.Scope == "NATIONAL" || (x.Scope == "KANTON" && x.KantonCode == k))
            .Select(x => x.Datum).ToHashSet();
        return Ok(new
        {
            kanton = k,
            jahr = year,
            kantonsliste = FeiertagVorschlag.KantonslisteVorhanden(k),
            hinweis = FeiertagVorschlag.Hinweis(k),
            kandidaten = FeiertagVorschlag.Fuer(year, k).Select(c => new
            {
                datum = c.Datum.ToString("yyyy-MM-dd"),
                c.Bezeichnung,
                vorgewaehlt = c.Vorgewaehlt && !erfasst.Contains(c.Datum),
                c.Sonntagsgleich,
                scope = c.National ? "NATIONAL" : "KANTON",
                schonErfasst = erfasst.Contains(c.Datum),
            }),
        });
    }

    public record UebernehmenEintrag(string? Datum, string? Bezeichnung, string? Scope, bool Sonntagsgleich);
    public record UebernehmenDto(string? KantonCode, List<UebernehmenEintrag>? Eintraege);

    [Authorize(Roles = "admin")]
    [HttpPost("uebernehmen")]
    public async Task<IActionResult> Uebernehmen([FromBody] UebernehmenDto dto)
    {
        var k = Kanton(dto.KantonCode);
        if (k == null || k.Length != 2) return BadRequest(new { error = "KANTON_FEHLT", message = "Bitte einen Kanton wählen." });
        var filialen = await FilialenAsync();
        var proId = filialen.ToDictionary(f => f.Id);
        int neu = 0, uebersprungen = 0;
        var jahre = new Dictionary<int, List<DienstplanFeiertag>>();
        foreach (var e in dto.Eintraege ?? new())
        {
            if (!DateOnly.TryParse(e.Datum, out var datum)) { uebersprungen++; continue; }
            if (!jahre.TryGetValue(datum.Year, out var vorhanden)) jahre[datum.Year] = vorhanden = await JahrAsync(datum.Year);
            var scope = e.Scope?.ToUpperInvariant() == "NATIONAL" ? "NATIONAL" : "KANTON";
            var (pruef, eintrag) = Eintrag(datum, e.Bezeichnung, scope, k, null, e.Sonntagsgleich, filialen);
            if (pruef != null || Ueberschneidet(eintrag!, vorhanden, proId)) { uebersprungen++; continue; }
            _db.DienstplanFeiertage.Add(eintrag!);
            vorhanden.Add(eintrag!);
            neu++;
        }
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, neu, uebersprungen });
    }
}

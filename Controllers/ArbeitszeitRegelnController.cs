using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using K = HrSystem.Services.ArbeitszeitRegelKatalog;

namespace HrSystem.Controllers;

/// <summary>
/// Arbeitszeit-Regeln (Walter 07.10.2026): Vorlage am Hauptsitz, Abweichungen pro Filiale.
/// Gespeichert werden nur gesetzte Werte — leer heisst «wie Vorlage» bzw. «wie Gesetz».
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superuser,user")]
[Route("api/arbeitszeit-regeln")]
public class ArbeitszeitRegelnController : HrControllerBase
{
    public ArbeitszeitRegelnController(AppDbContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? hauptsitzId, [FromQuery] int? companyProfileId)
    {
        int? hsId = hauptsitzId;
        string? filialeName = null;
        if (companyProfileId is int cp)
        {
            if (!await CanAccessBranchAsync(cp)) return Forbid();
            var c = await _db.CompanyProfiles.AsNoTracking().Where(x => x.Id == cp)
                .Select(x => new { x.HauptsitzId, x.BranchName, x.RestaurantCode }).FirstOrDefaultAsync();
            if (c == null) return NotFound(new { error = "FILIALE_NICHT_GEFUNDEN", message = "Filiale nicht gefunden." });
            hsId = c.HauptsitzId;
            filialeName = $"{c.RestaurantCode} {c.BranchName}".Trim();
        }
        else if (hsId == null)
            return BadRequest(new { error = "EBENE_FEHLT", message = "Hauptsitz oder Filiale angeben." });

        var hsName = hsId == null ? null
            : await _db.Hauptsitze.AsNoTracking().Where(h => h.Id == hsId).Select(h => h.Name).FirstOrDefaultAsync();
        var werte = await _db.ArbeitszeitRegelWerte.AsNoTracking()
            .Where(w => (hsId != null && w.HauptsitzId == hsId) || (companyProfileId != null && w.CompanyProfileId == companyProfileId))
            .ToListAsync();
        var vorlage = werte.Where(w => w.HauptsitzId != null).ToList();
        var filiale = werte.Where(w => w.CompanyProfileId != null).ToList();
        var gilt = ArbeitszeitEinstellungen.Aufloesen(vorlage, filiale);
        var nurVorlage = ArbeitszeitEinstellungen.Aufloesen(vorlage, Array.Empty<ArbeitszeitRegelWert>());

        decimal? W(List<ArbeitszeitRegelWert> l, string r, string k) =>
            l.FirstOrDefault(x => x.Regel == r && x.Schluessel == k)?.Wert;
        string? T(List<ArbeitszeitRegelWert> l, string r) =>
            l.FirstOrDefault(x => x.Regel == r && x.Schluessel == K.Text)?.Text;
        var letzte = werte.OrderByDescending(w => w.GeaendertAm).FirstOrDefault();

        return Ok(new
        {
            ebene = companyProfileId != null ? "FILIALE" : "HAUPTSITZ",
            hauptsitzId = hsId,
            hauptsitzName = hsName,
            filiale = filialeName,
            geaendert = letzte == null ? null : new { am = letzte.GeaendertAm, von = letzte.GeaendertVon },
            regeln = K.Alle.Select(r => new
            {
                r.Code,
                titel = ArbeitszeitVerstoesse.Titel(r.Code, gilt),
                r.Recht,
                standardText = ArbeitszeitVerstoesse.Beschreibung(r.Code, ArbeitszeitEinstellungen.Gesetz),
                vorlageText = ArbeitszeitVerstoesse.Beschreibung(r.Code, nurVorlage),
                giltText = ArbeitszeitVerstoesse.Beschreibung(r.Code, gilt),
                aktiv = new { vorlage = W(vorlage, r.Code, K.Aktiv), filiale = W(filiale, r.Code, K.Aktiv), gilt = gilt.IstAktiv(r.Code), vorlageGilt = nurVorlage.IstAktiv(r.Code) },
                text = new { vorlage = T(vorlage, r.Code), filiale = T(filiale, r.Code) },
                parameter = r.Parameter.Select(p => new
                {
                    p.Schluessel, p.Label, p.Einheit, p.Min, p.Max, p.Standard,
                    vorlage = W(vorlage, r.Code, p.Schluessel),
                    filiale = W(filiale, r.Code, p.Schluessel),
                    vorlageGilt = nurVorlage.Wert(r.Code, p.Schluessel),
                    gilt = gilt.Wert(r.Code, p.Schluessel),
                }),
            }),
        });
    }

    public record WertDto(string Regel, string Schluessel, decimal? Wert, string? Text);
    public record SpeichernDto(int? HauptsitzId, int? CompanyProfileId, List<WertDto>? Werte);

    /// <summary>Ersetzt alle Werte der Ebene (Hauptsitz-Vorlage oder Filiale).</summary>
    [Authorize(Roles = "admin,superuser")]
    [HttpPut]
    public async Task<IActionResult> Speichern([FromBody] SpeichernDto dto)
    {
        if ((dto.HauptsitzId == null) == (dto.CompanyProfileId == null))
            return BadRequest(new { error = "EBENE_FEHLT", message = "Entweder Hauptsitz oder Filiale angeben." });
        if (dto.CompanyProfileId is int cp && !await CanAccessBranchAsync(cp)) return Forbid();
        if (dto.HauptsitzId != null && !User.IsInRole("admin"))
            return StatusCode(403, new { error = "NUR_ADMIN", message = "Die Vorlage am Hauptsitz kann nur ein Admin ändern." });

        var neu = new List<ArbeitszeitRegelWert>();
        foreach (var w in dto.Werte ?? new())
        {
            var regel = K.Finde(w.Regel);
            if (regel == null || !K.SchluesselGueltig(w.Regel, w.Schluessel))
                return BadRequest(new { error = "UNBEKANNT", message = $"Unbekannter Wert «{w.Regel}.{w.Schluessel}»." });
            if (w.Schluessel == K.Text)
            {
                if (string.IsNullOrWhiteSpace(w.Text)) continue;
            }
            else
            {
                if (w.Wert == null) continue;
                var p = regel.Parameter.FirstOrDefault(x => x.Schluessel == w.Schluessel);
                decimal min = p?.Min ?? 0, max = p?.Max ?? 1;
                if (w.Wert < min || w.Wert > max)
                    return BadRequest(new { error = "AUSSERHALB", message = $"«{p?.Label ?? "Aktiv"}» muss zwischen {min:0.##} und {max:0.##} liegen." });
            }
            neu.Add(new ArbeitszeitRegelWert
            {
                HauptsitzId = dto.HauptsitzId,
                CompanyProfileId = dto.CompanyProfileId,
                Regel = w.Regel,
                Schluessel = w.Schluessel,
                Wert = w.Schluessel == K.Text ? null : w.Wert,
                Text = w.Schluessel == K.Text ? w.Text!.Trim() : null,
                GeaendertAm = DateTime.Now,
                GeaendertVon = User.Identity?.Name,
            });
        }
        if (neu.GroupBy(x => (x.Regel, x.Schluessel)).Any(g => g.Count() > 1))
            return BadRequest(new { error = "DOPPELT", message = "Ein Wert ist doppelt angegeben." });

        var alt = await _db.ArbeitszeitRegelWerte
            .Where(w => (dto.HauptsitzId != null && w.HauptsitzId == dto.HauptsitzId)
                     || (dto.CompanyProfileId != null && w.CompanyProfileId == dto.CompanyProfileId))
            .ToListAsync();
        _db.ArbeitszeitRegelWerte.RemoveRange(alt);
        _db.ArbeitszeitRegelWerte.AddRange(neu);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, anzahl = neu.Count });
    }
}

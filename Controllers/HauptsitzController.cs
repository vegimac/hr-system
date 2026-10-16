using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.Elm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Hauptsitz-/Rechtseinheiten-Verwaltung (Walter 29.08.2026): System →
/// Filialen &amp; Benutzer → «Hauptsitze». Mehrere Hauptsitze möglich;
/// Filialen werden im Filial-Stammdaten-Modal zugeordnet. Reiner Katalog,
/// kein MA-Lohn → EditLock-Audit-Whitelist.
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/hauptsitze")]
public class HauptsitzController : ControllerBase
{
    private readonly AppDbContext _db;
    public HauptsitzController(AppDbContext db) => _db = db;

    public record HauptsitzDto(
        string? Name, string? Uid,
        string? ZusatzAbteilung, string? ZusatzAbteilung2, string? AdressZusatz,
        string? Strasse, string? Postfach, string? Lokalitaet,
        string? Plz, string? Ort, string? KantonCode, string? Land, int? BfsGemeindeNr,
        string? Bemerkung, bool? IsActive,
        string? FixPensenErlaubt = null, decimal? FlexStundenMax = null,
        decimal? MtpStundenMin = null, decimal? MtpStundenMax = null,
        string? KontaktName = null, string? KontaktEmail = null, string? KontaktTelefon = null);

    private static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? UidError(string? uid) => ElmStammdatenPruefung.UidBfsFehler(uid);

    private static string? LandNorm(string? land)
    {
        var iso = ElmGemeinsam.LandCodeIso(land);
        return iso ?? (string.IsNullOrWhiteSpace(land) ? "CH" : null);
    }

    private IActionResult? AdressePflicht(string? name, string? plz, string? ort)
    {
        if (name == null)
            return BadRequest(new { error = "NAME_FEHLT", message = "Bitte den Firmennamen der Rechtseinheit angeben." });
        if (plz == null)
            return BadRequest(new { error = "PLZ_FEHLT", message = "PLZ ist Pflicht (Swissdec 4.2)." });
        if (ort == null)
            return BadRequest(new { error = "ORT_FEHLT", message = "Ort ist Pflicht (Swissdec 4.2)." });
        return null;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var filialen = await _db.CompanyProfiles.AsNoTracking()
            .Where(p => p.HauptsitzId != null)
            .Select(p => new { p.Id, p.HauptsitzId, Name = p.BranchName ?? p.CompanyName, p.RestaurantCode })
            .ToListAsync();

        var list = await _db.Hauptsitze.AsNoTracking()
            .OrderBy(h => h.Name)
            .ToListAsync();

        return Ok(list.Select(h => new
        {
            h.Id, h.Name, h.Uid,
            h.ZusatzAbteilung, h.ZusatzAbteilung2, h.AdressZusatz,
            h.Strasse, h.Postfach, h.Lokalitaet,
            h.Plz, h.Ort, h.KantonCode, land = h.Land ?? "CH", h.BfsGemeindeNr,
            h.Bemerkung, h.IsActive,
            h.FixPensenErlaubt, h.FlexStundenMax, h.MtpStundenMin, h.MtpStundenMax,
            h.KontaktName, h.KontaktEmail, h.KontaktTelefon,
            regelnEffektiv = Effektiv(h),
            filialen = filialen.Where(f => f.HauptsitzId == h.Id)
                .OrderBy(f => f.RestaurantCode)
                .Select(f => new { f.Id, f.Name, f.RestaurantCode })
                .ToList()
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] HauptsitzDto dto)
    {
        var name = Norm(dto.Name);
        var plz = Norm(dto.Plz);
        var ort = Norm(dto.Ort);
        if (AdressePflicht(name, plz, ort) is { } err) return err;
        var uid = Norm(dto.Uid)?.ToUpperInvariant();
        if (UidError(uid) is string uidErr)
            return BadRequest(new { error = "UID_INVALID", message = uidErr });
        var land = LandNorm(dto.Land);
        if (land == null)
            return BadRequest(new { error = "LAND_UNGUELTIG", message = "Land bitte aus der Liste wählen (ISO-2)." });

        var h = new Hauptsitz
        {
            Name = name!, Uid = uid,
            ZusatzAbteilung = Norm(dto.ZusatzAbteilung),
            ZusatzAbteilung2 = Norm(dto.ZusatzAbteilung2),
            AdressZusatz = Norm(dto.AdressZusatz),
            Strasse = Norm(dto.Strasse),
            Postfach = Norm(dto.Postfach),
            Lokalitaet = Norm(dto.Lokalitaet),
            Plz = plz, Ort = ort,
            KantonCode = Norm(dto.KantonCode)?.ToUpperInvariant(),
            Land = land,
            BfsGemeindeNr = dto.BfsGemeindeNr is > 0 ? dto.BfsGemeindeNr : null,
            Bemerkung = Norm(dto.Bemerkung),
            IsActive = dto.IsActive ?? true,
            FixPensenErlaubt = NormPensen(dto.FixPensenErlaubt),
            FlexStundenMax   = dto.FlexStundenMax,
            MtpStundenMin    = dto.MtpStundenMin,
            MtpStundenMax    = dto.MtpStundenMax,
            KontaktName      = Norm(dto.KontaktName),
            KontaktEmail     = Norm(dto.KontaktEmail),
            KontaktTelefon   = Norm(dto.KontaktTelefon)
        };
        _db.Hauptsitze.Add(h);
        await _db.SaveChangesAsync();
        return Ok(new { h.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] HauptsitzDto dto)
    {
        var h = await _db.Hauptsitze.FirstOrDefaultAsync(x => x.Id == id);
        if (h == null) return NotFound();

        var name = Norm(dto.Name);
        var plz = Norm(dto.Plz);
        var ort = Norm(dto.Ort);
        if (AdressePflicht(name, plz, ort) is { } err) return err;
        var uid = Norm(dto.Uid)?.ToUpperInvariant();
        if (UidError(uid) is string uidErr)
            return BadRequest(new { error = "UID_INVALID", message = uidErr });
        var land = LandNorm(dto.Land);
        if (land == null)
            return BadRequest(new { error = "LAND_UNGUELTIG", message = "Land bitte aus der Liste wählen (ISO-2)." });

        h.Name = name!; h.Uid = uid;
        h.ZusatzAbteilung = Norm(dto.ZusatzAbteilung);
        h.ZusatzAbteilung2 = Norm(dto.ZusatzAbteilung2);
        h.AdressZusatz = Norm(dto.AdressZusatz);
        h.Strasse = Norm(dto.Strasse);
        h.Postfach = Norm(dto.Postfach);
        h.Lokalitaet = Norm(dto.Lokalitaet);
        h.Plz = plz; h.Ort = ort;
        h.KantonCode = Norm(dto.KantonCode)?.ToUpperInvariant();
        h.Land = land;
        h.BfsGemeindeNr = dto.BfsGemeindeNr is > 0 ? dto.BfsGemeindeNr : null;
        h.Bemerkung = Norm(dto.Bemerkung);
        h.FixPensenErlaubt = NormPensen(dto.FixPensenErlaubt);
        h.FlexStundenMax   = dto.FlexStundenMax;
        h.MtpStundenMin    = dto.MtpStundenMin;
        h.MtpStundenMax    = dto.MtpStundenMax;
        h.KontaktName      = Norm(dto.KontaktName);
        h.KontaktEmail     = Norm(dto.KontaktEmail);
        h.KontaktTelefon   = Norm(dto.KontaktTelefon);
        if (dto.IsActive.HasValue) h.IsActive = dto.IsActive.Value;
        h.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { h.Id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool force = false)
    {
        var h = await _db.Hauptsitze.FirstOrDefaultAsync(x => x.Id == id);
        if (h == null) return NotFound();

        var zugeordnet = await _db.CompanyProfiles.CountAsync(p => p.HauptsitzId == id);
        if (zugeordnet > 0 && !force)
            return Conflict(new
            {
                error = "HAUPTSITZ_IN_VERWENDUNG",
                message = $"«{h.Name}» ist {zugeordnet} Filiale(n) zugeordnet. Löschen entfernt die Zuordnungen.",
                filialen = zugeordnet
            });

        _db.Hauptsitze.Remove(h);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    private static string? NormPensen(string? text)
    {
        var werte = Services.EasyAtWork.VertragsRegeln.ParsePensen(text);
        return werte == null ? null : string.Join(", ", werte.Select(v => v.ToString("0.##")));
    }

    private static object Effektiv(Hauptsitz h)
    {
        var r = Services.EasyAtWork.VertragsRegeln.Von(h);
        return new
        {
            fixPensen      = r.FixPensenText,
            flexStundenMax = r.FlexStundenMax,
            mtpStundenMin  = r.MtpStundenMin,
            mtpStundenMax  = r.MtpStundenMax
        };
    }
}

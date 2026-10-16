using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.Elm;
using HrSystem.Services.Personaldaten;
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
        string? Name, string? Uid, bool? UidUnbekannt,
        string? ZusatzAbteilung, string? ZusatzAbteilung2, string? AdressZusatz,
        string? Strasse, string? Postfach, string? Lokalitaet,
        string? Plz, string? Ort, string? KantonCode, string? Land, int? BfsGemeindeNr,
        string? Bemerkung, bool? IsActive,
        string? FixPensenErlaubt = null, decimal? FlexStundenMax = null,
        decimal? MtpStundenMin = null, decimal? MtpStundenMax = null,
        string? KontaktName = null, string? KontaktEmail = null,
        string? KontaktTelefon = null, string? KontaktMobil = null,
        string? StellvertreterName = null, string? StellvertreterInhaber = null,
        string? StellvertreterAdressZusatz = null, string? StellvertreterStrasse = null,
        string? StellvertreterPostfach = null, string? StellvertreterLokalitaet = null,
        string? StellvertreterPlz = null, string? StellvertreterOrt = null,
        string? StellvertreterLand = null, string? StellvertreterUid = null);

    private static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? UidError(string? uid) => ElmStammdatenPruefung.UidBfsFehler(uid);

    private static string? LandNorm(string? land)
    {
        var iso = ElmGemeinsam.LandCodeIso(land);
        return iso ?? (string.IsNullOrWhiteSpace(land) ? "CH" : null);
    }

    private static string? TelNorm(string? roh)
    {
        if (string.IsNullOrWhiteSpace(roh)) return null;
        return PersonaldatenPruefung.TelefonFormatiert(roh) ?? roh.Trim();
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

    private IActionResult? TelFehler(string? roh, string feld)
    {
        if (string.IsNullOrWhiteSpace(roh)) return null;
        if (PersonaldatenPruefung.TelefonFormatiert(roh) != null) return null;
        return BadRequest(new {
            error = "TELEFON_UNGUELTIG",
            message = $"{feld} «{roh.Trim()}» ist keine gültige Nummer (z.B. +41 79 409 43 33)."
        });
    }

    static object Projektion(Hauptsitz h, IEnumerable<object> filialen) => new
    {
        h.Id, h.Name, h.Uid, h.UidUnbekannt,
        h.ZusatzAbteilung, h.ZusatzAbteilung2, h.AdressZusatz,
        h.Strasse, h.Postfach, h.Lokalitaet,
        h.Plz, h.Ort, h.KantonCode, land = h.Land ?? "CH", h.BfsGemeindeNr,
        h.Bemerkung, h.IsActive,
        h.FixPensenErlaubt, h.FlexStundenMax, h.MtpStundenMin, h.MtpStundenMax,
        h.KontaktName, h.KontaktEmail, h.KontaktTelefon, h.KontaktMobil,
        h.StellvertreterName, h.StellvertreterInhaber, h.StellvertreterAdressZusatz,
        h.StellvertreterStrasse, h.StellvertreterPostfach, h.StellvertreterLokalitaet,
        h.StellvertreterPlz, h.StellvertreterOrt,
        stellvertreterLand = h.StellvertreterLand ?? "CH",
        h.StellvertreterUid,
        regelnEffektiv = Effektiv(h),
        filialen
    };

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

        return Ok(list.Select(h => Projektion(h,
            filialen.Where(f => f.HauptsitzId == h.Id)
                .OrderBy(f => f.RestaurantCode)
                .Select(f => new { f.Id, f.Name, f.RestaurantCode })
                .ToList())));
    }

    void ApplyDto(Hauptsitz h, HauptsitzDto dto, string name, string? plz, string? ort,
        string? uid, bool uidUnbekannt, string land)
    {
        h.Name = name;
        h.UidUnbekannt = uidUnbekannt;
        h.Uid = uidUnbekannt ? null : uid;
        h.ZusatzAbteilung = Norm(dto.ZusatzAbteilung);
        h.ZusatzAbteilung2 = Norm(dto.ZusatzAbteilung2);
        h.AdressZusatz = Norm(dto.AdressZusatz);
        h.Strasse = Norm(dto.Strasse);
        h.Postfach = Norm(dto.Postfach);
        h.Lokalitaet = Norm(dto.Lokalitaet);
        h.Plz = plz; h.Ort = ort;
        h.KantonCode = Norm(dto.KantonCode)?.ToUpperInvariant();
        h.Land = land;
        // BFS kommt aus dem Ortschaftsverzeichnis (PLZ/Ort) — dto nur Fallback.
        h.BfsGemeindeNr = dto.BfsGemeindeNr is > 0 ? dto.BfsGemeindeNr : null;
        h.Bemerkung = Norm(dto.Bemerkung);
        h.FixPensenErlaubt = NormPensen(dto.FixPensenErlaubt);
        h.FlexStundenMax = dto.FlexStundenMax;
        h.MtpStundenMin = dto.MtpStundenMin;
        h.MtpStundenMax = dto.MtpStundenMax;
        h.KontaktName = Norm(dto.KontaktName);
        h.KontaktEmail = Norm(dto.KontaktEmail);
        h.KontaktTelefon = TelNorm(dto.KontaktTelefon);
        h.KontaktMobil = TelNorm(dto.KontaktMobil);
        h.StellvertreterName = Norm(dto.StellvertreterName);
        h.StellvertreterInhaber = Norm(dto.StellvertreterInhaber);
        h.StellvertreterAdressZusatz = Norm(dto.StellvertreterAdressZusatz);
        h.StellvertreterStrasse = Norm(dto.StellvertreterStrasse);
        h.StellvertreterPostfach = Norm(dto.StellvertreterPostfach);
        h.StellvertreterLokalitaet = Norm(dto.StellvertreterLokalitaet);
        h.StellvertreterPlz = Norm(dto.StellvertreterPlz);
        h.StellvertreterOrt = Norm(dto.StellvertreterOrt);
        h.StellvertreterLand = LandNorm(dto.StellvertreterLand) ?? "CH";
        h.StellvertreterUid = Norm(dto.StellvertreterUid)?.ToUpperInvariant();
        if (dto.IsActive.HasValue) h.IsActive = dto.IsActive.Value;
    }

    IActionResult? Validieren(HauptsitzDto dto, out string name, out string? plz, out string? ort,
        out string? uid, out bool uidUnbekannt, out string land)
    {
        name = Norm(dto.Name)!;
        plz = Norm(dto.Plz);
        ort = Norm(dto.Ort);
        uid = null;
        uidUnbekannt = false;
        land = "CH";

        if (AdressePflicht(Norm(dto.Name), plz, ort) is { } err) return err;

        uidUnbekannt = dto.UidUnbekannt == true || ElmStammdatenPruefung.IstUidUnbekannt(dto.Uid);
        uid = uidUnbekannt ? null : Norm(dto.Uid)?.ToUpperInvariant();
        if (!uidUnbekannt && UidError(uid) is string uidErr)
            return BadRequest(new { error = "UID_INVALID", message = uidErr });

        var landN = LandNorm(dto.Land);
        if (landN == null)
            return BadRequest(new { error = "LAND_UNGUELTIG", message = "Land bitte aus der Liste wählen (ISO-2)." });
        land = landN;

        if (TelFehler(dto.KontaktTelefon, "Telefon") is { } t1) return t1;
        if (TelFehler(dto.KontaktMobil, "Mobil") is { } t2) return t2;

        if (!string.IsNullOrWhiteSpace(dto.StellvertreterUid)
            && UidError(dto.StellvertreterUid) is string svUid)
            return BadRequest(new { error = "STELLVERTRETER_UID_INVALID", message = svUid });

        if (!string.IsNullOrWhiteSpace(dto.StellvertreterName))
        {
            if (string.IsNullOrWhiteSpace(dto.StellvertreterPlz) || string.IsNullOrWhiteSpace(dto.StellvertreterOrt))
                return BadRequest(new {
                    error = "STELLVERTRETER_ADRESSE",
                    message = "Stellvertreter: PLZ und Ort sind Pflicht, wenn ein Name erfasst ist."
                });
            if (string.IsNullOrWhiteSpace(dto.StellvertreterUid))
                return BadRequest(new {
                    error = "STELLVERTRETER_UID_FEHLT",
                    message = "Stellvertreter: UID-BFS ist Pflicht (Swissdec Delegate)."
                });
        }

        return null;
    }

    /// <summary>BFS-Gemeindenummer aus swiss_location (PLZ + Ort) — nie von Hand tippen.</summary>
    async Task<int?> BfsAusVerzeichnisAsync(string? plz, string? ort, string land)
    {
        if (!string.Equals(land, "CH", StringComparison.OrdinalIgnoreCase)) return null;
        var p = (plz ?? "").Trim();
        if (p.Length != 4) return null;
        var o = (ort ?? "").Trim().ToLowerInvariant();
        var treffer = await _db.SwissLocations.AsNoTracking()
            .Where(l => l.Plz4 == p)
            .Select(l => new { l.BfsNr, l.Ortschaftsname, l.Gemeindename })
            .ToListAsync();
        if (treffer.Count == 0) return null;
        var nrs = treffer.Select(t => t.BfsNr).Distinct().ToList();
        if (nrs.Count == 1) return nrs[0];
        if (o.Length == 0) return null;
        return treffer.FirstOrDefault(t =>
                   (t.Ortschaftsname ?? "").ToLowerInvariant().StartsWith(o)
                   || (t.Gemeindename ?? "").ToLowerInvariant() == o)?.BfsNr;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] HauptsitzDto dto)
    {
        if (Validieren(dto, out var name, out var plz, out var ort, out var uid, out var uidUnbekannt, out var land) is { } err)
            return err;

        var bfs = await BfsAusVerzeichnisAsync(plz, ort, land) ?? (dto.BfsGemeindeNr is > 0 ? dto.BfsGemeindeNr : null);
        dto = dto with { BfsGemeindeNr = bfs };

        var h = new Hauptsitz { IsActive = dto.IsActive ?? true };
        ApplyDto(h, dto, name, plz, ort, uid, uidUnbekannt, land);
        _db.Hauptsitze.Add(h);
        await _db.SaveChangesAsync();
        return Ok(new { h.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] HauptsitzDto dto)
    {
        var h = await _db.Hauptsitze.FirstOrDefaultAsync(x => x.Id == id);
        if (h == null) return NotFound();

        if (Validieren(dto, out var name, out var plz, out var ort, out var uid, out var uidUnbekannt, out var land) is { } err)
            return err;

        var bfs = await BfsAusVerzeichnisAsync(plz, ort, land) ?? (dto.BfsGemeindeNr is > 0 ? dto.BfsGemeindeNr : null);
        dto = dto with { BfsGemeindeNr = bfs };

        ApplyDto(h, dto, name, plz, ort, uid, uidUnbekannt, land);
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
            fixPensen = r.FixPensenText,
            flexStundenMax = r.FlexStundenMax,
            mtpStundenMin = r.MtpStundenMin,
            mtpStundenMax = r.MtpStundenMax
        };
    }
}

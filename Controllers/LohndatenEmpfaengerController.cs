using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Lohndatenempfänger (Walter-Vorgabe 06.08.2026, UI getrennt 17.10.2026,
/// Swissdec-only 17.10.2026): Katalog nur lesen + Swissdec-Import;
/// Filiale ordnet zu und erfasst Mitglied-/SSL-Nummer + Gültigkeit.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/lohndaten-empfaenger")]
public class LohndatenEmpfaengerController : ControllerBase
{
    private readonly AppDbContext _db;
    public LohndatenEmpfaengerController(AppDbContext db) => _db = db;

    static readonly object SwissdecOnly = new
    {
        error = "SWISSDEC_ONLY",
        message = "Empfänger-Stammdaten kommen nur aus der Swissdec-Empfängerliste — bitte über den Swissdec-Button aktualisieren.",
    };

    // ── Katalog (nur lesen) ───────────────────────────────────────────────

    /// <summary>Alle Empfänger (Katalog), inkl. Anzahl Zuordnungen.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool all = false)
    {
        var list = await _db.LohndatenEmpfaengers.AsNoTracking()
            .Where(e => all || e.IsActive)
            .OrderBy(e => e.Art).ThenBy(e => e.Bezeichnung)
            .Select(e => new
            {
                e.Id, e.Art, e.Bezeichnung, e.Zusatz, e.UidNummer,
                e.Strasse, e.Postfach, e.Plz, e.Ort, e.KantonCode,
                e.Kassennummer, e.KassenTyp, e.SupportEmail, e.Bemerkung, e.IsActive,
                zuordnungen = e.Zuordnungen.Count,
            })
            .ToListAsync();
        return Ok(list);
    }

    // Handpflege gesperrt (Walter 17.10.2026) — einzige Schreibquelle = Swissdec-Import.
    [HttpPost]
    public IActionResult Create() => Conflict(SwissdecOnly);

    [HttpPut("{id:int}")]
    public IActionResult Update(int id) => Conflict(SwissdecOnly);

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => Conflict(SwissdecOnly);

    // ── Swissdec Empfängerliste (Walter 17.10.2026) ───────────────────────
    // Quelle: https://www.swissdec.ch/data-receiver — monatlich aktualisieren.

    /// <summary>Proxy-Download der aktuellen XLSX (CORS/Redirects umgehen).</summary>
    [Authorize(Roles = "admin")]
    [HttpGet("swissdec-xlsx")]
    public async Task<IActionResult> SwissdecXlsxDownload([FromServices] IHttpClientFactory httpFactory)
    {
        try
        {
            var client = httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60);
            // Swissdec liefert 403 ohne Browser-UA + Referer (Stand 17.10.2026).
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Referrer = new Uri(HrSystem.Services.Elm.SwissdecEmpfaengerListe.InfoUrl);
            client.DefaultRequestHeaders.Accept.ParseAdd(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/octet-stream,*/*");
            using var resp = await client.GetAsync(HrSystem.Services.Elm.SwissdecEmpfaengerListe.DownloadUrl);
            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, new
                {
                    error = "DOWNLOAD_FEHLER",
                    message = "Swissdec-Datei nicht erreichbar — bitte XLSX manuell von swissdec.ch/data-receiver laden.",
                });
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length < 100 || bytes[0] != (byte)'P' || bytes[1] != (byte)'K')
                return StatusCode(502, new
                {
                    error = "DOWNLOAD_FEHLER",
                    message = "Antwort war keine Excel-Datei — bitte manuell von swissdec.ch laden.",
                });
            var name = $"Swissdec_Empfaengerliste_{DateTime.Now:yyyy-MM-dd}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { error = "DOWNLOAD_FEHLER", message = ex.Message });
        }
    }

    /// <summary>
    /// Vorschau oder Übernahme der Swissdec-XLSX.
    /// dryRun=true (Default): nur Diff. dryRun=false: neu anlegen + geänderte Stammdaten korrigieren.
    /// Adresse/Bemerkung der bestehenden Zeilen bleiben unangetastet.
    /// </summary>
    [Authorize(Roles = "admin")]
    [HttpPost("swissdec-import")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> SwissdecImport(IFormFile? file, [FromQuery] bool dryRun = true)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "DATEI_FEHLT", message = "Bitte die Swissdec-Empfängerliste (.xlsx) hochladen." });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            && !file.FileName.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "FORMAT", message = "Nur .xlsx-Dateien." });

        List<HrSystem.Services.Elm.SwissdecEmpfaengerListe.Zeile> liste;
        try
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            ms.Position = 0;
            liste = HrSystem.Services.Elm.SwissdecEmpfaengerListe.Parse(ms);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "PARSE", message = "Excel konnte nicht gelesen werden: " + ex.Message });
        }
        if (liste.Count == 0)
            return BadRequest(new { error = "LEER", message = "Keine Empfängerzeilen gefunden — stimmt das Sheet «Endempfänger»?" });

        List<HrSystem.Services.Elm.SwissdecEmpfaengerListe.Diff> diffs;
        List<LohndatenEmpfaenger> bestand;
        try
        {
            bestand = await _db.LohndatenEmpfaengers.ToListAsync();
            diffs = HrSystem.Services.Elm.SwissdecEmpfaengerListe.Vergleichen(liste, bestand);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "VERGLEICH", message = "Vergleich fehlgeschlagen: " + ex.Message });
        }

        if (dryRun)
        {
            return Ok(new
            {
                dryRun = true,
                quelle = HrSystem.Services.Elm.SwissdecEmpfaengerListe.InfoUrl,
                total = diffs.Count,
                neu = diffs.Count(d => d.Status == "neu"),
                aenderung = diffs.Count(d => d.Status == "aenderung"),
                gleich = diffs.Count(d => d.Status == "gleich"),
                diffs = diffs.Where(d => d.Status != "gleich").Select(d => new
                {
                    d.Status, d.Art, d.Bezeichnung, d.Kassennummer, d.UidNummer,
                    d.KantonCode, d.SupportEmail, d.BestehendId, d.AenderungText,
                }),
            });
        }

        var neu = 0;
        var geaendert = 0;
        foreach (var d in diffs.Where(x => x.Status is "neu" or "aenderung"))
        {
            if (d.Status == "neu")
            {
                _db.LohndatenEmpfaengers.Add(new LohndatenEmpfaenger
                {
                    Art = d.Art,
                    Bezeichnung = d.Bezeichnung,
                    Kassennummer = d.Kassennummer,
                    KassenTyp = KassenArt.AusNummer(d.Art, d.Kassennummer),
                    UidNummer = d.UidNummer,
                    KantonCode = d.KantonCode,
                    SupportEmail = d.SupportEmail,
                    Bemerkung = "Swissdec Empfängerliste",
                    IsActive = true,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                });
                neu++;
            }
            else if (d.BestehendId is int id)
            {
                var e = bestand.FirstOrDefault(x => x.Id == id);
                if (e == null) continue;
                e.Bezeichnung = d.Bezeichnung;
                if (!string.IsNullOrWhiteSpace(d.UidNummer)) e.UidNummer = d.UidNummer;
                if (!string.IsNullOrWhiteSpace(d.SupportEmail)) e.SupportEmail = d.SupportEmail;
                if (!string.IsNullOrWhiteSpace(d.KantonCode)) e.KantonCode = d.KantonCode;
                if (!string.IsNullOrWhiteSpace(d.Kassennummer)) e.Kassennummer = d.Kassennummer;
                e.KassenTyp = KassenArt.AusNummer(e.Art, e.Kassennummer);
                e.UpdatedAt = DateTime.Now;
                geaendert++;
            }
        }
        foreach (var d in diffs.Where(x => x.Status == "gleich" && x.BestehendId is int))
        {
            var e = bestand.FirstOrDefault(x => x.Id == d.BestehendId);
            if (e == null) continue;
            var typ = KassenArt.AusNummer(e.Art, e.Kassennummer);
            if (e.KassenTyp == typ) continue;
            e.KassenTyp = typ;
            e.UpdatedAt = DateTime.Now;
        }
        await _db.SaveChangesAsync();
        return Ok(new
        {
            dryRun = false,
            neu,
            aenderung = geaendert,
            gleich = diffs.Count(d => d.Status == "gleich"),
            message = $"{neu} neu, {geaendert} korrigiert.",
        });
    }
}

/// <summary>Zuordnungen Empfänger ↔ Filiale (Mitglied-/Subnummer pro Filiale).</summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/companyprofiles/{cpId:int}/empfaenger")]
public class CompanyProfileEmpfaengerController : ControllerBase
{
    private readonly AppDbContext _db;
    public CompanyProfileEmpfaengerController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetForBranch(int cpId)
    {
        var list = await _db.CompanyProfileEmpfaengers.AsNoTracking()
            .Where(z => z.CompanyProfileId == cpId)
            .OrderBy(z => z.Empfaenger!.Art).ThenBy(z => z.Empfaenger!.Bezeichnung)
            .Select(z => new
            {
                z.Id, z.EmpfaengerId, z.Mitgliednummer, z.Subnummer,
                z.GueltigAb, z.GueltigBis, z.Bemerkung, z.IsActive,
                z.IkvAktiv, z.IkvEmpfaengerId,
                ikvBezeichnung = z.IkvEmpfaenger != null ? z.IkvEmpfaenger.Bezeichnung : null,
                ikvKanton      = z.IkvEmpfaenger != null ? z.IkvEmpfaenger.KantonCode : null,
                ikvNummer      = z.IkvEmpfaenger != null ? z.IkvEmpfaenger.Kassennummer : null,
                art          = z.Empfaenger!.Art,
                bezeichnung  = z.Empfaenger!.Bezeichnung,
                zusatz       = z.Empfaenger!.Zusatz,
                strasse      = z.Empfaenger!.Strasse,
                postfach     = z.Empfaenger!.Postfach,
                plz          = z.Empfaenger!.Plz,
                ort          = z.Empfaenger!.Ort,
                kantonCode   = z.Empfaenger!.KantonCode,
                kassennummer = z.Empfaenger!.Kassennummer,
                kassenTyp    = z.Empfaenger!.KassenTyp,
                uidNummer    = z.Empfaenger!.UidNummer,
                supportEmail = z.Empfaenger!.SupportEmail,
            })
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int cpId, [FromBody] CpEmpfaengerDto dto)
    {
        var filiale = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => c.Id == cpId)
            .Select(c => new { c.KantonCode, c.HauptsitzId })
            .FirstOrDefaultAsync();
        if (filiale == null) return NotFound(new { error = "BRANCH_NOT_FOUND" });
        var empf = await _db.LohndatenEmpfaengers.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == dto.EmpfaengerId);
        if (empf == null) return NotFound(new { error = "EMPFAENGER_NOT_FOUND" });
        var nummerFehler = PruefeKassennummer(empf.Art, dto.Kassennummer);
        if (nummerFehler != null) return nummerFehler;
        var z = new CompanyProfileEmpfaenger
        {
            CompanyProfileId = cpId,
            EmpfaengerId     = dto.EmpfaengerId,
        };
        ApplyZuordnung(z, dto);
        if (z.GueltigAb != null && z.GueltigBis != null && z.GueltigBis < z.GueltigAb)
            return BadRequest(new { error = "GUELTIG_BIS_VOR_AB" });
        var ikvFehler = await PruefeIkvAsync(filiale.KantonCode, filiale.HauptsitzId, empf, z);
        if (ikvFehler != null) return ikvFehler;

        // Derselbe Empfänger nochmals ohne Gültig-ab = echtes Duplikat.
        // Mit Gültig-ab ist es ein neuer Satz (neuer Vertrag / neue Nummer).
        var gleicher = await _db.CompanyProfileEmpfaengers
            .AnyAsync(x => x.CompanyProfileId == cpId && x.EmpfaengerId == dto.EmpfaengerId
                        && (z.GueltigAb == null || x.GueltigAb == z.GueltigAb));
        if (gleicher) return Conflict(new { error = "EMPFAENGER_BEREITS_ZUGEORDNET" });

        var geschlossen = await SchliesseVorgaengerAsync(cpId, z);
        _db.CompanyProfileEmpfaengers.Add(z);
        await _db.SaveChangesAsync();
        return Ok(new { z.Id, vorgaengerGeschlossen = geschlossen });
    }

    /// <summary>
    /// Walter 07.09.2026: «Gültig bis» bleibt normalerweise offen. Ein neuer
    /// Satz DERSELBEN ART (z.B. neue UVG-Versicherung ab 1.1.) setzt bei allen
    /// noch offenen Sätzen dieser Art in dieser Filiale das Ende automatisch
    /// auf Neubeginn − 1 Tag. Sätze, die erst nach dem Neubeginn starten,
    /// bleiben unberührt (Zukunftssatz). Bei QST/FAK/Lohnausweis zählt der
    /// Kanton mit — pro Kanton ist es ein eigener «Strang».
    /// </summary>
    private async Task<List<string>> SchliesseVorgaengerAsync(int cpId, CompanyProfileEmpfaenger neu)
    {
        var info = new List<string>();
        if (neu.GueltigAb == null) return info;
        var neuE = await _db.LohndatenEmpfaengers.AsNoTracking().FirstOrDefaultAsync(e => e.Id == neu.EmpfaengerId);
        if (neuE == null) return info;
        var kantonZaehlt = neuE.Art is "QST" or "FAK" or "LOHNAUSWEIS";
        var ende = neu.GueltigAb.Value.AddDays(-1);

        var offene = await _db.CompanyProfileEmpfaengers
            .Include(x => x.Empfaenger)
            .Where(x => x.CompanyProfileId == cpId && x.IsActive && x.Id != neu.Id
                     && x.Empfaenger!.Art == neuE.Art
                     && (x.GueltigBis == null || x.GueltigBis >= neu.GueltigAb)
                     && (x.GueltigAb == null || x.GueltigAb < neu.GueltigAb))
            .ToListAsync();
        foreach (var alt in offene)
        {
            if (kantonZaehlt && !string.Equals(alt.Empfaenger?.KantonCode ?? "", neuE.KantonCode ?? "", StringComparison.OrdinalIgnoreCase))
                continue;
            alt.GueltigBis = ende;
            alt.UpdatedAt  = DateTime.Now;
            info.Add($"{alt.Empfaenger?.Bezeichnung} → gültig bis {ende:dd.MM.yyyy}");
        }
        return info;
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int cpId, int id, [FromBody] CpEmpfaengerDto dto)
    {
        var z = await _db.CompanyProfileEmpfaengers
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyProfileId == cpId);
        if (z == null) return NotFound();
        var filiale = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => c.Id == cpId)
            .Select(c => new { c.KantonCode, c.HauptsitzId })
            .FirstOrDefaultAsync();
        var empf = await _db.LohndatenEmpfaengers.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == z.EmpfaengerId);
        var nummerFehler = PruefeKassennummer(empf?.Art, dto.Kassennummer);
        if (nummerFehler != null) return nummerFehler;
        ApplyZuordnung(z, dto);
        if (z.GueltigAb != null && z.GueltigBis != null && z.GueltigBis < z.GueltigAb)
            return BadRequest(new { error = "GUELTIG_BIS_VOR_AB" });
        if (empf != null && filiale != null)
        {
            var ikvFehler = await PruefeIkvAsync(filiale.KantonCode, filiale.HauptsitzId, empf, z);
            if (ikvFehler != null) return ikvFehler;
        }
        z.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { z.Id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int cpId, int id)
    {
        var z = await _db.CompanyProfileEmpfaengers
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyProfileId == cpId);
        if (z == null) return NotFound();
        _db.CompanyProfileEmpfaengers.Remove(z);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    /// <summary>Ausgleichskasse und FAK, und nur wenn die Maske eine Nummer schickt.</summary>
    private static IActionResult? PruefeKassennummer(string? art, string? nummer)
    {
        if (nummer == null) return null;
        var begriff = art switch
        {
            "AUSGLEICHSKASSE" => AusgleichskasseNummer.BegriffAhv,
            "FAK" => AusgleichskasseNummer.BegriffFak,
            _ => null,
        };
        if (begriff == null) return null;
        var fehler = AusgleichskasseNummer.Fehler(nummer, begriff);
        return fehler == null ? null : new BadRequestObjectResult(new { error = "KASSENNUMMER_UNGUELTIG", message = fehler });
    }

    private async Task<IActionResult?> PruefeIkvAsync(
        string? arbeitsKanton, int? hauptsitzId, LohndatenEmpfaenger quelle, CompanyProfileEmpfaenger z)
    {
        if (!z.IkvAktiv)
        {
            z.IkvEmpfaengerId = null;
            return null;
        }
        var ziel = z.IkvEmpfaengerId is int id
            ? await _db.LohndatenEmpfaengers.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id)
            : null;
        string? hsKanton = null;
        if (hauptsitzId is int hid)
            hsKanton = await _db.Hauptsitze.AsNoTracking()
                .Where(h => h.Id == hid).Select(h => h.KantonCode).FirstOrDefaultAsync();
        var text = FakIkv.Pruefen(quelle, true, ziel, arbeitsKanton, hsKanton);
        if (text != null) return BadRequest(new { error = "IKV_UNGUELTIG", message = text });
        var hatTarif = await _db.FamilienzulagenTarife.AnyAsync(t =>
            t.IsActive && t.KantonCode == ziel!.KantonCode);
        if (!hatTarif)
            return BadRequest(new
            {
                error = "IKV_OHNE_TARIF",
                message = $"Für den Hauptsitzkanton {ziel!.KantonCode} ist kein Familienzulagen-Tarif hinterlegt."
            });
        return null;
    }

    private static void ApplyZuordnung(CompanyProfileEmpfaenger z, CpEmpfaengerDto dto)
    {
        z.Mitgliednummer = string.IsNullOrWhiteSpace(dto.Mitgliednummer) ? null : dto.Mitgliednummer.Trim();
        z.Subnummer      = string.IsNullOrWhiteSpace(dto.Subnummer) ? null : dto.Subnummer.Trim();
        z.Bemerkung      = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        z.GueltigAb      = DateOnly.TryParse(dto.GueltigAb, out var ab) ? ab : null;
        z.GueltigBis     = DateOnly.TryParse(dto.GueltigBis, out var bis) ? bis : null;
        if (dto.IsActive.HasValue) z.IsActive = dto.IsActive.Value;
        z.IkvAktiv = dto.IkvAktiv == true;
        z.IkvEmpfaengerId = z.IkvAktiv ? dto.IkvEmpfaengerId : null;
    }
}

public class LohndatenEmpfaengerDto
{
    public string? Art { get; set; }
    public string? Bezeichnung { get; set; }
    public string? Zusatz { get; set; }
    public string? UidNummer { get; set; }
    public string? Strasse { get; set; }
    public string? Postfach { get; set; }
    public string? Plz { get; set; }
    public string? Ort { get; set; }
    public string? KantonCode { get; set; }
    public string? Kassennummer { get; set; }
    public string? SupportEmail { get; set; }
    public string? Bemerkung { get; set; }
    public bool? IsActive { get; set; }
}

public class CpEmpfaengerDto
{
    public int EmpfaengerId { get; set; }
    public string? Mitgliednummer { get; set; }
    public string? Subnummer { get; set; }
    /// <summary>Nur zur Prüfung bei Ausgleichskasse und FAK. Wird nicht gespeichert.</summary>
    public string? Kassennummer { get; set; }
    public string? Bemerkung { get; set; }
    /// <summary>ISO yyyy-MM-dd (native Datumsfelder) — leer = seit jeher.</summary>
    public string? GueltigAb { get; set; }
    /// <summary>ISO yyyy-MM-dd — leer = offen (gilt weiterhin).</summary>
    public string? GueltigBis { get; set; }
    public bool? IsActive { get; set; }
    /// <summary>Interkantonale Vereinbarung. Leer = aus.</summary>
    public bool? IkvAktiv { get; set; }
    public int? IkvEmpfaengerId { get; set; }
}

using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.Elm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Lohnmeldungen (ELM): Meldung prüfen und senden, Stand je Empfänger verfolgen, Rückfragen
/// beantworten, Freigabe-Link, Ersatzmeldung. Die Anwendermaske im HR-Bereich
/// (<c>page-elm-anwender</c>) ist für admin und superuser. Zertifikat, Verbindungstest und
/// die Entwicklungsseite bleiben admin. Adresse und Prüfschalter setzt der Server.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superuser")]
[Route("api/elm/meldungen")]
public class ElmMeldungenController : ControllerBase
{
    private readonly ElmMeldungenService _dienst;
    private readonly AppDbContext _db;
    private readonly ElmZertifikatStore _store;

    public ElmMeldungenController(ElmMeldungenService dienst, AppDbContext db, ElmZertifikatStore store)
    {
        _dienst = dienst;
        _db = db;
        _store = store;
    }

    [HttpGet]
    public IActionResult Liste() => Ok(Stand());

    /// <summary>Lesen: je Filiale der letzte abgeschlossene Lohnmonat und der Stand schon gesendeter Meldungen. Fasst nichts zusammen.</summary>
    [HttpGet("uebersicht")]
    public async Task<IActionResult> Uebersicht(int jahr, CancellationToken ct)
    {
        if (jahr < 2000 || jahr > 2100) jahr = DateTime.Now.Year;
        var filialen = (await _db.CompanyProfiles.AsNoTracking()
            .Select(p => new { p.Id, p.BranchName, p.CompanyName })
            .ToListAsync(ct))
            .OrderBy(p => string.IsNullOrWhiteSpace(p.BranchName) ? p.CompanyName : p.BranchName)
            .ToList();
        var perioden = await _db.PayrollPerioden.AsNoTracking()
            .Where(p => p.Year == jahr)
            .Select(p => new { p.CompanyProfileId, p.Month, p.Status })
            .ToListAsync(ct);
        var meldungen = _dienst.Liste()
            .Where(v => v.Jahr == jahr && !ElmMeldungAnzeige.Zertifizierungstest(v))
            .ToList();
        var monate = new[] { "", "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember" };

        string Stufe(ElmVorgang? m)
        {
            if (m == null) return "keine";
            if (m.Adressaten.Any(a => a.Stories.Any(s => s.Art == "DialogMessage" && !s.Beantwortet))) return "rueckfrage";
            return ElmMeldungAnzeige.CockpitStatus(m) switch
            {
                "error" => "fehler",
                "success" => "angenommen",
                _ => "unterwegs",
            };
        }
        string Wort(string stufe) => stufe switch
        {
            "rueckfrage" => "Rückfrage",
            "fehler" => "Fehler",
            "angenommen" => "Angenommen",
            "unterwegs" => "Unterwegs",
            "bereit" => "Bereit",
            _ => "Nicht bereit",
        };
        string? Aufgabe(ElmVorgang? m)
        {
            if (m == null) return null;
            var dialog = m.Adressaten.FirstOrDefault(a => a.Stories.Any(s => s.Art == "DialogMessage" && !s.Beantwortet));
            if (dialog != null) return "Rückfrage von " + (dialog.InstitutionName ?? dialog.Identification);
            if (m.Adressaten.Any(a => a.State == "CompletionReleaseMissing" && a.Completion != null))
                return "Freigabe offen";
            if (ElmMeldungAnzeige.CockpitStatus(m) == "error") return "Nicht angenommen";
            return null;
        }

        var zeilen = filialen.Select(f =>
        {
            var name = string.IsNullOrWhiteSpace(f.BranchName) ? f.CompanyName : f.BranchName!;
            var derFiliale = perioden.Where(p => p.CompanyProfileId == f.Id).ToList();
            var geschlossen = derFiliale.Where(p => p.Status == "abgeschlossen").Select(p => p.Month).ToList();
            var lohnperioden = derFiliale
                .GroupBy(p => p.Month)
                .Select(g => new
                {
                    monat = g.Key,
                    status = g.Any(p => p.Status == "abgeschlossen") ? "abgeschlossen"
                        : g.Any(p => p.Status == "provisorisch_abgeschlossen") ? "provisorisch_abgeschlossen"
                        : g.Select(p => p.Status).FirstOrDefault() ?? "offen",
                })
                .ToList();
            var letzter = geschlossen.Count == 0 ? 0 : geschlossen.Max();
            var eigene = meldungen.Where(m => m.CompanyProfileId == f.Id).ToList();
            var monatM = letzter == 0 ? null : eigene.Where(m => m.Art == "monthly" && m.Monat == letzter).OrderByDescending(m => m.Gesendet).FirstOrDefault();
            var jahrM = eigene.Where(m => m.Art is "annual" or "grenz-it" or "grenz-fr" or "grenz-sfn").OrderByDescending(m => m.Gesendet).FirstOrDefault();
            var monatStufe = monatM == null ? (letzter == 0 ? "keine" : "bereit") : Stufe(monatM);
            var jahrStufe = jahrM == null ? (geschlossen.Count == 0 ? "keine" : "bereit") : Stufe(jahrM);
            var aufgabeVon = new[] { monatM, jahrM }.Where(m => m != null).Select(m => m!).FirstOrDefault(m => Aufgabe(m) != null);
            return new
            {
                id = f.Id,
                name,
                abgeschlossen = geschlossen,
                lohnperioden,
                letzterMonat = letzter,
                letzterText = letzter == 0 ? "Kein abgeschlossener Lohnlauf" : monate[letzter] + " " + jahr,
                monatStatus = monatStufe,
                monatText = Wort(monatStufe),
                jahrStatus = jahrStufe,
                jahrText = Wort(jahrStufe),
                aufgabe = aufgabeVon == null ? null : Aufgabe(aufgabeVon),
                aufgabeId = aufgabeVon?.Id,
            };
        }).ToList();
        return Ok(new { jahr, filialen = zeilen });
    }

    [HttpGet("vorschau")]
    public async Task<IActionResult> Vorschau(string art, int jahr, int? monat, int? companyProfileId, CancellationToken ct)
    {
        art = ElmMeldungAnzeige.NormalisiereArt(art);
        try
        {
            var v = await _dienst.VorschauAsync(art, jahr, monat, ct, companyProfileId);
            return Ok(new
            {
                v.Titel, v.Firmenname, v.Uid,
                empfaenger = v.Adressaten.Select(a => new
                {
                    a.AddresseeId,
                    a.Identification,
                    a.Domain,
                    name = ElmMeldungAnzeige.EmpfaengerName(a.Domain, a.Identification, null),
                }),
                fehler = v.Plausibilitaet.Select(h => h.Text ?? h.Code ?? "Plausibilitätsregel verletzt"),
                technisch = v.XsdFehler,
                warnungen = v.Warnungen,
                sendenGesperrt = _dienst.SendenGesperrt(),
            });
        }
        catch (Exception ex) { return BadRequest(new { error = "VORSCHAU", message = ex.GetBaseException().Message }); }
    }

    public record SendenBody(string? Art, int Jahr, int? Monat, string? ErsetztId, string? Ziel,
        bool? Testmeldung, bool FreigabePortal, Dictionary<string, bool>? Adressaten, int? CompanyProfileId);

    [HttpPost("senden")]
    public async Task<IActionResult> Senden([FromBody] SendenBody dto, CancellationToken ct)
    {
        var art = ElmMeldungAnzeige.NormalisiereArt(dto.Art);
        try
        {
            var r = await _dienst.SendenAsync(art, dto.Jahr, dto.Monat, dto.ErsetztId, dto.Ziel,
                dto.Testmeldung, dto.FreigabePortal, dto.Adressaten, ct, dto.CompanyProfileId);
            return Ok(new
            {
                ok = r.Ok,
                meldung = r.Ok ? "Gesendet. OneCrew holt den Stand bei Swissdec jetzt ab." : r.Meldung,
                hinweise = r.Hinweise.Select(h => h.Text ?? h.Code),
                vorgangId = r.Vorgang?.Id,
                stand = Stand(),
            });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = "SENDEN", message = ex.Message }); }
    }

    [HttpPost("{id}/abfragen")]
    public async Task<IActionResult> Abfragen(string id, CancellationToken ct)
    {
        try
        {
            var r = await _dienst.AbfragenAsync(id, automatisch: false, ct);
            return Ok(new { ok = r.Ok, meldung = r.Fehler, stand = Stand() });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = "ABFRAGEN", message = ex.Message }); }
    }

    /// <summary>Eine Funktion für alle technischen Schritte (Swissdec-Testfall Aktualisieren).</summary>
    [HttpPost("aktualisieren")]
    public async Task<IActionResult> Aktualisieren(CancellationToken ct)
    {
        try
        {
            var r = await _dienst.AktualisierenAsync(ct);
            return Ok(new
            {
                ok = r.Fehler.Count == 0,
                geprueft = r.Geprueft,
                meldung = r.Fehler.Count == 0 ? null : string.Join(" · ", r.Fehler.Distinct()),
                stand = Stand(),
            });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = "AKTUALISIEREN", message = ex.Message }); }
    }

    public record AntwortBody(string AddresseeId, string StoryId, Dictionary<short, string?>? Werte);

    [HttpPost("{id}/antworten")]
    public async Task<IActionResult> Antworten(string id, [FromBody] AntwortBody dto, CancellationToken ct)
    {
        try
        {
            var r = await _dienst.AntwortenAsync(id, dto.AddresseeId, dto.StoryId, dto.Werte ?? new(), ct);
            return Ok(new
            {
                ok = r.Ok,
                meldung = r.Ok ? "Antwort gesendet." : r.Meldung,
                stand = Stand(),
            });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = "ANTWORTEN", message = ex.Message }); }
    }

    // ── Verbindung + Archiv ──────────────────────────────────────────────────

    [HttpGet("verbindung")]
    public IActionResult Verbindung() => Ok(new { ziel = _dienst.Ziel.Name, letzte = _dienst.LetzteVerbindung() });

    /// <summary>Ping + Testnachricht. Die Anwendermaske ruft das einmal beim Öffnen auf, nicht im Takt.</summary>
    [HttpPost("verbindung/pruefen")]
    public async Task<IActionResult> VerbindungPruefen(CancellationToken ct)
    {
        try
        {
            var von = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            return Ok(new { ziel = _dienst.Ziel.Name, letzte = await _dienst.VerbindungPruefenAsync(von, ct) });
        }
        catch (InvalidOperationException ex) { return Conflict(new { error = "VERBINDUNG", message = ex.Message }); }
    }

    [HttpGet("archiv/{name}")]
    public IActionResult ArchivDatei(string name)
    {
        var inhalt = _dienst.ArchivDatei(name);
        return inhalt == null
            ? NotFound(new { error = "NICHT_GEFUNDEN", message = "Archivdatei nicht gefunden." })
            : File(System.Text.Encoding.UTF8.GetBytes(inhalt), "application/xml", name);
    }

    // ── Stand ────────────────────────────────────────────────────────────────

    private object Stand()
    {
        var z = _dienst.Ziel;
        return new
        {
            ziel = new { z.Name, z.IstTest },
            ziele = ElmEndpunkte.MeldungsZiele().Select(x => new { x.Schluessel, x.Name }),
            sendenGesperrt = _dienst.SendenGesperrt(),
            meldungen = _dienst.Liste().Select(Projektion),
            anmeldungen = _dienst.Anmeldungen().Select(Projektion),
        };
    }

    /// <summary>Adressaten der Jahresmeldung, gruppiert wie im Cockpit.
    /// Mit companyProfileId nur die gültigen Lohndatenempfänger dieser Filiale.</summary>
    [HttpGet("domaenen")]
    public async Task<IActionResult> Domaenen(int jahr, int? companyProfileId, CancellationToken ct)
    {
        var heute = DateOnly.FromDateTime(DateTime.Now);
        var jahrVon = new DateOnly(jahr, 1, 1);
        var jahrBis = new DateOnly(jahr, 12, 31);
        var zuordnungen = companyProfileId is int cp && cp > 0
            ? (await _db.CompanyProfileEmpfaengers.AsNoTracking()
                .Include(z => z.Empfaenger)
                .Where(z => z.CompanyProfileId == cp && z.IsActive && z.Empfaenger != null && z.Empfaenger.IsActive)
                .ToListAsync(ct))
                .Where(z => GiltImJahr(z, jahrVon, jahrBis))
                .ToList()
            : null;
        var grenz = zuordnungen == null ? null : await GrenzArtenAsync(companyProfileId!.Value, jahr, ct);
        var genutzt = zuordnungen == null ? null : await GenutzteVersichererAsync(companyProfileId!.Value, jahr, ct);
        var empf = zuordnungen == null
            ? await _db.LohndatenEmpfaengers.AsNoTracking()
                .Include(e => e.Zuordnungen)
                .Where(e => e.IsActive)
                .OrderBy(e => e.Bezeichnung)
                .ToListAsync(ct)
            : new List<LohndatenEmpfaenger>();
        object Zeile(string id, string name, string an, string? art, bool lohnsummen,
            string? kennung, string? kundennummer, string? vertragsnummer, string? domain) => new
        {
            id, name,
            an = string.IsNullOrEmpty(an) ? "noch keiner zugeordnet" : an,
            art, lohnsummen, kennung, kundennummer, vertragsnummer, domain,
        };
        IEnumerable<object> AusKatalog(string prefix, string name, string[] arten, string? art, bool lohnsummen, string? domain)
        {
            if (zuordnungen != null)
            {
                var liste = zuordnungen.Where(z => arten.Contains(z.Empfaenger!.Art)).ToList();
                if (arten.Length == 1 && arten[0] is "UVG" or "UVGZ" or "KTG" or "BVG"
                    && genutzt != null && genutzt.TryGetValue(arten[0], out var ids))
                {
                    var sichtbar = UvgMehrereVersicherer.Sichtbar(liste.Select(z => z.EmpfaengerId).ToList(), ids);
                    liste = liste.Where(z => sichtbar.Contains(z.EmpfaengerId)).ToList();
                }
                if (liste.Count == 0) return Array.Empty<object>();
                return liste.Select(z => Zeile(prefix + "-" + z.Id, name, z.Empfaenger!.Bezeichnung.Trim(), art, lohnsummen,
                    z.Empfaenger.Kassennummer, z.Mitgliednummer, z.Subnummer, domain));
            }
            var alle = empf.Where(e => arten.Contains(e.Art) && e.Zuordnungen.Any(z =>
                z.GueltigBis == null || z.GueltigBis >= heute)).ToList();
            if (alle.Count == 0)
                return new[] { Zeile(prefix, name, "", art, lohnsummen, null, null, null, domain) };
            return alle.Select(e =>
            {
                var z = e.Zuordnungen.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Mitgliednummer));
                return Zeile(prefix + "-" + e.Id, name, e.Bezeichnung.Trim(), art, lohnsummen,
                    e.Kassennummer, z?.Mitgliednummer, z?.Subnummer, domain);
            });
        }
        IEnumerable<object> GrenzZeilen()
        {
            object ZeileArt(string id, string name) => Zeile(id, name, "Steuerverwaltung des Arbeitskantons", id, false, null, null, null, "TaxCrossborder");
            if (grenz == null)
                return new[]
                {
                    ZeileArt("grenz-sfn", "Grenzgänger SFN"),
                    ZeileArt("grenz-fr", "Grenzgänger Frankreich"),
                    ZeileArt("grenz-it", "Grenzgänger Italien"),
                };
            var liste = new List<object>();
            if (grenz.Contains("grenz-sfn")) liste.Add(ZeileArt("grenz-sfn", "Grenzgänger SFN"));
            if (grenz.Contains("grenz-fr")) liste.Add(ZeileArt("grenz-fr", "Grenzgänger Frankreich"));
            if (grenz.Contains("grenz-it")) liste.Add(ZeileArt("grenz-it", "Grenzgänger Italien"));
            return liste;
        }

        var gruppen = new object[]
        {
            new
            {
                id = "ausgleich", name = "Ausgleichskassen", portal = false,
                zeilen = AusKatalog("ahv", "AHV", new[] { "AUSGLEICHSKASSE" }, "annual", false, "AHV-AVS")
                    .Concat(AusKatalog("fak", "FAK", new[] { "FAK" }, null, false, "FAK-CAF")),
            },
            new
            {
                id = "versicherung", name = "Versicherungen", portal = true,
                zeilen = AusKatalog("uvg", "UVG", new[] { "UVG" }, null, false, "UVG-LAA")
                    .Concat(AusKatalog("uvgz", "UVGZ", new[] { "UVGZ" }, null, false, "UVGZ-LAAC"))
                    .Concat(AusKatalog("ktg", "KTG", new[] { "KTG" }, null, false, "KTG-AMC")),
            },
            new
            {
                id = "vorsorge", name = "Vorsorgeeinrichtungen", portal = true,
                zeilen = AusKatalog("bvg", "BVG", new[] { "BVG" }, null, true, "BVG-LPP"),
            },
            new
            {
                id = "steuern", name = "Steuern", portal = false,
                zeilen = AusKatalog("lohnausweis", "Lohnausweise", new[] { "LOHNAUSWEIS" }, null, false, "Tax")
                    .Concat(GrenzZeilen()),
            },
        };
        return Ok(new { jahr, companyProfileId, gruppen });
    }

    private static bool GiltImJahr(CompanyProfileEmpfaenger z, DateOnly von, DateOnly bis)
        => z.IsActive
           && (z.GueltigAb == null || z.GueltigAb <= bis)
           && (z.GueltigBis == null || z.GueltigBis >= von);

    private async Task<HashSet<string>> GrenzArtenAsync(int filiale, int jahr, CancellationToken ct)
    {
        var von = new DateOnly(jahr, 1, 1);
        var bis = new DateOnly(jahr, 12, 31);
        var yearStart = new DateTime(jahr, 1, 1);
        var yearEnd = new DateTime(jahr, 12, 31);
        var empIds = await _db.Employments.AsNoTracking()
            .Where(m => m.CompanyProfileId == filiale
                && m.ContractStartDate <= yearEnd
                && (m.ContractEndDate == null || m.ContractEndDate >= yearStart))
            .Select(m => m.EmployeeId)
            .Distinct()
            .ToListAsync(ct);
        if (empIds.Count == 0) return new HashSet<string>();
        var emps = await _db.Employees.AsNoTracking().Include(e => e.NationalityRef)
            .Where(e => empIds.Contains(e.Id) && !e.IsPayrollExcluded)
            .ToListAsync(ct);
        var ids = emps.Select(e => e.Id).ToList();
        var versionen = await _db.EmployeeQuellensteuer.AsNoTracking()
            .Where(q => ids.Contains(q.EmployeeId) && q.ValidFrom <= bis && (q.ValidTo == null || q.ValidTo >= von))
            .ToListAsync(ct);
        var arten = new HashSet<string>();
        foreach (var e in emps)
        {
            foreach (var art in GrenzgaengerMeldung.Arten(versionen.Where(q => q.EmployeeId == e.Id), e.Country, e.NationalityRef?.Code))
                arten.Add(art);
        }
        return arten;
    }

    private async Task<Dictionary<string, HashSet<int>>> GenutzteVersichererAsync(int filiale, int jahr, CancellationToken ct)
    {
        var von = new DateOnly(jahr, 1, 1);
        var bis = new DateOnly(jahr, 12, 31);
        var yearStart = new DateTime(jahr, 1, 1);
        var yearEnd = new DateTime(jahr, 12, 31);
        var empIds = await _db.Employments.AsNoTracking()
            .Where(m => m.CompanyProfileId == filiale
                && m.ContractStartDate <= yearEnd
                && (m.ContractEndDate == null || m.ContractEndDate >= yearStart))
            .Select(m => m.EmployeeId)
            .Distinct()
            .ToListAsync(ct);
        if (empIds.Count == 0) return new Dictionary<string, HashSet<int>>();
        var codes = await _db.EmployeeVersicherungCodes.AsNoTracking()
            .Where(c => empIds.Contains(c.EmployeeId)
                && c.EmpfaengerId != null
                && c.ValidFrom <= bis
                && (c.ValidTo == null || c.ValidTo >= von)
                && (c.Art == "UVG" || c.Art == "UVGZ" || c.Art == "KTG" || c.Art == "BVG"))
            .Select(c => new { c.Art, EmpfaengerId = c.EmpfaengerId!.Value })
            .ToListAsync(ct);
        return codes
            .GroupBy(c => c.Art)
            .ToDictionary(g => g.Key, g => g.Select(x => x.EmpfaengerId).ToHashSet());
    }

    [HttpGet("bvg-lohnsummen")]
    public async Task<IActionResult> BvgLohnsummen(int jahr, CancellationToken ct)
    {
        var summen = await (
            from s in _db.PayrollSnapshots.AsNoTracking()
            join p in _db.PayrollPerioden.AsNoTracking() on s.PayrollPeriodeId equals p.Id
            where p.Year == jahr && s.Status != "STORNIERT"
            group s by s.EmployeeId into g
            select new { EmployeeId = g.Key, Summe = g.Sum(x => x.SvBasisBvg) }
        ).ToListAsync(ct);
        var ids = summen.Select(s => s.EmployeeId).ToList();
        var namen = await _db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id) && !e.IsPayrollExcluded)
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .ToListAsync(ct);
        var gespeichert = _store.LadeBvgLohnsummen().FirstOrDefault(j => j.Jahr == jahr);
        var betrag = (gespeichert?.Zeilen ?? new()).ToDictionary(z => z.EmployeeId, z => z.Betrag);
        var zeilen = namen
            .OrderBy(n => n.FirstName).ThenBy(n => n.LastName)
            .Select(n =>
            {
                var vorschlag = summen.First(s => s.EmployeeId == n.Id).Summe;
                return new
                {
                    employeeId = n.Id,
                    name = $"{n.FirstName} {n.LastName}".Trim(),
                    vorschlag,
                    betrag = betrag.TryGetValue(n.Id, out var b) ? b : vorschlag,
                };
            }).ToList();
        return Ok(new
        {
            jahr,
            freigegeben = gespeichert?.Freigegeben == true,
            freigegebenAm = gespeichert?.FreigegebenAm,
            zeilen,
        });
    }

    public record BvgSpeichernBody(int Jahr, bool Freigegeben, List<ElmBvgZeile>? Zeilen);

    [HttpPost("bvg-lohnsummen")]
    public IActionResult BvgLohnsummenSpeichern([FromBody] BvgSpeichernBody dto)
    {
        if (dto.Jahr < 2000 || dto.Jahr > 2100)
            return BadRequest(new { error = "JAHR", message = "Jahr fehlt." });
        var alle = _store.LadeBvgLohnsummen();
        alle.RemoveAll(j => j.Jahr == dto.Jahr);
        alle.Add(new ElmBvgJahr
        {
            Jahr = dto.Jahr,
            Freigegeben = dto.Freigegeben,
            FreigegebenAm = dto.Freigegeben ? DateTime.Now : null,
            Zeilen = (dto.Zeilen ?? new()).Where(z => z.EmployeeId > 0).ToList(),
        });
        _store.SpeichereBvgLohnsummen(alle);
        return Ok(new { ok = true, freigegeben = dto.Freigegeben });
    }

    public record AnmeldenBody(string? Domain, string? Versicherer, string? Kundennummer, string? Vertragsnummer, string? Kennung);

    [HttpPost("anmelden")]
    public async Task<IActionResult> Anmelden([FromBody] AnmeldenBody dto, CancellationToken ct)
    {
        var hs = await _db.Hauptsitze.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Id).FirstOrDefaultAsync(ct);
        if (hs == null)
            return BadRequest(new { error = "HAUPTSITZ", message = "Kein Hauptsitz erfasst." });
        try
        {
            var r = await _dienst.AnmeldenAsync(new ElmUebermittlungService.SubscribeOptionen(
                hs.Uid, hs.Name, hs.KontaktName, dto.Kennung, dto.Domain, hs.Plz, hs.Ort,
                dto.Versicherer, dto.Kundennummer, dto.Vertragsnummer), ct);
            return Ok(new { ok = r.Ok, meldung = r.Meldung, stand = Stand() });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = "ANMELDEN", message = ex.Message }); }
    }

    [HttpPost("anmeldungen/{id}/abmelden")]
    public async Task<IActionResult> Abmelden(string id, [FromBody] AntwortBody dto, CancellationToken ct)
    {
        try
        {
            var r = await _dienst.AbmeldenAsync(id, dto.AddresseeId, ct);
            return Ok(new { ok = r.Ok, meldung = r.Meldung, stand = Stand() });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = "ABMELDEN", message = ex.Message }); }
    }

    /// <summary>Verlauf je Meldung in der Liste — die automatische Abfrage schreibt stündlich eine Zeile.</summary>
    private const int VerlaufMax = 200;

    private static object Projektion(ElmVorgang v)
    {
        var st = ElmMeldungAnzeige.Meldung(v);
        var (jahr, monat) = ElmMeldungAnzeige.Periode(v);
        return new
        {
            v.Id, v.Art, jahr, monat, v.CompanyProfileId, v.Gesendet, v.TestCase, v.FreigabePortal,
            declarationId = ElmMeldungAnzeige.DeclarationId(v),
            status = ElmMeldungAnzeige.CockpitStatus(v),
            statusText = ElmMeldungAnzeige.CockpitStatusText(ElmMeldungAnzeige.CockpitStatus(v)),
            qualityTool = v.Url == ElmEndpunkte.QualityToolUrl,
            titel = ElmMeldungAnzeige.Titel(v),
            ersatz = v.Substitution != null,
            zertifizierung = ElmMeldungAnzeige.Zertifizierungstest(v),
            stufe = st.Stufe, text = st.Text, aktionNoetig = st.AktionNoetig,
            fertig = v.JobFinished && v.Adressaten.All(ElmMeldungAnzeige.Erledigt),
            ersetzbar = ElmMeldungAnzeige.Ersetzbar(v),
            letzteAbfrage = new[] { v.LetzteStatusAbfrage }.Concat(v.Adressaten.Select(a => a.LetzteSynchronisierung)).Max(),
            gesendeteMeldung = v.Protokoll.FirstOrDefault(p => p.Schritt == "declare")?.ArchivAnfrage,
            verlauf = v.Protokoll.OrderByDescending(p => p.Zeit).Take(VerlaufMax).Select(p => new
            {
                p.Zeit, schritt = ElmVerbindung.Schritt(p.Schritt), p.Text,
                anfrage = p.ArchivAnfrage, antwort = p.ArchivAntwort,
            }),
            verlaufAnzahl = v.Protokoll.Count,
            empfaenger = v.Adressaten.Select(a =>
            {
                var e = ElmMeldungAnzeige.Empfaenger(a);
                var offen = a.State == "CompletionReleaseMissing" && a.Completion != null;
                var abgelaufen = offen && a.Completion!.Ablauf != null && a.Completion.Ablauf <= DateTime.Now;
                var freigabe = offen && !abgelaufen;
                return new
                {
                    a.AddresseeId,
                    a.Domain,
                    declarationId = a.FallId,
                    name = ElmMeldungAnzeige.EmpfaengerName(a.Domain, a.Identification, a.InstitutionName),
                    stufe = e.Stufe, text = e.Text,
                    hinweise = a.Hinweise.Where(h => !string.IsNullOrWhiteSpace(h.Text)).Select(h => h.Text),
                    freigabeUrl = freigabe ? a.Completion!.AufrufUrl : null,
                    freigabeBis = freigabe ? a.Completion!.Ablauf : null,
                    freigabeAbgelaufen = abgelaufen,
                    nachrichten = a.Stories.Select(s => new
                    {
                        s.StoryId, s.Empfangen, s.Beantwortet, s.Quittiert,
                        art = ElmMeldungAnzeige.NachrichtArt(s.Art),
                        dialog = s.Art == "DialogMessage" ? LiesDialog(s.Xml) : null,
                        s.Xml,
                    }),
                };
            }),
        };
    }

    private static ElmDialog.Nachricht? LiesDialog(string xml)
    {
        try { return ElmDialog.Lies(System.Xml.Linq.XElement.Parse(xml)); }
        catch { return null; }
    }
}

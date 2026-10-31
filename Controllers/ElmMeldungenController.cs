using HrSystem.Data;
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

    [HttpGet("vorschau")]
    public async Task<IActionResult> Vorschau(string art, int jahr, int? monat, CancellationToken ct)
    {
        art = ElmMeldungAnzeige.NormalisiereArt(art);
        try
        {
            var v = await _dienst.VorschauAsync(art, jahr, monat, ct);
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
        bool? Testmeldung, bool FreigabePortal, Dictionary<string, bool>? Adressaten);

    [HttpPost("senden")]
    public async Task<IActionResult> Senden([FromBody] SendenBody dto, CancellationToken ct)
    {
        var art = ElmMeldungAnzeige.NormalisiereArt(dto.Art);
        try
        {
            var r = await _dienst.SendenAsync(art, dto.Jahr, dto.Monat, dto.ErsetztId, dto.Ziel,
                dto.Testmeldung, dto.FreigabePortal, dto.Adressaten, ct);
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

    /// <summary>Adressaten der Jahresmeldung, gruppiert wie im Cockpit. Leer = im Katalog nicht erfasst.</summary>
    [HttpGet("domaenen")]
    public async Task<IActionResult> Domaenen(int jahr, CancellationToken ct)
    {
        var empf = await _db.LohndatenEmpfaengers.AsNoTracking()
            .Include(e => e.Zuordnungen)
            .Where(e => e.IsActive)
            .OrderBy(e => e.Bezeichnung)
            .ToListAsync(ct);
        object Zeile(string id, string name, string an, string? art, bool lohnsummen,
            string? kennung, string? kundennummer, string? vertragsnummer, string? domain) => new
        {
            id, name,
            an = string.IsNullOrEmpty(an) ? "noch kein Empfänger im Katalog" : an,
            art, lohnsummen, kennung, kundennummer, vertragsnummer, domain,
        };
        IEnumerable<object> AusKatalog(string prefix, string name, string[] arten, string? art, bool lohnsummen, string? domain)
        {
            var liste = empf.Where(e => arten.Contains(e.Art)).ToList();
            if (liste.Count == 0)
                return new[] { Zeile(prefix, name, "", art, lohnsummen, null, null, null, domain) };
            return liste.Select(e =>
            {
                var z = e.Zuordnungen.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Mitgliednummer));
                return Zeile(prefix + "-" + e.Id, name, e.Bezeichnung.Trim(), art, lohnsummen,
                    e.Kassennummer, z?.Mitgliednummer, z?.Subnummer, domain);
            });
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
                    .Concat(new object[]
                    {
                        Zeile("grenz-sfn", "Grenzgänger SFN", "Steuerverwaltung des Arbeitskantons", "grenz-sfn", false, null, null, null, "TaxCrossborder"),
                        Zeile("grenz-fr", "Grenzgänger Frankreich", "Steuerverwaltung des Arbeitskantons", "grenz-fr", false, null, null, null, "TaxCrossborder"),
                        Zeile("grenz-it", "Grenzgänger Italien", "Steuerverwaltung des Arbeitskantons", "grenz-it", false, null, null, null, "TaxCrossborder"),
                    }),
            },
        };
        return Ok(new { jahr, gruppen });
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
            v.Id, v.Art, jahr, monat, v.Gesendet, v.TestCase, v.FreigabePortal,
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
                var freigabe = a.State == "CompletionReleaseMissing" && a.Completion != null
                    && (a.Completion.Ablauf == null || a.Completion.Ablauf > DateTime.Now);
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

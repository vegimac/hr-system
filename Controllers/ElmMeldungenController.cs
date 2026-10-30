using HrSystem.Services.Elm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    public ElmMeldungenController(ElmMeldungenService dienst) => _dienst = dienst;

    [HttpGet]
    public IActionResult Liste() => Ok(Stand());

    [HttpGet("vorschau")]
    public async Task<IActionResult> Vorschau(string art, int jahr, int? monat, CancellationToken ct)
    {
        art = art == "annual" ? "annual" : "monthly";
        try
        {
            var v = await _dienst.VorschauAsync(art, jahr, monat, ct);
            return Ok(new
            {
                v.Titel, v.Firmenname, v.Uid,
                empfaenger = v.Adressaten.Select(a => new
                {
                    a.AddresseeId,
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

    public record SendenBody(string? Art, int Jahr, int? Monat, string? ErsetztId, string? Ziel);

    [HttpPost("senden")]
    public async Task<IActionResult> Senden([FromBody] SendenBody dto, CancellationToken ct)
    {
        var art = dto.Art == "annual" ? "annual" : "monthly";
        try
        {
            var r = await _dienst.SendenAsync(art, dto.Jahr, dto.Monat, dto.ErsetztId, dto.Ziel, ct);
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

    [Authorize(Roles = "admin")]
    [HttpGet("verbindung")]
    public IActionResult Verbindung() => Ok(new { ziel = _dienst.Ziel.Name, letzte = _dienst.LetzteVerbindung() });

    /// <summary>Ping + Testnachricht — nur auf Knopfdruck (Richtlinien: Ping nie automatisieren).</summary>
    [Authorize(Roles = "admin")]
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
        };
    }

    /// <summary>Verlauf je Meldung in der Liste — die automatische Abfrage schreibt stündlich eine Zeile.</summary>
    private const int VerlaufMax = 200;

    private static object Projektion(ElmVorgang v)
    {
        var st = ElmMeldungAnzeige.Meldung(v);
        var (jahr, monat) = ElmMeldungAnzeige.Periode(v);
        return new
        {
            v.Id, v.Art, jahr, monat, v.Gesendet, v.TestCase,
            qualityTool = v.Url == ElmEndpunkte.QualityToolUrl,
            titel = v.Art == "annual" ? $"Jahresmeldung {jahr}" : monat != null ? $"Monatsmeldung {monat:00}.{jahr}" : v.Titel,
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

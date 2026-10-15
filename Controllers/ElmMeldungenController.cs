using HrSystem.Services.Elm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrSystem.Controllers;

/// <summary>
/// Lohnmeldungen (ELM) für den Benutzer (Walter 15.10.2026, Swissdec-Zertifizierung): Meldung prüfen
/// und senden, Stand je Empfänger verfolgen, Rückfragen beantworten, Freigabe-Link, Ersatzmeldung.
/// Admin genügt; Adresse und Prüfschalter setzt der Server (<see cref="ElmMeldungenService"/>).
/// Der technische Kommunikations-Test bleibt unter <c>api/elm/uebermittlung</c> (nur Superadmin).
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
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

    public record SendenBody(string? Art, int Jahr, int? Monat, string? ErsetztId);

    [HttpPost("senden")]
    public async Task<IActionResult> Senden([FromBody] SendenBody dto, CancellationToken ct)
    {
        var art = dto.Art == "annual" ? "annual" : "monthly";
        try
        {
            var r = await _dienst.SendenAsync(art, dto.Jahr, dto.Monat, dto.ErsetztId, ct);
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

    // ── Stand ────────────────────────────────────────────────────────────────

    private object Stand()
    {
        var z = _dienst.Ziel;
        return new
        {
            ziel = new { z.Name, z.IstTest },
            sendenGesperrt = _dienst.SendenGesperrt(),
            meldungen = _dienst.Liste().Select(Projektion),
        };
    }

    private static object Projektion(ElmVorgang v)
    {
        var st = ElmMeldungAnzeige.Meldung(v);
        var (jahr, monat) = ElmMeldungAnzeige.Periode(v);
        return new
        {
            v.Id, v.Art, jahr, monat, v.Gesendet, v.TestCase,
            titel = v.Art == "annual" ? $"Jahresmeldung {jahr}" : monat != null ? $"Monatsmeldung {monat:00}.{jahr}" : v.Titel,
            ersatz = v.Substitution != null,
            stufe = st.Stufe, text = st.Text, aktionNoetig = st.AktionNoetig,
            fertig = v.JobFinished && v.Adressaten.All(ElmMeldungAnzeige.Erledigt),
            ersetzbar = ElmMeldungAnzeige.Ersetzbar(v),
            letzteAbfrage = new[] { v.LetzteStatusAbfrage }.Concat(v.Adressaten.Select(a => a.LetzteSynchronisierung)).Max(),
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
                        s.StoryId, s.Empfangen, s.Beantwortet,
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

using System.Text;
using System.Text.RegularExpressions;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.Elm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// EMA-Meldung NotifyChanges (E7, Walter 04.11.2026): Vorschau mit Referenzvergleich und die vom
/// Anwender auszulösenden Ereignisse nach RL 8.6.8. Der Versand folgt nach dem Abgleich auf test.onecrew.ch.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/elm/ema")]
[MaEintrag(typeof(ElmEmaManuell))]
public class ElmEmaController : HrControllerBase
{
    private static readonly HashSet<string> Mutationen = new() { "partialRetirement", "incapacityToWork", "fullyFitForWork" };
    private static readonly HashSet<string> Unterbrueche = new()
        { "interruptionOfEmployment", "interruptionOfEmploymentOnlyRisk", "interruptionOfEmploymentNotInsured" };

    private readonly ElmEmaBuilder _builder;

    public ElmEmaController(AppDbContext db, ElmEmaBuilder builder) : base(db) => _builder = builder;

    public sealed record ManuellDto(int EmployeeId, DateOnly Monat, string Art, string Grund, DateOnly? ValidAsOf);

    [HttpGet("{year:int}/{month:int}")]
    public async Task<IActionResult> Vorschau(int year, int month, CancellationToken ct)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return BadRequest(new { error = "PERIODE_UNGUELTIG", message = "Bitte Jahr und Monat gültig angeben." });
        var r = await _builder.BuildAsync(year, month, null, ct);
        var ordner = Path.Combine(Directory.GetCurrentDirectory(), "SWISSCEC", "RefXML");
        var refPfad = Path.Combine(ordner, $"RefXML_{year}-{month:00}_EMA.xml");
        object? vergleich = null;
        if (r.Xml.Length > 0 && System.IO.File.Exists(refPfad))
        {
            var e = ElmXmlVergleich.Vergleiche(r.Xml, await System.IO.File.ReadAllTextAsync(refPfad, ct));
            vergleich = new { datei = Path.GetFileName(refPfad), e.Geprueft, e.Offen, bericht = ElmXmlVergleich.Bericht(e, $"EMA {year}-{month:00}", Path.GetFileName(refPfad)) };
        }
        return Ok(new
        {
            xml = r.Xml,
            ereignisse = r.Ereignisse.Select(z => new
            {
                z.EmployeeId, z.Name, z.PersonalNr, z.Ereignis.Domaene, z.Ereignis.Art,
                validAsOf = z.Ereignis.ValidAsOf.ToString("yyyy-MM-dd"), z.Ereignis.Grund, z.Ereignis.BvgCode, z.Ereignis.Basis, z.Ereignis.Kanton, z.Arbeitsfaehig,
            }),
            warnungen = r.Warnungen, xsdFehler = r.XsdFehler, vergleich,
        });
    }

    /// <summary>
    /// Alle hochgeladenen RefXML_JJJJ-MM_EMA.xml erzeugen und Feld für Feld vergleichen. Die Referenzen
    /// liegen nicht auf dem Server und werden nur für diesen Aufruf gelesen. Es wird nichts gesendet.
    /// </summary>
    [HttpPost("pruefen")]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> Pruefen([FromForm] List<IFormFile> dateien, CancellationToken ct)
    {
        var quellen = new SortedDictionary<DateOnly, (string Name, string Xml)>();
        foreach (var f in dateien ?? new List<IFormFile>())
        {
            var name = Path.GetFileName(f.FileName ?? "");
            var m = Regex.Match(name, @"(\d{4})-(\d{2})_EMA");
            if (!m.Success) continue;
            using var leser = new StreamReader(f.OpenReadStream());
            quellen[new DateOnly(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), 1)] = (name, await leser.ReadToEndAsync(ct));
        }
        if (quellen.Count == 0)
            return BadRequest(new { error = "KEINE_REFERENZ", message = "Keine Datei RefXML_JJJJ-MM_EMA.xml dabei." });

        var monate = new List<object>();
        var bericht = new StringBuilder();
        for (var m = quellen.Keys.First(); m <= quellen.Keys.Last(); m = m.AddMonths(1))
        {
            var r = await _builder.BuildAsync(m.Year, m.Month, null, ct);
            int? geprueft = null, offen = null;
            if (quellen.TryGetValue(m, out var q) && r.Xml.Length > 0)
            {
                var e = ElmXmlVergleich.Vergleiche(r.Xml, q.Xml);
                (geprueft, offen) = (e.Geprueft, e.Offen);
                bericht.AppendLine(ElmXmlVergleich.Bericht(e, $"EMA {m:yyyy-MM}", q.Name));
            }
            monate.Add(new
            {
                monat = m.ToString("yyyy-MM"), referenz = quellen.ContainsKey(m), ereignisse = r.Ereignisse.Count,
                geprueft, offen, xsdFehler = r.XsdFehler, warnungen = r.Warnungen,
                fragen = r.Ereignisse.Where(z => z.FrageOffen).Select(z => new
                {
                    z.EmployeeId, z.Name, z.Ereignis.Art, validAsOf = z.Ereignis.ValidAsOf.ToString("yyyy-MM-dd"), monat = m.ToString("yyyy-MM-dd"),
                }),
            });
        }
        return Ok(new { monate, bericht = bericht.ToString() });
    }

    [HttpGet("manuell")]
    public async Task<IActionResult> Manuell([FromQuery] int employeeId, CancellationToken ct)
        => Ok(await _db.ElmEmaManuelle.AsNoTracking().Where(m => m.EmployeeId == employeeId).OrderBy(m => m.Monat).ToListAsync(ct));

    [HttpPost("manuell")]
    public async Task<IActionResult> Erfassen([FromBody] ManuellDto d, CancellationToken ct)
    {
        bool gueltig = d.Art switch
        {
            ElmEmaEreignisse.Mutation => Mutationen.Contains(d.Grund),
            ElmEmaEreignisse.Entry or ElmEmaEreignisse.Withdrawal => Unterbrueche.Contains(d.Grund),
            ElmEmaManuell.ArtFit => d.Grund is "ja" or "nein" && d.ValidAsOf != null,
            _ => false,
        };
        if (!gueltig) return BadRequest(new { error = "GRUND_UNGUELTIG", message = "Diese Kombination aus Art und Grund ist nicht manuell auslösbar." });
        if (!await _db.Employees.AnyAsync(e => e.Id == d.EmployeeId, ct)) return NotFound();
        // Die Antwort zur Arbeitsfähigkeit gibt es je Ereignis nur einmal.
        if (d.Art == ElmEmaManuell.ArtFit)
            _db.ElmEmaManuelle.RemoveRange(await _db.ElmEmaManuelle
                .Where(m => m.EmployeeId == d.EmployeeId && m.Art == ElmEmaManuell.ArtFit && m.ValidAsOf == d.ValidAsOf).ToListAsync(ct));
        var neu = new ElmEmaManuell
        {
            EmployeeId = d.EmployeeId, Monat = new DateOnly(d.Monat.Year, d.Monat.Month, 1),
            Art = d.Art, Grund = d.Grund, ValidAsOf = d.ValidAsOf, ErfasstAm = DateTime.Now, ErfasstVonUserId = GetCurrentUserId(), ErfasstVon = User.Identity?.Name,
        };
        _db.ElmEmaManuelle.Add(neu);
        await _db.SaveChangesAsync(ct);
        return Ok(new { neu.Id });
    }

    [HttpDelete("manuell/{id:int}")]
    public async Task<IActionResult> Loeschen(int id, CancellationToken ct)
    {
        var m = await _db.ElmEmaManuelle.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m == null) return NotFound();
        _db.ElmEmaManuelle.Remove(m);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

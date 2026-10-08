using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Stempelzeiten: READ-ONLY in Cowork (Walter-Vorgabe 17.05.2026).
///
/// Die Quelle der Wahrheit ist easy@work — sämtliche Erfassung, Korrektur
/// und Löschung passiert dort. Cowork zeigt die importierten Stempelzeiten
/// nur an. Der Import-Pfad (siehe <c>ImportController.ImportStempelzeiten</c>
/// und <c>ImportMonatlich</c>) schreibt direkt über _db und ist davon
/// nicht betroffen — der ist admin/superuser-only und idempotent.
///
/// Konkrete Konsequenz: POST/PUT/DELETE auf diesem Controller liefern
/// HTTP 403 mit klarer Meldung. Auch admin/superuser sind blockiert —
/// für Korrekturen geht der Weg über easy@work + Re-Import.
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/timeentries")]
public class EmployeeTimeEntriesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly HrSystem.Services.EasyAtWork.EasyAtWorkClient _eaw;
    public EmployeeTimeEntriesController(AppDbContext db, HrSystem.Services.EasyAtWork.EasyAtWorkClient eaw)
    {
        _db = db;
        _eaw = eaw;
    }

    // GET /api/employees/{employeeId}/timeentries/{id}/easy-verlauf
    // Kommentare + Änderungsprotokoll des Stempels live aus easy@work (read-only).
    // Roh durchgereicht: welche Felder easy@work dort liefert, ist nicht dokumentiert.
    [HttpGet("{id:int}/easy-verlauf")]
    public async Task<IActionResult> EasyVerlauf(int employeeId, int id, CancellationToken ct)
    {
        var entry = await _db.EmployeeTimeEntries.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.EmployeeId == employeeId, ct);
        if (entry is null) return NotFound();
        if (entry.EasyAtWorkTimepunchId is not int tpId || entry.EasyAtWorkCustomerId is not int custId)
            return Ok(new { verfuegbar = false, grund = "Stempel ohne easy@work-Verknüpfung." });

        var basis = $"customers/{custId}/timepunches/{tpId}";
        var versuche = new List<object>();
        async Task<System.Text.Json.JsonElement?> Hole(string query)
        {
            var (status, body) = await _eaw.GetRawAsync(basis + query, ct);
            var ok = status >= 200 && status < 300;
            versuche.Add(new
            {
                abfrage = string.IsNullOrEmpty(query) ? "(ohne Zusatz)" : Uri.UnescapeDataString(query),
                status,
                meldung = ok ? null : (body.Length > 600 ? body[..600] + " …" : body),
            });
            if (!ok) return null;
            try
            {
                var root = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body);
                return root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty("data", out var d) ? d : root;
            }
            catch { return null; }
        }
        static System.Text.Json.JsonElement? Teil(System.Text.Json.JsonElement? el, string name)
            => el is { ValueKind: System.Text.Json.JsonValueKind.Object } o && o.TryGetProperty(name, out var v) ? v : null;

        var alles = await Hole(HrSystem.Services.EasyAtWork.EasyAtWorkClient.TimepunchMitAllem);
        System.Text.Json.JsonElement? mitChangelog = alles, mitKommentaren = alles;
        if (alles is null)
        {
            mitChangelog   = await Hole(HrSystem.Services.EasyAtWork.EasyAtWorkClient.TimepunchNurChangelog);
            mitKommentaren = await Hole(HrSystem.Services.EasyAtWork.EasyAtWorkClient.TimepunchNurKommentare);
        }
        var daten = alles ?? mitChangelog ?? mitKommentaren ?? await Hole("");
        if (daten is null)
            return Ok(new { verfuegbar = false, grund = "easy@work lehnt jede Abfrage dieses Stempels ab.", versuche });

        return Ok(new
        {
            verfuegbar = true,
            timepunchId = tpId,
            createdAt = Teil(daten, "created_at"),
            updatedAt = Teil(daten, "updated_at"),
            comments  = Teil(mitKommentaren, "comments"),
            changelog = Teil(mitChangelog, "changelog"),
            gespeichert = new { kommentar = entry.Comment, protokoll = entry.OriginalComment },
            versuche,
            roh = daten,
            // Zweites Fenster nur, wenn Changelog und Kommentare aus getrennten
            // Abfragen kamen — sonst ist rohKommentare = roh (Doppel-Anzeige).
            rohKommentare = alles is null && mitChangelog is not null ? mitKommentaren : null,
        });
    }

    private static IActionResult ReadOnlyResponse() => new ObjectResult(new
    {
        error   = "STEMPELZEITEN_READONLY",
        message = "Stempelzeiten werden in easy@work verwaltet — in Cowork nur Anzeige. " +
                  "Für Korrekturen bitte in easy@work erfassen und anschliessend Stempelzeiten neu importieren."
    })
    { StatusCode = StatusCodes.Status403Forbidden };

    // GET /api/employees/{employeeId}/timeentries?dateFrom=2026-02-21&dateTo=2026-03-20
    // GET /api/employees/{employeeId}/timeentries?year=2026&month=3  (calendar month fallback)
    [HttpGet]
    public async Task<IActionResult> GetAll(
        int employeeId,
        [FromQuery] string? dateFrom,
        [FromQuery] string? dateTo,
        [FromQuery] int? year,
        [FromQuery] int? month)
    {
        var query = _db.EmployeeTimeEntries
            .Where(t => t.EmployeeId == employeeId);

        DateOnly.TryParse(dateFrom, out var from);
        DateOnly.TryParse(dateTo,   out var to);
        var hasDateRange = !string.IsNullOrEmpty(dateFrom) && !string.IsNullOrEmpty(dateTo);

        if (hasDateRange)
        {
            query = query.Where(t => t.EntryDate >= from && t.EntryDate <= to);
        }
        else
        {
            if (year.HasValue)
                query = query.Where(t => t.EntryDate.Year == year.Value);
            if (month.HasValue)
                query = query.Where(t => t.EntryDate.Month == month.Value);
        }

        var entries = await query
            .OrderBy(t => t.EntryDate)
            .ThenBy(t => t.TimeIn)
            .ToListAsync();

        return Ok(entries);
    }

    // GET /api/employees/{employeeId}/timeentries/periods
    // Liefert alle Jahre/Monate, in denen für diesen MA Einträge existieren (neueste zuerst)
    [HttpGet("periods")]
    public async Task<IActionResult> GetPeriods(int employeeId)
    {
        var periods = await _db.EmployeeTimeEntries
            .Where(t => t.EmployeeId == employeeId)
            .GroupBy(t => new { t.EntryDate.Year, t.EntryDate.Month })
            .Select(g => new {
                year  = g.Key.Year,
                month = g.Key.Month,
                count = g.Count()
            })
            .OrderByDescending(x => x.year)
            .ThenByDescending(x => x.month)
            .ToListAsync();
        return Ok(periods);
    }

    // GET /api/employees/{employeeId}/timeentries/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int employeeId, int id)
    {
        var entry = await _db.EmployeeTimeEntries
            .FirstOrDefaultAsync(t => t.Id == id && t.EmployeeId == employeeId);
        return entry is null ? NotFound() : Ok(entry);
    }

    // POST /api/employees/{employeeId}/timeentries
    // READ-ONLY: easy@work ist die Quelle der Wahrheit. Manuelle Erfassung
    // in Cowork ist nicht erlaubt — auch nicht für admin/superuser.
    [HttpPost]
    public IActionResult Create(int employeeId, [FromBody] EmployeeTimeEntry dto)
        => ReadOnlyResponse();

    // PUT /api/employees/{employeeId}/timeentries/{id} — siehe POST.
    [HttpPut("{id:int}")]
    public IActionResult Update(int employeeId, int id, [FromBody] EmployeeTimeEntry dto)
        => ReadOnlyResponse();

    // DELETE /api/employees/{employeeId}/timeentries/{id} — siehe POST.
    [HttpDelete("{id:int}")]
    public IActionResult Delete(int employeeId, int id)
        => ReadOnlyResponse();
}

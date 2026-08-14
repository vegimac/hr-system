using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.EasyAtWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HrSystem.Controllers;

/// <summary>
/// Endpoints für die easy@work-API-Integration. Phase 1 = Foundation:
/// Status anzeigen, Verbindung testen, Branch-Mapping pflegen.
/// Phase 2+ (Sync) baut auf diesen Endpoints auf.
///
/// Schreib-/Massen-Sync bleibt admin (Methoden-Attribute).
/// Einzel-MA-Sync + Lesen: auch GF (user) — Walter 20.07.2026.
/// </summary>
[ApiController]
[Route("api/easywork")]
[Authorize(Roles = "admin,superuser,user")]
public class EasyAtWorkController : ControllerBase
{
    private readonly EasyAtWorkClient _client;
    private readonly AppDbContext _db;
    private readonly ILogger<EasyAtWorkController> _log;
    private readonly Services.EasyAtWork.EasyAtWorkTimepunchSyncService _tpSync;
    private readonly Services.EasyAtWork.EasyAtWorkEmployeeSyncService  _empSync;
    private readonly LohnEditLockService _editLock;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Services.EasyAtWork.EasyAtWorkImportJobService _importJobs;

    public EasyAtWorkController(
        EasyAtWorkClient client,
        AppDbContext db,
        ILogger<EasyAtWorkController> log,
        Services.EasyAtWork.EasyAtWorkTimepunchSyncService tpSync,
        Services.EasyAtWork.EasyAtWorkEmployeeSyncService empSync,
        LohnEditLockService editLock,
        IServiceScopeFactory scopeFactory,
        Services.EasyAtWork.EasyAtWorkImportJobService importJobs)
    {
        _client = client;
        _db = db;
        _log = log;
        _tpSync = tpSync;
        _empSync = empSync;
        _editLock = editLock;
        _scopeFactory = scopeFactory;
        _importJobs = importJobs;
    }

    // ─────────────────────── API-Dump (Diagnose) ────────────────────

    /// <summary>
    /// Diagnose: holt für einen MA (Personalnummer ODER Alt-Nummer) ALLE roh-
    /// JSON-Antworten der erreichbaren easy@work-Endpoints — inkl. nicht
    /// gemappter Felder und Discovery-Versuche für Funktion/Gruppen. Nur zum
    /// Anschauen (kein Schreiben). Walter-Vorgabe 22.06.2026.
    /// </summary>
    [HttpGet("debug/employee-dump")]
    public async Task<IActionResult> EmployeeDump(
        [FromQuery] int companyProfileId, [FromQuery] string number, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(number))
            return BadRequest(new { error = "NUMBER_REQUIRED" });
        var num = number.Trim();

        var mapping = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyProfileId == companyProfileId, ct);
        if (mapping == null)
            return BadRequest(new { error = "NO_MAPPING", message = "Filiale hat kein easy@work-Mapping." });
        int customerId = mapping.EasyAtWorkCustomerId;

        // easy@work-ID des MA über die EMPLOYEE-LISTE des Customers per Nummer
        // auflösen — das liefert die echte Resource-Id (NICHT die UserId, die wir
        // gespeichert haben und die der /employees/{id}-Endpoint nicht akzeptiert).
        List<EawEmployee> eawList;
        try { eawList = await _client.GetAllEmployeesIncludingInactiveAsync(customerId, ct); }
        catch (Exception ex) { return StatusCode(502, new { error = "EAW_LIST_FAILED", message = ex.Message }); }
        var match = eawList.FirstOrDefault(e => (e.Number ?? "").Trim() == num);
        if (match == null)
            return NotFound(new
            {
                error = "NOT_IN_CUSTOMER",
                message = $"Personalnr. {num} nicht in easy@work-Customer {customerId} gefunden ({eawList.Count} MA in der Liste). Evtl. falsche Filiale gewählt oder MA gehört zu einem anderen Customer.",
                customerId, listCount = eawList.Count
            });
        int eid = match.Id;
        var storedCoworkEawId = (await _db.Employees.AsNoTracking()
            .Where(e => e.EmployeeNumber == num).Select(e => e.EasyAtWorkEmployeeId).FirstOrDefaultAsync(ct));

        // Alle bekannten Endpoints + Discovery-Versuche für Funktion/Gruppen.
        var paths = new[]
        {
            $"customers/{customerId}/employees/{eid}",
            $"customers/{customerId}/employees/{eid}/contracts",
            $"customers/{customerId}/employees/{eid}/pay_rates",
            $"customers/{customerId}/employees/{eid}/fiscal_info",
            $"customers/{customerId}/employees/{eid}/properties?per_page=200",
            $"customers/{customerId}/employees/{eid}/functions",
            $"customers/{customerId}/employees/{eid}/function",
            $"customers/{customerId}/employees/{eid}/groups",
            $"customers/{customerId}/employees/{eid}/group_memberships",
            $"customers/{customerId}/employees/{eid}/memberships",
            $"customers/{customerId}/employees/{eid}/roles",
            $"customers/{customerId}/employees/{eid}/positions",
            // Verfügbarkeit / gewünschte Arbeitszeiten — Kandidaten-Pfade (Walter 07.07.2026):
            // easy@work zeigt das im UI («Verfügbarkeit»: Wochen-Muster + gewünschte Tage +
            // Genehmigung). Wir probieren mehrere Endpunkt-Namen durch; welcher Status 200 +
            // Daten liefert, ist der richtige → danach den echten Sync anhängen.
            $"customers/{customerId}/employees/{eid}/availabilities",
            $"customers/{customerId}/employees/{eid}/availability",
            $"customers/{customerId}/employees/{eid}/desired_days",
            $"customers/{customerId}/employees/{eid}/desired_availabilities",
            $"customers/{customerId}/employees/{eid}/availability_requests",
            $"customers/{customerId}/employees/{eid}/preferences",
            $"customers/{customerId}/employees/{eid}/schedules",
            $"customers/{customerId}/employees/{eid}/weekly_availabilities",
        };

        object ParseBody(string b)
        {
            if (string.IsNullOrWhiteSpace(b)) return "";
            try { return JsonSerializer.Deserialize<JsonElement>(b); }
            catch { return b; }
        }

        var results = new List<object>();
        foreach (var p in paths)
        {
            try
            {
                var (status, body) = await _client.GetRawAsync(p, ct);
                results.Add(new { path = p, status, body = ParseBody(body) });
            }
            catch (Exception ex)
            {
                results.Add(new { path = p, status = -1, error = ex.Message });
            }
        }

        // Verschollen-Diagnose (Walter 06.08.2026): steht der MA HEUTE in der
        // ?active=-Liste? Genau diese Liste nutzt der Verschollen-Wächter —
        // fehlt der MA hier trotz offenem to=null, filtert easy vertragsbasiert
        // (kein laufender Vertrag/Pay-rate in easy).
        object activeListCheck;
        try
        {
            var activeRows = await _client.GetAllEmployeesActiveAtAsync(
                customerId, DateOnly.FromDateTime(DateTime.Today), ct);
            var inList = activeRows.Any(r => r.Id == eid
                || (match.UserId.HasValue && r.UserId == match.UserId));
            activeListCheck = new
            {
                heuteInAktivListe = inList,
                aktivListeCount   = activeRows.Count,
                hinweis = inList
                    ? "MA ist in der Aktivliste — Verschollen-Warnung sollte sich beim nächsten Check aufheben."
                    : "MA FEHLT in der Aktivliste (?active=heute) trotz offenem Austritt — easy filtert vertragsbasiert: Vertrag/Pay-rate in easy prüfen (Einsatz & Vertragsinfos).",
            };
        }
        catch (Exception ex)
        {
            activeListCheck = new { error = ex.Message };
        }

        return Ok(new
        {
            number = num,
            customerId,
            easyAtWorkResourceId = eid,        // für /employees/{id} verwendet
            easyAtWorkUserId     = match.UserId,
            storedCoworkEawId    = storedCoworkEawId,   // bei uns gespeichert (i.d.R. UserId)
            activeListCheck,
            results
        });
    }

    // Verschollen-Check manuell anstossen (Walter 06.08.2026): gleicher Lauf
    // wie im Nacht-Sync — setzt Markierungen UND hebt sie auf, wenn ein MA
    // wieder in einer Aktivliste steht. So muss nach einer easy-Korrektur
    // nicht bis zum nächsten Morgen gewartet werden.
    [HttpPost("verschollen-check")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> VerschollenCheck(
        [FromServices] EasyAtWorkEmployeeSyncService empSync, CancellationToken ct)
    {
        var notes = await empSync.CheckVerscholleneAsync(ct);
        return Ok(new { notes });
    }

    // Generischer Roh-Pfad-Prober (Walter 07.07.2026): fragt einen BELIEBIGEN
    // easy@work-API-Pfad ab und gibt Status + Body zurück. Für die Endpunkt-
    // Discovery (z.B. availabilities/{id} + Unter-Ressourcen für die Zeitfenster).
    [HttpGet("debug/raw")]
    public async Task<IActionResult> DebugRaw([FromQuery] string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest(new { error = "Query-Parameter «path» fehlt (z.B. customers/769/employees/8039/availabilities/775081)." });
        var clean = path.TrimStart('/');
        try
        {
            var (status, body) = await _client.GetRawAsync(clean, ct);
            object parsed;
            try { parsed = JsonSerializer.Deserialize<JsonElement>(body); }
            catch { parsed = body; }
            return Ok(new { path = clean, status, body = parsed });
        }
        catch (Exception ex)
        {
            return Ok(new { path = clean, status = -1, error = ex.Message });
        }
    }

    /// <summary>
    /// Verfügbarkeits-Dump (Walter 09.07.2026): easy@work-Support hat die
    /// Endpunkte bestätigt — GET …/availabilities/{availability}/days (+ /days/{day}).
    /// Holt für einen MA (Personalnummer) die Verfügbarkeits-LISTE und pro
    /// Verfügbarkeit die kompletten /days roh — zum Anschauen der JSON-Struktur,
    /// BEVOR der echte Sync gebaut wird. Read-only.
    /// </summary>
    [HttpGet("debug/availability-dump")]
    public async Task<IActionResult> AvailabilityDump(
        [FromQuery] int companyProfileId, [FromQuery] string number, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(number))
            return BadRequest(new { error = "NUMBER_REQUIRED" });
        var num = number.Trim();

        var mapping = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyProfileId == companyProfileId, ct);
        if (mapping == null)
            return BadRequest(new { error = "NO_MAPPING", message = "Filiale hat kein easy@work-Mapping." });
        int customerId = mapping.EasyAtWorkCustomerId;

        List<EawEmployee> eawList;
        try { eawList = await _client.GetAllEmployeesIncludingInactiveAsync(customerId, ct); }
        catch (Exception ex) { return StatusCode(502, new { error = "EAW_LIST_FAILED", message = ex.Message }); }
        var match = eawList.FirstOrDefault(e => (e.Number ?? "").Trim() == num);
        if (match == null)
            return NotFound(new { error = "NOT_IN_CUSTOMER", message = $"Personalnr. {num} nicht in easy@work-Customer {customerId} gefunden." });
        int eid = match.Id;

        object ParseBody(string b)
        {
            if (string.IsNullOrWhiteSpace(b)) return "";
            try { return JsonSerializer.Deserialize<JsonElement>(b); }
            catch { return b; }
        }

        // 1) Liste der Verfügbarkeiten
        var listPath = $"customers/{customerId}/employees/{eid}/availabilities";
        int listStatus; string listBody;
        try { (listStatus, listBody) = await _client.GetRawAsync(listPath, ct); }
        catch (Exception ex) { return StatusCode(502, new { error = "EAW_AVAIL_FAILED", message = ex.Message }); }
        var results = new List<object> { new { path = listPath, status = listStatus, body = ParseBody(listBody) } };

        // 2) Availability-Ids tolerant aus dem JSON fischen (Array direkt ODER
        //    unter «data»/«availabilities») und pro Id die /days holen.
        var ids = new List<long>();
        try
        {
            using var doc = JsonDocument.Parse(listBody);
            var root = doc.RootElement;
            var arr = root.ValueKind == JsonValueKind.Array ? root
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array ? d
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("availabilities", out var av) && av.ValueKind == JsonValueKind.Array ? av
                : default;
            if (arr.ValueKind == JsonValueKind.Array)
                foreach (var item in arr.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var idVal))
                        ids.Add(idVal);
        }
        catch { /* Struktur unbekannt — dann liefert der Dump nur die Liste */ }

        foreach (var aid in ids.Take(20))
        {
            var daysPath = $"customers/{customerId}/employees/{eid}/availabilities/{aid}/days";
            try
            {
                var (st, body) = await _client.GetRawAsync(daysPath, ct);
                results.Add(new { path = daysPath, status = st, body = ParseBody(body) });
            }
            catch (Exception ex) { results.Add(new { path = daysPath, status = -1, error = ex.Message }); }
        }

        return Ok(new { number = num, customerId, easyAtWorkResourceId = eid, availabilityIds = ids, results });
    }

    /// <summary>
    /// Absenzen-Probe (Walter 09.08.2026; Pfade am 14.08.2026 vom
    /// easy@work-Support BESTÄTIGT): read-only Dump der Absenz-Quellen —
    /// absence_types (Katalog), absences («unforeseen»: Krankheit etc.) und
    /// off_times («planned»: Ferien/Freizeit; vacation=true = Ferien) auf
    /// Customer- und MA-Ebene. Datums-Semantik laut Support: `dates` = UTC,
    /// `business_dates` = lokales DATUM mit 00:00:00 (Zeitanteil verwerfen);
    /// die Antwort nennt die Zuordnung in `_dates[]`/`_business_dates[]`.
    /// Auf dieser Basis bauen wir den Absenz-Sync. Es wird NICHTS geschrieben.
    /// </summary>
    [HttpGet("debug/absence-probe")]
    public async Task<IActionResult> AbsenceProbe(
        [FromQuery] int companyProfileId, [FromQuery] string? number, CancellationToken ct)
    {
        var mapping = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyProfileId == companyProfileId, ct);
        if (mapping == null)
            return BadRequest(new { error = "NO_MAPPING", message = "Filiale hat kein easy@work-Mapping." });
        int customerId = mapping.EasyAtWorkCustomerId;

        // Optional: Personalnummer → easy@work-Employee-Id (für MA-Ebene-Pfade).
        int? eid = null;
        if (!string.IsNullOrWhiteSpace(number))
        {
            try
            {
                var eawList = await _client.GetAllEmployeesIncludingInactiveAsync(customerId, ct);
                eid = eawList.FirstOrDefault(e => (e.Number ?? "").Trim() == number.Trim())?.Id;
                if (eid == null)
                    return NotFound(new { error = "NOT_IN_CUSTOMER", message = $"Personalnr. {number} nicht in easy@work-Customer {customerId} gefunden." });
            }
            catch (Exception ex) { return StatusCode(502, new { error = "EAW_LIST_FAILED", message = ex.Message }); }
        }

        // Bestätigte Endpunkte (easy@work-Support 14.08.2026). Die früher
        // geratenen Pfade (vacations/leaves/leave_requests/holidays) sind raus.
        var pfade = new List<string>
        {
            $"customers/{customerId}/absence_types?per_page=100",
            $"customers/{customerId}/absences?per_page=10",
            $"customers/{customerId}/off_times?per_page=10",
        };
        if (eid.HasValue)
        {
            pfade.Add($"customers/{customerId}/employees/{eid}/absences?per_page=100");
            pfade.Add($"customers/{customerId}/employees/{eid}/off_times?per_page=100");
        }

        object ParseBody(string b)
        {
            if (string.IsNullOrWhiteSpace(b)) return "";
            try { return JsonSerializer.Deserialize<JsonElement>(b); }
            catch { return b.Length > 3000 ? b[..3000] + "…" : b; }
        }

        var results = new List<object>();
        foreach (var p in pfade)
        {
            try
            {
                var (st, body) = await _client.GetRawAsync(p, ct);
                results.Add(new { path = p, status = st, body = ParseBody(body) });
            }
            catch (Exception ex) { results.Add(new { path = p, status = -1, error = ex.Message }); }
        }

        return Ok(new
        {
            customerId,
            easyAtWorkResourceId = eid,
            hinweis = "Support-bestätigte Endpunkte: absence_types (Katalog), absences (Krankheit etc.), off_times (Ferien/Freizeit; vacation=true = Ferien). "
                    + "ACHTUNG UTC (Walter 14.08.2026): `dates`-Felder kommen wie ALLE easy@work-Timestamps in UTC → beim Sync IMMER EawDateUtil (UTC→Europe/Zurich), NIE den Roh-String als Kalendertag nehmen. "
                    + "`business_dates` sollen laut Support lokale Daten mit 00:00:00 sein — an echten Beispielen (Absenz mit bekanntem Datum) GEGENPRÜFEN, bevor der Sync gebaut wird. Die Antwort nennt die Zuordnung in _dates[]/_business_dates[].",
            results,
        });
    }

    /// <summary>
    /// Absenz-Sync easy@work → OneCrew (Walter 14.08.2026): Vorschau + Commit.
    /// Details/Mapping siehe EasyAtWorkAbsenceSyncService. vonDatum default
    /// 01.01.2026 (Vergangenheit = Mirus-Import).
    /// </summary>
    [Authorize(Roles = "admin,superuser")]
    [HttpPost("absence-sync")]
    public async Task<IActionResult> AbsenceSync(
        [FromQuery] int companyProfileId,
        [FromQuery] string? von,
        [FromQuery] bool dryRun,
        [FromServices] EasyAtWorkAbsenceSyncService syncService,
        CancellationToken ct)
    {
        var vonDatum = DateOnly.TryParse(von, out var vd) ? vd : new DateOnly(2026, 1, 1);
        try
        {
            var r = await syncService.RunAsync(companyProfileId, vonDatum, dryRun, ct);
            return Ok(new
            {
                dryRun,
                von = vonDatum.ToString("yyyy-MM-dd"),
                neu = r.Neu,
                geaendert = r.Geaendert,
                geloescht = r.Geloescht,
                schonErfasst = r.SchonErfasst,
                fehler = r.Fehler,
                uebersprungen = r.Uebersprungen,
                zeilen = r.Zeilen,
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = "SYNC_FEHLER", message = ex.Message });
        }
    }

    /// <summary>
    /// Diagnose-Dump NACH easy@work-ID (Walter 29.06.2026): holt für eine direkt
    /// angegebene easy@work-employee-Id ALLE erreichbaren Roh-JSON-Antworten. Der
    /// passende Customer wird automatisch über ALLE gemappten Filialen gesucht
    /// (die ID kann in einer beliebigen Filiale liegen) — so sieht man, welche
    /// Personalnummer easy@work für diese ID liefert. Read-only.
    /// </summary>
    [HttpGet("debug/employee-dump-by-id")]
    public async Task<IActionResult> EmployeeDumpById(
        [FromQuery] int easyAtWorkId, [FromQuery] int? companyProfileId, CancellationToken ct)
    {
        if (easyAtWorkId <= 0)
            return BadRequest(new { error = "ID_REQUIRED", message = "Bitte eine easy@work-ID angeben." });

        var mappings = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .Select(m => new { m.CompanyProfileId, m.EasyAtWorkCustomerId })
            .ToListAsync(ct);
        if (mappings.Count == 0)
            return BadRequest(new { error = "NO_MAPPING", message = "Keine easy@work-Branch-Mappings vorhanden." });

        // Reihenfolge: zuerst die gewählte Filiale (falls übergeben), dann alle
        // übrigen Customer — wir probieren, bei welchem /employees/{id} 2xx liefert.
        var customerIds = new List<int>();
        if (companyProfileId.HasValue)
        {
            var sel = mappings.FirstOrDefault(m => m.CompanyProfileId == companyProfileId.Value);
            if (sel != null) customerIds.Add(sel.EasyAtWorkCustomerId);
        }
        foreach (var cid in mappings.Select(m => m.EasyAtWorkCustomerId).Distinct())
            if (!customerIds.Contains(cid)) customerIds.Add(cid);

        int? foundCustomer = null;
        var probe = new List<object>();
        foreach (var cid in customerIds)
        {
            try
            {
                var (status, _) = await _client.GetRawAsync($"customers/{cid}/employees/{easyAtWorkId}", ct);
                probe.Add(new { customerId = cid, status });
                if (status >= 200 && status < 300) { foundCustomer = cid; break; }
            }
            catch (Exception ex) { probe.Add(new { customerId = cid, status = -1, error = ex.Message }); }
        }
        if (foundCustomer == null)
            return NotFound(new
            {
                error = "NOT_FOUND",
                message = $"easy@work-ID {easyAtWorkId} in keinem der {customerIds.Count} gemappten Customer gefunden.",
                checkedCustomers = probe
            });

        int customerId = foundCustomer.Value;
        int eid = easyAtWorkId;

        var paths = new[]
        {
            $"customers/{customerId}/employees/{eid}",
            $"customers/{customerId}/employees/{eid}/contracts",
            $"customers/{customerId}/employees/{eid}/pay_rates",
            $"customers/{customerId}/employees/{eid}/fiscal_info",
            $"customers/{customerId}/employees/{eid}/properties?per_page=200",
            $"customers/{customerId}/employees/{eid}/functions",
            $"customers/{customerId}/employees/{eid}/function",
            $"customers/{customerId}/employees/{eid}/groups",
            $"customers/{customerId}/employees/{eid}/group_memberships",
            $"customers/{customerId}/employees/{eid}/memberships",
            $"customers/{customerId}/employees/{eid}/roles",
            $"customers/{customerId}/employees/{eid}/positions",
            // Verfügbarkeit / gewünschte Arbeitszeiten — Kandidaten-Pfade (Walter 07.07.2026):
            // easy@work zeigt das im UI («Verfügbarkeit»: Wochen-Muster + gewünschte Tage +
            // Genehmigung). Wir probieren mehrere Endpunkt-Namen durch; welcher Status 200 +
            // Daten liefert, ist der richtige → danach den echten Sync anhängen.
            $"customers/{customerId}/employees/{eid}/availabilities",
            $"customers/{customerId}/employees/{eid}/availability",
            $"customers/{customerId}/employees/{eid}/desired_days",
            $"customers/{customerId}/employees/{eid}/desired_availabilities",
            $"customers/{customerId}/employees/{eid}/availability_requests",
            $"customers/{customerId}/employees/{eid}/preferences",
            $"customers/{customerId}/employees/{eid}/schedules",
            $"customers/{customerId}/employees/{eid}/weekly_availabilities",
        };

        object ParseBody(string b)
        {
            if (string.IsNullOrWhiteSpace(b)) return "";
            try { return JsonSerializer.Deserialize<JsonElement>(b); }
            catch { return b; }
        }

        var results = new List<object>();
        foreach (var p in paths)
        {
            try
            {
                var (status, body) = await _client.GetRawAsync(p, ct);
                results.Add(new { path = p, status, body = ParseBody(body) });
            }
            catch (Exception ex)
            {
                results.Add(new { path = p, status = -1, error = ex.Message });
            }
        }

        // Was haben wir bei dieser ID in Cowork gespeichert (zum Abgleich)?
        var stored = await _db.Employees.AsNoTracking()
            .Where(e => e.EasyAtWorkEmployeeId == easyAtWorkId)
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName })
            .FirstOrDefaultAsync(ct);

        return Ok(new
        {
            easyAtWorkResourceId = eid,
            customerId,
            storedInCowork = stored,   // null = bei uns ist diese ID (noch) nicht gespeichert
            results
        });
    }

    // ─────────────────────────── Status ─────────────────────────────

    /// <summary>
    /// Zeigt, ob die Integration konfiguriert ist (kein Secret-Leak — nur
    /// `configured: true/false` + die nicht-geheimen Felder BaseUrl/ClientId).
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            configured = _client.IsConfigured,
            baseUrl    = _client.IsConfigured ? _client.BaseUrl  : null,
            clientId   = _client.IsConfigured ? _client.ClientId : null,
        });
    }

    /// <summary>
    /// Testet die Verbindung: holt einen Token + ruft GET /customers. Liefert
    /// bei Erfolg die Liste der für unseren API-Client sichtbaren Customers
    /// (Filialen) — daraus baut der Admin im Frontend die Branch-Mappings.
    /// </summary>
    [HttpGet("test-connection")]
    public async Task<IActionResult> TestConnection(CancellationToken ct)
    {
        if (!_client.IsConfigured)
            return StatusCode(503, new
            {
                error   = "EAW_NOT_CONFIGURED",
                message = "easy@work nicht konfiguriert. Bitte EASYATWORK_CLIENT_ID, "
                        + "EASYATWORK_CLIENT_SECRET und EASYATWORK_BASE_URL setzen (ENV "
                        + "oder appsettings.json Section 'EasyAtWork')."
            });

        try
        {
            var customers = await _client.GetCustomersAsync(ct);
            return Ok(new
            {
                ok       = true,
                baseUrl  = _client.BaseUrl,
                customers = customers.Data.Select(c => new
                {
                    id        = c.Id,
                    number    = c.Number,
                    name      = c.Name,
                    updatedAt = c.UpdatedAt
                }).ToList(),
                total    = customers.Total ?? customers.Data.Count
            });
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "easy@work test-connection fehlgeschlagen");
            return StatusCode(502, new
            {
                error   = "EAW_REQUEST_FAILED",
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "easy@work test-connection: unerwarteter Fehler");
            return StatusCode(500, new { error = "EAW_UNKNOWN", message = ex.Message });
        }
    }

    // ───────────────────── Branch-Mappings ──────────────────────────

    public record BranchMappingDto(
        int     Id,
        int     CompanyProfileId,
        string? CompanyProfileName,
        string? RestaurantCode,
        int     EasyAtWorkCustomerId,
        string? EasyAtWorkCustomerNumber,
        string? EasyAtWorkCustomerName,
        bool    AutoSyncEnabled,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    public record UpsertMappingDto(
        int     CompanyProfileId,
        int     EasyAtWorkCustomerId,
        string? EasyAtWorkCustomerNumber,
        string? EasyAtWorkCustomerName);

    /// <summary>Liste aller Filial-Mappings.</summary>
    [HttpGet("mappings")]
    public async Task<IActionResult> GetMappings(CancellationToken ct)
    {
        var rows = await (
            from m in _db.EasyAtWorkBranchMappings.AsNoTracking()
            join cp in _db.CompanyProfiles.AsNoTracking() on m.CompanyProfileId equals cp.Id
            orderby (cp.BranchName ?? cp.CompanyName)
            select new BranchMappingDto(
                m.Id,
                m.CompanyProfileId,
                cp.BranchName ?? cp.CompanyName,
                cp.RestaurantCode,
                m.EasyAtWorkCustomerId,
                m.EasyAtWorkCustomerNumber,
                m.EasyAtWorkCustomerName,
                m.AutoSyncEnabled,
                m.CreatedAt,
                m.UpdatedAt)
        ).ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>Mapping einer einzelnen Filiale (für den Filial-Einstellungen-Tab).</summary>
    [HttpGet("mappings/by-branch/{companyProfileId:int}")]
    public async Task<IActionResult> GetMappingByBranch(int companyProfileId, CancellationToken ct)
    {
        var dto = await (
            from m in _db.EasyAtWorkBranchMappings.AsNoTracking()
            join cp in _db.CompanyProfiles.AsNoTracking() on m.CompanyProfileId equals cp.Id
            where m.CompanyProfileId == companyProfileId
            select new BranchMappingDto(
                m.Id, m.CompanyProfileId, cp.BranchName ?? cp.CompanyName, cp.RestaurantCode,
                m.EasyAtWorkCustomerId, m.EasyAtWorkCustomerNumber, m.EasyAtWorkCustomerName,
                m.AutoSyncEnabled, m.CreatedAt, m.UpdatedAt)
        ).FirstOrDefaultAsync(ct);
        if (dto == null) return NotFound(new { error = "NOT_MAPPED" });

        // Sync-Status (Resource TIMEPUNCH) mitliefern — für die Anzeige beim
        // Auto-Sync-Schalter (Walter-Vorgabe 19.06.2026).
        var st = await _db.EasyAtWorkSyncStates.AsNoTracking()
            .Where(s => s.CompanyProfileId == companyProfileId && s.Resource == "TIMEPUNCH")
            .Select(s => new { s.LastSyncAt, s.LastSeenUpdatedAt, s.LastRowCount, s.LastError })
            .FirstOrDefaultAsync(ct);

        return Ok(new
        {
            dto.Id, dto.CompanyProfileId, dto.CompanyProfileName, dto.RestaurantCode,
            dto.EasyAtWorkCustomerId, dto.EasyAtWorkCustomerNumber, dto.EasyAtWorkCustomerName,
            dto.AutoSyncEnabled, dto.CreatedAt, dto.UpdatedAt,
            syncState = st == null ? null : new
            {
                lastSyncAt        = st.LastSyncAt,
                lastSeenUpdatedAt = st.LastSeenUpdatedAt,
                lastRowCount      = st.LastRowCount,
                lastError         = st.LastError,
            }
        });
    }

    public record AutoSyncToggleDto(bool Enabled);

    /// <summary>Auto-Sync für eine Filiale ein-/ausschalten (Filial-Einstellungen-Tab).</summary>
    [HttpPatch("mappings/{companyProfileId:int}/auto-sync")]
    public async Task<IActionResult> SetAutoSync(int companyProfileId, [FromBody] AutoSyncToggleDto dto, CancellationToken ct)
    {
        var row = await _db.EasyAtWorkBranchMappings
            .FirstOrDefaultAsync(m => m.CompanyProfileId == companyProfileId, ct);
        if (row == null) return NotFound(new { error = "NOT_MAPPED", message = "Filiale ist nicht mit easy@work verknüpft." });
        row.AutoSyncEnabled = dto.Enabled;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true, autoSyncEnabled = row.AutoSyncEnabled });
    }

    // ──────────── Auto-Sync-Protokoll (Admin-Ansicht) ────────────────
    public record SyncLogDto(
        int Id, int CompanyProfileId, string? CompanyProfileName, DateTime RunAt,
        string Status, DateOnly? PeriodFrom, DateOnly? PeriodTo, bool UsedUpdatesFeed,
        int Inserted, int Updated, int Deleted, int LockedSkipped, int Skipped,
        int MissingCount, string? Message, bool HasDetail);

    /// <summary>Protokoll des automatischen Sync (neueste zuerst), optional pro Filiale.</summary>
    [HttpGet("sync-log")]
    public async Task<IActionResult> GetSyncLog(
        [FromQuery] int? branchId, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (limit <= 0 || limit > 500) limit = 100;

        var logs = await _db.EasyAtWorkSyncLogs.AsNoTracking()
            .Where(l => !branchId.HasValue || l.CompanyProfileId == branchId.Value)
            .OrderByDescending(l => l.RunAt)
            .Take(limit)
            .ToListAsync(ct);

        var cpIds = logs.Select(l => l.CompanyProfileId).Distinct().ToList();
        var names = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => cpIds.Contains(c.Id))
            .Select(c => new { c.Id, Name = c.BranchName ?? c.CompanyName })
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var rows = logs.Select(l => new SyncLogDto(
            l.Id, l.CompanyProfileId,
            names.TryGetValue(l.CompanyProfileId, out var n) ? n : null,
            l.RunAt, l.Status, l.PeriodFrom, l.PeriodTo, l.UsedUpdatesFeed,
            l.Inserted, l.Updated, l.Deleted, l.LockedSkipped, l.Skipped,
            l.MissingCount, l.Message, !string.IsNullOrEmpty(l.DetailJson))).ToList();
        return Ok(rows);
    }

    /// <summary>Detail der echten Änderungen eines Sync-Laufs (Variante A) —
    /// reichert die gespeicherten Zeilen mit MA-Name/-Nummer an.</summary>
    [HttpGet("sync-log/{id:int}/detail")]
    public async Task<IActionResult> GetSyncLogDetail(int id, CancellationToken ct = default)
    {
        var log = await _db.EasyAtWorkSyncLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (log == null) return NotFound();
        if (string.IsNullOrEmpty(log.DetailJson))
            return Ok(new { totalChanges = 0, capped = false, changes = Array.Empty<object>() });

        JsonElement root;
        try { root = JsonDocument.Parse(log.DetailJson).RootElement; }
        catch { return Ok(new { totalChanges = 0, capped = false, changes = Array.Empty<object>() }); }

        var rawChanges = root.TryGetProperty("changes", out var ch) && ch.ValueKind == JsonValueKind.Array
            ? ch.EnumerateArray().ToList() : new List<JsonElement>();
        var empIds = rawChanges
            .Where(c => c.TryGetProperty("empId", out _))
            .Select(c => c.GetProperty("empId").GetInt32()).Distinct().ToList();
        var emps = await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber })
            .ToDictionaryAsync(e => e.Id, ct);

        decimal? Dec(JsonElement c, string k) => c.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : (decimal?)null;
        var changes = rawChanges.Select(c =>
        {
            int eid = c.GetProperty("empId").GetInt32();
            emps.TryGetValue(eid, out var e);
            return new {
                employeeId = eid,
                name       = e != null ? $"{e.FirstName} {e.LastName}".Trim() : $"MA-{eid}",
                number     = e?.EmployeeNumber,
                date       = c.TryGetProperty("date", out var d) ? d.GetString() : null,
                action     = c.TryGetProperty("action", out var a) ? a.GetString() : null,
                oldTotal   = Dec(c, "oldTotal"), newTotal = Dec(c, "newTotal"),
                oldNight   = Dec(c, "oldNight"), newNight = Dec(c, "newNight")
            };
        })
        .OrderBy(x => x.name).ThenBy(x => x.date).ToList();

        return Ok(new {
            totalChanges = root.TryGetProperty("totalChanges", out var tc) ? tc.GetInt32() : changes.Count,
            capped       = root.TryGetProperty("capped", out var cp) && cp.GetBoolean(),
            runAt        = log.RunAt,
            changes
        });
    }

    /// <summary>Mapping neu anlegen oder vorhandenes updaten (per CompanyProfileId).</summary>
    [HttpPost("mappings")]
    public async Task<IActionResult> UpsertMapping([FromBody] UpsertMappingDto dto, CancellationToken ct)
    {
        if (dto.CompanyProfileId <= 0)
            return BadRequest(new { error = "BAD_COMPANY_PROFILE" });
        if (dto.EasyAtWorkCustomerId <= 0)
            return BadRequest(new { error = "BAD_CUSTOMER_ID" });

        var cpExists = await _db.CompanyProfiles.AnyAsync(c => c.Id == dto.CompanyProfileId, ct);
        if (!cpExists) return NotFound(new { error = "COMPANY_PROFILE_NOT_FOUND" });

        // Duplikat-Schutz: Customer-ID darf nur einmal vergeben sein.
        var dup = await _db.EasyAtWorkBranchMappings
            .FirstOrDefaultAsync(m => m.EasyAtWorkCustomerId == dto.EasyAtWorkCustomerId
                                   && m.CompanyProfileId != dto.CompanyProfileId, ct);
        if (dup != null)
            return Conflict(new
            {
                error   = "EAW_CUSTOMER_ALREADY_MAPPED",
                message = $"Customer-ID {dto.EasyAtWorkCustomerId} ist bereits Filiale {dup.CompanyProfileId} zugeordnet."
            });

        var existing = await _db.EasyAtWorkBranchMappings
            .FirstOrDefaultAsync(m => m.CompanyProfileId == dto.CompanyProfileId, ct);

        if (existing == null)
        {
            existing = new EasyAtWorkBranchMapping
            {
                CompanyProfileId          = dto.CompanyProfileId,
                EasyAtWorkCustomerId      = dto.EasyAtWorkCustomerId,
                EasyAtWorkCustomerNumber  = dto.EasyAtWorkCustomerNumber,
                EasyAtWorkCustomerName    = dto.EasyAtWorkCustomerName,
                CreatedAt                 = DateTime.UtcNow,
                UpdatedAt                 = DateTime.UtcNow,
            };
            _db.EasyAtWorkBranchMappings.Add(existing);
        }
        else
        {
            existing.EasyAtWorkCustomerId      = dto.EasyAtWorkCustomerId;
            existing.EasyAtWorkCustomerNumber  = dto.EasyAtWorkCustomerNumber;
            existing.EasyAtWorkCustomerName    = dto.EasyAtWorkCustomerName;
            existing.UpdatedAt                 = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = existing.Id });
    }

    /// <summary>Mapping entfernen (per CompanyProfileId).</summary>
    [HttpDelete("mappings/{companyProfileId:int}")]
    public async Task<IActionResult> DeleteMapping(int companyProfileId, CancellationToken ct)
    {
        var row = await _db.EasyAtWorkBranchMappings
            .FirstOrDefaultAsync(m => m.CompanyProfileId == companyProfileId, ct);
        if (row == null) return NotFound();
        _db.EasyAtWorkBranchMappings.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ────────────────── Stempelzeit-Sync (Phase 2) ──────────────────

    public record SyncRequestDto(int CompanyProfileId, DateOnly From, DateOnly To, List<int>? SkipEawEmployeeIds = null, DateOnly? EmployeeCutoffOverride = null, bool? IgnoreMissing = null);

    /// <summary>Dry-Run: zeigt, was importiert/dedupliziert/unmatched wäre. Schreibt nichts.</summary>
    [Authorize(Roles = "admin")]
    [HttpPost("sync/timepunches/preview")]
    public async Task<IActionResult> SyncTimepunchesPreview([FromBody] SyncRequestDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var res = await _tpSync.PreviewAsync(new Services.EasyAtWork.EasyAtWorkTimepunchSyncService.SyncRequest
        {
            CompanyProfileId = dto.CompanyProfileId,
            From = dto.From,
            To = dto.To,
            SkipEawEmployeeIds = dto.SkipEawEmployeeIds ?? new(),
            EmployeeCutoffOverride = dto.EmployeeCutoffOverride
        }, ct);
        return Ok(res);
    }

    /// <summary>
    /// Direkt-Nachschlag eines easy@work-MA per ID (Walter-Vorgabe 20.06.2026) —
    /// auch wenn er nicht mehr in der MA-Liste steht. Liefert Name/Nummer (falls
    /// die API ihn noch hergibt) und schlägt — bei vorhandener Nummer — den
    /// passenden Cowork-MA zum Zuordnen vor.
    /// </summary>
    [HttpGet("employee-lookup")]
    public async Task<IActionResult> EmployeeLookup([FromQuery] int companyProfileId, [FromQuery] int eawId, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var mapping = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyProfileId == companyProfileId, ct);
        if (mapping == null) return NotFound(new { error = "NO_MAPPING" });

        var emp = await _client.GetEmployeeByIdAsync(mapping.EasyAtWorkCustomerId, eawId, ct);
        if (emp == null)
            return Ok(new { found = false, eawId });

        var name = $"{emp.FirstName} {emp.LastName}".Trim();
        int? coworkId = null; string? coworkName = null;
        if (!string.IsNullOrWhiteSpace(emp.Number))
        {
            var nr = emp.Number!.Trim();
            var co = await _db.Employees.AsNoTracking()
                .Where(e => e.EmployeeNumber == nr)
                .Select(e => new { e.Id, e.FirstName, e.LastName }).FirstOrDefaultAsync(ct);
            if (co != null) { coworkId = co.Id; coworkName = $"{co.FirstName} {co.LastName}".Trim(); }
        }
        return Ok(new
        {
            found = true, eawId, number = emp.Number, name,
            coworkEmployeeId = coworkId, coworkName
        });
    }

    /// <summary>Commit: schreibt die NEW-Zeilen in employee_time_entry.</summary>
    [Authorize(Roles = "admin")]
    [HttpPost("sync/timepunches/commit")]
    public async Task<IActionResult> SyncTimepunchesCommit([FromBody] SyncRequestDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });

        // Import-Sperre läuft seit Walter-Vorgabe 20.06.2026 PRO PERIODE im Sync-
        // Service (IsImportable gegen ABGESCHLOSSENE Lohnperioden) statt über den
        // monotonen LohnEditLockService — sonst hätte eine laufende 2026-Periode
        // den ganzen historischen 2025-Import gesperrt. Davor/danach (inkl. 2025)
        // ist erlaubt, nur innerhalb abgeschlossener Perioden nicht. Der hier
        // übergebene Wert wird vom Service ignoriert (per-Periode-Logik gilt).
        DateOnly? firstAllowed = null;
        var res = await _tpSync.CommitAsync(new Services.EasyAtWork.EasyAtWorkTimepunchSyncService.SyncRequest
        {
            CompanyProfileId = dto.CompanyProfileId,
            From = dto.From,
            To = dto.To,
            SkipEawEmployeeIds = dto.SkipEawEmployeeIds ?? new(),
            EmployeeCutoffOverride = dto.EmployeeCutoffOverride,
            IgnoreMissing = dto.IgnoreMissing ?? false
        }, firstAllowed, progress: null, ct);
        return Ok(res);
    }

    /// <summary>
    /// Direkt-Import als Hintergrund-Job mit Fortschritt (Walter-Vorgabe
    /// 09.07.2026): kein Vorschau-Pflichtschritt mehr — der Commit lädt selbst,
    /// blockiert bei fehlenden/mehrdeutigen MA und überspringt geschlossene
    /// Perioden. Der Browser pollt den Job-Status (gleicher Endpoint wie der
    /// MA-Import: GET sync/employees/job/{jobId}).
    /// </summary>
    [Authorize(Roles = "admin")]
    [HttpPost("sync/timepunches/commit-async")]
    public IActionResult SyncTimepunchesCommitAsync([FromBody] SyncRequestDto dto)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });

        var job = _importJobs.Create();
        var jobId = job.Id;
        var req = new Services.EasyAtWork.EasyAtWorkTimepunchSyncService.SyncRequest
        {
            CompanyProfileId = dto.CompanyProfileId,
            From = dto.From,
            To = dto.To,
            SkipEawEmployeeIds = dto.SkipEawEmployeeIds ?? new(),
            EmployeeCutoffOverride = dto.EmployeeCutoffOverride,
            IgnoreMissing = dto.IgnoreMissing ?? false
        };

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider
                .GetRequiredService<Services.EasyAtWork.EasyAtWorkTimepunchSyncService>();
            try
            {
                var res = await svc.CommitAsync(req, firstAllowed: null,
                    progress: (done, total, phase) => _importJobs.Progress(jobId, done, total, phase),
                    ct: CancellationToken.None);
                _importJobs.Complete(jobId, res);
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                    msg += "  →  " + inner.Message;
                _importJobs.Fail(jobId, msg);
                _log.LogError(ex, "Asynchroner Stempelzeiten-Import (Job {JobId}) fehlgeschlagen.", jobId);
            }
        });

        return Ok(new { jobId });
    }

    // Liefert die gemappten Filialen (Id + Name) — fürs Frontend, das den
    // historischen Batch-Import Filiale-für-Filiale + Fenster-für-Fenster fährt
    // (kurze Requests, kein Gateway-Timeout). Walter 21.06.2026.
    [HttpGet("mapped-branches")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> MappedBranches(CancellationToken ct)
    {
        var ids = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .Select(m => m.CompanyProfileId).Distinct().ToListAsync(ct);
        var rows = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { id = c.Id, name = string.IsNullOrWhiteSpace(c.BranchName) ? c.CompanyName : c.BranchName })
            .ToListAsync(ct);
        return Ok(rows.OrderBy(r => r.name));
    }

    // ────────────────── Mitarbeiter-Sync (Phase 3.1) ─────────────────

    // OnlyActive ersetzt das frühere „Austritt nach"-Datumsfeld (Walter 19.06.2026):
    // true = nur aktive, false/null = alle (inkl. ausgetretene, ohne Pre-2025).
    public record EmpSyncRequestDto(int CompanyProfileId, DateOnly? ActiveAt, DateOnly? ExitedAfter, bool? IncludeAllInactive, bool? OnlyActive, List<string>? SelectedNumbers, bool? SkipDetailCalls = null);
    public record SingleCoworkEmployeeSyncDto(int? CompanyProfileId = null);

    /// <summary>Dry-Run für MA-Stammdaten — zeigt NEW/UPDATE/UNCHANGED/CONFLICT.
    /// Onboarding-Werkzeug (Walter-Vorgabe 08.07.2026): nur Admin — der laufende
    /// Betrieb nutzt den Einzelimport im Mitarbeitermodul (Neuzugang-Controller).</summary>
    [HttpPost("sync/employees/preview")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> SyncEmployeesPreview([FromBody] EmpSyncRequestDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var res = await _empSync.PreviewAsync(new Services.EasyAtWork.EasyAtWorkEmployeeSyncService.SyncRequest
        {
            CompanyProfileId = dto.CompanyProfileId,
            OnlyActive = dto.OnlyActive ?? false,
            SkipDetailCalls = dto.SkipDetailCalls ?? false,
        }, ct);
        return Ok(res);
    }

    /// <summary>Commit: INSERTet NEW-MA + UPDATEt ausgewählte UPDATE-MA in employee.
    /// Onboarding-Werkzeug — nur Admin (Walter-Vorgabe 08.07.2026).</summary>
    [HttpPost("sync/employees/commit")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> SyncEmployeesCommit([FromBody] EmpSyncRequestDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var res = await _empSync.CommitAsync(new Services.EasyAtWork.EasyAtWorkEmployeeSyncService.SyncRequest
        {
            CompanyProfileId = dto.CompanyProfileId,
            OnlyActive = dto.OnlyActive ?? false,
            SelectedNumbers = dto.SelectedNumbers,
            SkipDetailCalls = dto.SkipDetailCalls ?? false,
        }, ct: ct);
        return Ok(res);
    }

    /// <summary>
    /// Asynchroner Filial-Import (Walter-Vorgabe 29.06.2026): Der Commit läuft als
    /// Hintergrund-Job (eigener DI-Scope, browser-unabhängig) — gibt sofort eine
    /// Job-ID zurück. Der Browser pollt <c>sync/employees/job/{jobId}</c> für den
    /// Fortschritt und das Endergebnis. So gibt es kein Request-Timeout mehr,
    /// auch wenn easy@work mehrere Minuten braucht.
    /// </summary>
    [HttpPost("sync/employees/commit-async")]
    [Authorize(Roles = "admin")]
    public IActionResult SyncEmployeesCommitAsync([FromBody] EmpSyncRequestDto dto)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });

        var job = _importJobs.Create();
        var jobId = job.Id;
        var req = new Services.EasyAtWork.EasyAtWorkEmployeeSyncService.SyncRequest
        {
            CompanyProfileId = dto.CompanyProfileId,
            OnlyActive       = dto.OnlyActive ?? false,
            SelectedNumbers  = dto.SelectedNumbers,
            SkipDetailCalls  = dto.SkipDetailCalls ?? false,
        };

        // Bewusst NICHT awaiten: läuft im Hintergrund weiter, auch wenn der
        // Browser die Antwort längst hat. Eigener Scope, da der Request-Scope
        // (inkl. DbContext) nach der Antwort entsorgt wird.
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider
                .GetRequiredService<Services.EasyAtWork.EasyAtWorkEmployeeSyncService>();
            try
            {
                var res = await svc.CommitAsync(req,
                    progress: (done, total, phase) => _importJobs.Progress(jobId, done, total, phase),
                    ct: CancellationToken.None);
                _importJobs.Complete(jobId, new
                {
                    res.CountTotal, res.CountNew, res.CountUpdate, res.CountUnchanged,
                    res.CountConflict, res.CountInserted, res.CountUpdated, res.CountExisting,
                    notes = res.Notes, skippedContracts = res.SkippedContracts,
                    blocked = res.Blocked, numberConflicts = res.NumberConflicts
                });
            }
            catch (Exception ex)
            {
                // Inner-Exceptions mitnehmen — bei DbUpdateException steckt die
                // echte Ursache (Npgsql-Constraint/NULL) erst in der inneren.
                var msg = ex.Message;
                for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                    msg += "  →  " + inner.Message;
                _importJobs.Fail(jobId, msg);
                _log.LogError(ex, "Asynchroner Filial-Import (Job {JobId}) fehlgeschlagen.", jobId);
            }
        });

        return Accepted(new { jobId });
    }

    /// <summary>
    /// On-Demand «Probezeiten nachführen» (Walter 29.06.2026 / 02.08.2026):
    /// fehlende Probezeiten anlegen + an erster Stempelzeit ≥ Eintritt verankern.
    /// Unabhängig vom Stempel-Import.
    /// </summary>
    [HttpPost("probation/anchor")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> AnchorProbation(CancellationToken ct)
    {
        try
        {
            var notes = await _tpSync.RunProbationAnchorAsync(ct);
            return Ok(new { processed = notes.Count, anchored = notes.Count, notes });
        }
        catch (Exception ex)
        {
            // Inner-Exceptions mitnehmen — bei einer fehlenden Tabelle/Spalte steckt
            // die echte Ursache erst in der inneren Npgsql-Exception.
            var msg = ex.Message;
            for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                msg += "  →  " + inner.Message;
            _log.LogError(ex, "Probezeit-Anker fehlgeschlagen.");
            return StatusCode(500, new { error = "PROBATION_ANCHOR_FAILED", message = msg });
        }
    }

    /// <summary>Status/Fortschritt/Ergebnis eines asynchronen Filial-Imports.</summary>
    [HttpGet("sync/employees/job/{jobId}")]
    public IActionResult GetImportJob(string jobId)
    {
        var job = _importJobs.Get(jobId);
        if (job == null) return NotFound(new { error = "JOB_NOT_FOUND" });
        return Ok(new { job.Id, job.Status, job.Phase, job.Done, job.Total, job.Error, result = job.Result });
    }

    /// <summary>
    /// Massen-Korrektur für Altbestände: Früher wurde teils easy@work user_id
    /// in employee.easyatwork_employee_id gespeichert. Für die API-Endpunkte ist
    /// aber employee.id nötig. Dieser Lauf liest pro gemapptem Customer die
    /// easy@work-MA-Liste und korrigiert bestehende Cowork-MA.
    /// </summary>
    [HttpPost("sync/employees/repair-ids")]
    public async Task<IActionResult> RepairStoredEmployeeIds(CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });

        var mappings = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .OrderBy(m => m.CompanyProfileId)
            .ToListAsync(ct);
        if (mappings.Count == 0)
            return BadRequest(new { error = "NO_MAPPING", message = "Keine easy@work-Filial-Mappings vorhanden." });

        var coworkers = await _db.Employees
            .Where(e => !e.IsHidden)
            .ToListAsync(ct);
        // WICHTIG (Walter-Vorgabe 05.07.2026): Die EINZIGE eindeutige Kennung ist die
        // easy@work employee.id. Die user_id (App-Login) wird NICHT mehr zum Matchen
        // verwendet — sie ist nicht personen-eindeutig und hat zwei VERSCHIEDENE Personen
        // verknüpft (z.B. easy #4469/user_id 29300 zog Oktavia mit, deren gespeicherte
        // «eaw-ID» 29300 in Wahrheit eine user_id war). Gematcht wird ausschliesslich über
        // die Personalnummer; ein gespeicherter Wert, der KEINE gültige easy@work-
        // employee.id ist (also z.B. eine alte user_id), gilt als «stale» und wird über
        // die Nummer eindeutig korrigiert.
        var byNumber = coworkers
            .Where(e => !string.IsNullOrWhiteSpace(e.EmployeeNumber))
            .GroupBy(e => e.EmployeeNumber.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var repaired = new List<object>();
        var conflicts = new List<object>();
        var perBranch = new List<object>();
        var touched = new HashSet<int>();
        var totalEawRows = 0;

        // Pass 1: alle easy@work-Zeilen laden und die Menge ALLER gültigen employee.id
        // bilden (über alle Filialen) — damit «stale» gespeicherte Werte erkennbar sind.
        var branchRows = new List<(EasyAtWorkBranchMapping mapping, List<EawEmployee> rows)>();
        var validEawIds = new HashSet<int>();
        foreach (var mapping in mappings)
        {
            List<EawEmployee> rows;
            try
            {
                rows = await _client.GetAllEmployeesIncludingInactiveAsync(mapping.EasyAtWorkCustomerId, ct);
            }
            catch (Exception ex)
            {
                conflicts.Add(new { customerId = mapping.EasyAtWorkCustomerId, mapping.CompanyProfileId, error = ex.Message });
                continue;
            }
            branchRows.Add((mapping, rows));
            totalEawRows += rows.Count;
            foreach (var r in rows) validEawIds.Add(r.Id);
        }

        // Pass 2: pro Filiale über die Personalnummer zuordnen und die gespeicherte id
        // korrigieren.
        foreach (var (mapping, rows) in branchRows)
        {
            var branchFixed = 0;
            foreach (var eaw in rows)
            {
                if (string.IsNullOrWhiteSpace(eaw.Number)) continue;
                if (!byNumber.TryGetValue(eaw.Number.Trim(), out var byNum)) continue;

                // Nur MA mit dieser Nummer, deren gespeicherte id fehlt, bereits stimmt
                // ODER stale ist (keine gültige easy@work-employee.id). Korrekte, aber
                // abweichende ids (echte andere Person mit valider id) bleiben unberührt.
                var candidates = byNum.Where(e =>
                    !e.EasyAtWorkEmployeeId.HasValue
                    || e.EasyAtWorkEmployeeId == eaw.Id
                    || !validEawIds.Contains(e.EasyAtWorkEmployeeId.Value)).ToList();

                if (candidates.Count == 0) continue;
                if (candidates.Count > 1)
                {
                    conflicts.Add(new
                    {
                        customerId = mapping.EasyAtWorkCustomerId,
                        eawEmployeeId = eaw.Id,
                        eawUserId = eaw.UserId,
                        number = eaw.Number,
                        matches = candidates.Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.EasyAtWorkEmployeeId }).ToList()
                    });
                    continue;
                }

                var emp = candidates[0];
                if (touched.Contains(emp.Id) && emp.EasyAtWorkEmployeeId != eaw.Id)
                {
                    conflicts.Add(new { employeeId = emp.Id, emp.EmployeeNumber, current = emp.EasyAtWorkEmployeeId, proposed = eaw.Id, customerId = mapping.EasyAtWorkCustomerId });
                    continue;
                }

                if (emp.EasyAtWorkEmployeeId == eaw.Id) continue;
                var old = emp.EasyAtWorkEmployeeId;
                emp.EasyAtWorkEmployeeId = eaw.Id;
                touched.Add(emp.Id);
                branchFixed++;
                repaired.Add(new
                {
                    employeeId = emp.Id,
                    emp.EmployeeNumber,
                    name = $"{emp.FirstName} {emp.LastName}".Trim(),
                    oldEasyAtWorkId = old,
                    newEasyAtWorkId = eaw.Id,
                    eawUserId = eaw.UserId,
                    customerId = mapping.EasyAtWorkCustomerId
                });
            }

            perBranch.Add(new { mapping.CompanyProfileId, mapping.EasyAtWorkCustomerId, scanned = rows.Count, repaired = branchFixed });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new
        {
            scannedCowork = coworkers.Count,
            scannedEasyAtWork = totalEawRows,
            repaired = repaired.Count,
            conflicts = conflicts.Count,
            perBranch,
            rows = repaired,
            conflictRows = conflicts
        });
    }

    /// <summary>
    /// Aktualisiert genau EINEN bestehenden Cowork-MA aus easy@work.
    /// Wird aus der Mitarbeiter-Maske über den Button „easy@work Abgleich"
    /// aufgerufen. Schreibt erst nach vollständiger Validierung.
    /// </summary>
    // Walter-Vorgabe 13.07.2026: der Einzel-Abgleich steht auch dem GF (user)
    // + Buchhaltung offen — sie pflegen die MA ihrer Filialen. Filial-Guard:
    // user/buchhaltung nur mit companyProfileId aus ihrer UserBranchAccess-
    // Liste (analog Neuzugangs-Import; buchhaltung ZUERST, wegen des
    // superuser-Doppel-Claims).
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    [HttpPost("employees/cowork/{employeeId:int}/sync")]
    public async Task<IActionResult> SyncSingleCoworkEmployee(int employeeId, [FromBody] SingleCoworkEmployeeSyncDto? dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });

        var roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToHashSet();
        var istGlobal = !roles.Contains("buchhaltung") && (roles.Contains("admin") || roles.Contains("superuser"));
        if (!istGlobal)
        {
            if (dto?.CompanyProfileId is not int cpid)
                return StatusCode(403, new { error = "Bitte zuerst oben eine Filiale w\u00e4hlen." });
            var uidClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(uidClaim, out var uid)
                || !await _db.UserBranchAccesses.AnyAsync(a => a.UserId == uid && a.CompanyProfileId == cpid))
                return StatusCode(403, new { error = "Kein Zugriff auf diese Filiale." });
        }
        var res = await _empSync.SyncSingleCoworkEmployeeAsync(employeeId, dto?.CompanyProfileId, ct);
        if (!res.Success && res.Errors.Count > 0)
            return BadRequest(new { error = "EAW_SINGLE_SYNC_INVALID", message = string.Join("\n", res.Errors), res.Errors, res.Notes });
        return Ok(res);
    }

    private sealed record DupEmpRow(int Id, string? Number, string? First, string? Last,
        int? EawId, bool Excluded, bool Active, DateTime? Dob);

    /// <summary>
    /// Findet Personen mit MEHR als einem Lohn-MA (IsPayrollExcluded=false) —
    /// genau die Fälle, die den easy@work-Sync blockieren („mehrere Lohn-MA für
    /// eine Person"). Gruppiert nach easy@work-ID, Personalnummer und
    /// Name+Geburtstag. Reine DB-Auswertung, kein easy@work-API-Aufruf.
    /// </summary>
    [HttpGet("duplicate-payroll-employees")]
    public async Task<IActionResult> GetDuplicatePayrollEmployees(CancellationToken ct)
    {
        var emps = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden)
            .Select(e => new DupEmpRow(e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
                e.EasyAtWorkEmployeeId, e.IsPayrollExcluded, e.IsActive, e.DateOfBirth))
            .ToListAsync(ct);

        // Filiale je MA aus der jüngsten Anstellung (Employee selbst hat keine).
        var empBranch = (await _db.Employments.AsNoTracking()
                .Select(em => new { em.EmployeeId, em.CompanyProfileId, em.ContractStartDate })
                .ToListAsync(ct))
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key,
                g => g.OrderByDescending(x => x.ContractStartDate).First().CompanyProfileId);
        var branchNames = await _db.CompanyProfiles.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => (string?)(c.BranchName ?? c.CompanyName), ct);

        var groups = new List<object>();
        var emitted = new HashSet<string>();

        void Emit(string keyType, string keyValue, List<DupEmpRow> members)
        {
            var distinct = members.GroupBy(m => m.Id).Select(g => g.First()).ToList();
            var payrollCount = distinct.Count(m => !m.Excluded);
            if (payrollCount < 2) return;   // nur echte Mehrdeutigkeit (≥2 Lohn-MA)
            var sig = string.Join("-", distinct.Select(m => m.Id).OrderBy(x => x));
            if (!emitted.Add(sig)) return;  // dieselbe Gruppe nicht doppelt melden
            groups.Add(new
            {
                keyType,
                keyValue,
                payrollCount,
                members = distinct
                    .OrderByDescending(m => !m.Excluded).ThenBy(m => m.Id)
                    .Select(m => new
                    {
                        id = m.Id,
                        number = m.Number,
                        name = $"{m.First} {m.Last}".Trim(),
                        easyAtWorkEmployeeId = m.EawId,
                        isPayrollExcluded = m.Excluded,
                        isActive = m.Active,
                        branch = (empBranch.TryGetValue(m.Id, out var cp) && cp.HasValue
                                  && branchNames.TryGetValue(cp.Value, out var bn)) ? bn : null
                    })
            });
        }

        foreach (var grp in emps.Where(e => e.EawId.HasValue).GroupBy(e => e.EawId!.Value))
            Emit("easy@work-ID", "#" + grp.Key, grp.ToList());
        foreach (var grp in emps.Where(e => !string.IsNullOrWhiteSpace(e.Number))
                                .GroupBy(e => e.Number!.Trim(), StringComparer.OrdinalIgnoreCase))
            Emit("Personalnummer", grp.Key, grp.ToList());
        foreach (var grp in emps.Where(e => !string.IsNullOrWhiteSpace(e.First) || !string.IsNullOrWhiteSpace(e.Last))
                                .GroupBy(e => ($"{e.First} {e.Last}".Trim().ToLowerInvariant())
                                              + "|" + (e.Dob?.ToString("yyyy-MM-dd") ?? "")))
            Emit("Name + Geburtstag", $"{grp.First().First} {grp.First().Last}".Trim(), grp.ToList());

        return Ok(new { count = groups.Count, groups });
    }

    /// <summary>
    /// Aktive easy@work-Mitarbeitende einer Filiale (read-only) für den neuen
    /// laufenden API-Abgleich. Sortierung nach Vorname, Tie-Break Nachname
    /// (Walter-Konvention für alle MA-Listen). Schreibt nichts.
    /// </summary>
    [HttpGet("employees/active")]
    public async Task<IActionResult> GetActiveEasyAtWorkEmployees([FromQuery] int companyProfileId, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var mapping = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyProfileId == companyProfileId, ct);
        if (mapping == null)
            return NotFound(new { error = "NOT_MAPPED", message = "Filiale ist nicht mit easy@work verknüpft." });

        var activeAt = DateOnly.FromDateTime(DateTime.Today);
        var emps = await _client.GetAllEmployeesActiveAtAsync(mapping.EasyAtWorkCustomerId, activeAt, ct);
        var rows = emps
            .OrderBy(e => e.FirstName ?? "", StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.LastName ?? "", StringComparer.CurrentCultureIgnoreCase)
            .Select(e => new
            {
                easyAtWorkEmployeeId = e.Id,
                easyAtWorkUserId = e.UserId,
                e.Number,
                e.FirstName,
                e.LastName,
                name = $"{e.FirstName} {e.LastName}".Trim(),
                e.Email,
                e.Phone,
                from = e.From,
                to = e.To,
                updatedAt = e.UpdatedAt,
                mapping.CompanyProfileId,
                mapping.EasyAtWorkCustomerId
            })
            .ToList();

        return Ok(new
        {
            companyProfileId = mapping.CompanyProfileId,
            customerId = mapping.EasyAtWorkCustomerId,
            activeAt,
            count = rows.Count,
            employees = rows
        });
    }

    /// <summary>
    /// Liefert für EINEN easy@work-MA eine CSV-kompatible Import-Zeile, damit der
    /// bestehende CSV-Importer unverändert seine Vorschau-/Vertragslogik nutzen kann.
    /// Schreibt nichts.
    /// </summary>
    [HttpGet("employees/{companyProfileId:int}/{eawEmployeeId:int}/import-row")]
    public async Task<IActionResult> GetEmployeeImportRow(int companyProfileId, int eawEmployeeId, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var mapping = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyProfileId == companyProfileId, ct);
        if (mapping == null) return NotFound(new { error = "NOT_MAPPED", message = "Filiale ist nicht mit easy@work verknüpft." });

        var emp = await _client.GetEmployeeByIdAsync(mapping.EasyAtWorkCustomerId, eawEmployeeId, ct);
        if (emp == null)
        {
            // Manche easy@work-Listen liefern je nach Endpoint Resource-ID vs.
            // User-ID unterschiedlich. Für die Import-Zeile reicht der Listen-
            // Datensatz; Detail-Calls darunter probieren wir weiter mit Resource-ID.
            var activeAt = DateOnly.FromDateTime(DateTime.Today);
            var allActive = await _client.GetAllEmployeesActiveAtAsync(mapping.EasyAtWorkCustomerId, activeAt, ct);
            emp = allActive.FirstOrDefault(e => e.Id == eawEmployeeId || e.UserId == eawEmployeeId);
        }
        if (emp == null) return NotFound(new { error = "EMP_NOT_FOUND", message = "easy@work-Mitarbeiter nicht gefunden." });

        var resourceId = emp.Id;

        var contracts = (await _client.GetContractsAsync(mapping.EasyAtWorkCustomerId, resourceId, ct))?.Data ?? new();
        try
        {
            var types = await _client.GetContractTypesByIdAsync(mapping.EasyAtWorkCustomerId, ct);
            Services.EasyAtWork.EasyAtWorkEmployeeSyncService.ApplyContractTypeNames(contracts, types);
        }
        catch { /* Fallback: Stunden-Heuristik wenn Katalog fehlt */ }
        var rates     = (await _client.GetPayRatesAsync(mapping.EasyAtWorkCustomerId, resourceId, ct))?.Data ?? new();
        var positions = (await _client.GetPositionsAsync(mapping.EasyAtWorkCustomerId, resourceId, ct))?.Data ?? new();
        var props = await _client.GetAllPropertiesAsync(mapping.EasyAtWorkCustomerId, resourceId, ct);
        EawFiscalInfo? fiscal = null;
        try { fiscal = await _client.GetFiscalInfoAsync(mapping.EasyAtWorkCustomerId, resourceId, ct); } catch { }

        static EawContract? CurrentContract(List<EawContract> rows)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            return rows
                .Where(c => c.From.HasValue && c.From.Value <= today && (!c.To.HasValue || c.To.Value >= today))
                .OrderByDescending(c => c.From ?? DateOnly.MinValue)
                .FirstOrDefault()
                ?? rows.OrderByDescending(c => c.From ?? DateOnly.MinValue).FirstOrDefault();
        }
        static EawPayRate? CurrentRate(List<EawPayRate> rows, string type)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            return rows
                .Where(r => (r.Type ?? "").StartsWith(type, StringComparison.OrdinalIgnoreCase)
                         && r.From.HasValue && r.From.Value <= today && (!r.To.HasValue || r.To.Value >= today))
                .OrderByDescending(r => r.From ?? DateOnly.MinValue)
                .FirstOrDefault()
                ?? rows.Where(r => (r.Type ?? "").StartsWith(type, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.From ?? DateOnly.MinValue)
                    .FirstOrDefault();
        }
        static string ContractTypeForImport(Services.EasyAtWork.EasyAtWorkEmployeeSyncService.HistContractInfo info)
            => (info.EmploymentModel ?? "").ToUpperInvariant() switch
            {
                "MTP" => "MTP/TPM",
                "FIX" => "Fix",
                "FIX-M" => "Fix",
                "FLEX" => "Flex",
                "UTP" => "Flex",   // Legacy-Alias (Rename 08.07.2026)
                _ => string.IsNullOrWhiteSpace(info.ContractType) ? "Flex" : info.ContractType!
            };
        static string AnzahlForImport(Services.EasyAtWork.EasyAtWorkEmployeeSyncService.HistContractInfo info, EawContract? c)
        {
            var model = (info.EmploymentModel ?? "").ToUpperInvariant();
            if (model is "FIX" or "FIX-M")
            {
                var pct = info.EmploymentPercentage ?? c?.Percentage ?? c?.Amount;
                return pct.HasValue
                    ? pct.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%"
                    : "";
            }

            var hours = info.GuaranteedHoursPerWeek ?? c?.Amount ?? c?.WeekHours;
            return hours.HasValue
                ? hours.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " Stunden/Woche"
                : "";
        }
        static string? Prop(List<EawProperty> rows, string key)
            => rows.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
        static string? PropAny(List<EawProperty> rows, params string[] keys)
        {
            static string Norm(string? s)
                => new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

            foreach (var key in keys)
            {
                var exact = rows.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
                if (!string.IsNullOrWhiteSpace(exact)) return exact;
            }

            var wanted = keys.Select(Norm).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
            foreach (var p in rows)
            {
                var nk = Norm(p.Key);
                if (wanted.Any(w => nk == w || nk.Contains(w) || w.Contains(nk)))
                    return p.Value;
            }

            return null;
        }
        static string? SalutationFromGender(string? g)
        {
            var s = (g ?? "").Trim().ToLowerInvariant();
            if (s is "female" or "f" or "frau") return "Frau";
            if (s is "male" or "m" or "herr") return "Herr";
            if (s is "divers" or "diverse" or "andere" or "other" or "nonbinary" or "non-binary" or "x" or "d") return null;
            return null;
        }
        // Gemeinsamer Mapper mit dem echten Sync (Walter 01.08.2026: E=Getrennt).
        static string? Marital(string? v)
            => EasyAtWorkEmployeeSyncService.MapMaritalStatus(v);
        static string Fmt(DateOnly? d) => d?.ToString("yyyy-MM-dd") ?? "";

        var today = DateOnly.FromDateTime(DateTime.Today);
        var c = CurrentContract(contracts);
        var monthlyRate = CurrentRate(rates, "month") ?? CurrentRate(rates, "fte");
        var hourlyRate  = CurrentRate(rates, "hour");
        var functionValues = positions
            .Select(p => p.Name?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var propertyFunction = Prop(props, "cf_src_job_code")?.Trim();
        if (functionValues.Count == 0 && !string.IsNullOrWhiteSpace(propertyFunction))
            functionValues.Add(propertyFunction);
        var position = functionValues.Count == 1 ? functionValues[0] : string.Join(", ", functionValues);
        var isKader = position is "ASST_1" or "ASST_2" or "REST_MANAGER" or "SHIFT_LEADER_1_6" or "SHIFT_LEADER_7_PLUS";
        var info = Services.EasyAtWork.EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, rates, today, isKader);
        var payFrequency = info.SalaryType == "monthly" ? "month" : "hour";
        var contractType = ContractTypeForImport(info);
        var anzahl = AnzahlForImport(info, c);
        var contractFrom = info.ContractFrom ?? c?.From ?? emp.From;
        var contractTo   = info.ContractTo ?? c?.To;
        var payRateFrom = info.RateFrom ?? monthlyRate?.From ?? hourlyRate?.From ?? contractFrom;

        var row = new Dictionary<string, string?>
        {
            ["__source"] = "easywork-api",
            ["__functionCount"] = functionValues.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["__functionRaw"] = string.Join(", ", functionValues),
            ["Nummer"] = emp.Number,
            ["Vorname"] = emp.FirstName,
            ["Nachname"] = emp.LastName,
            ["Nickname"] = emp.Nickname,
            ["Kurzname"] = emp.Nickname,
            ["Geschlecht"] = emp.Gender,
            ["Anrede"] = SalutationFromGender(emp.Gender),
            ["Geburtsdatum"] = Fmt(emp.BirthDate),
            ["Adresse"] = emp.Address1,
            ["Adresse 2"] = emp.Address2,
            ["Postleitzahl"] = emp.PostalCode,
            ["Stadt"] = emp.City,
            ["E-Mail"] = emp.Email,
            ["Telefon"] = emp.Phone,
            ["Nationalität"] = emp.Nationality,
            ["Von"] = Fmt(contractFrom),
            ["Bis"] = Fmt(contractTo),
            ["Store number"] = mapping.EasyAtWorkCustomerNumber,
            ["Funktion"] = position,
            ["Funktionen"] = position,
            ["Group memberships"] = "Employee",
            ["Contract type"] = contractType,
            ["Pay frequency"] = payFrequency,
            ["Anzahl"] = anzahl,
            ["Pay rate from"] = Fmt(payRateFrom),
            ["Tarife"] = (info.HourlyRate != null && info.HourlyRate.Value > 1m) ? info.HourlyRate.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "",
            ["Salary (actual)"] = (info.MonthlySalary != null && info.MonthlySalary.Value > 1m) ? info.MonthlySalary.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "",
            ["Qualification CCNT"] = PropAny(props, "cf_swiss_qualification_ll", "swiss_qualification_ll", "cf_qualification_ccnt", "qualification_ccnt", "ccnt_qualification", "Qualification CCNT", "CCNT"),
            ["INTL_BANK_ACCT_NBR1"] = fiscal?.Iban,
            ["AHV"] = Prop(props, "cf_swiss_national_id"),
            ["Marital status"] = Marital(PropAny(props, "cf_marital_status", "marital_status", "Marital status", "Familienstand"))
        };

        return Ok(new
        {
            companyProfileId,
            easyAtWorkEmployeeId = emp.Id,
            row
        });
    }

    public record InitialImportDto(DateOnly? Since, bool? SkipDetailCalls = null);
    public record InitialImportBranchDto(int CompanyProfileId, DateOnly? Since, bool? SkipDetailCalls = null);

    /// <summary>
    /// Tief-Import für EINE Filiale (Walter-Vorgabe 21.06.2026) — damit das
    /// Frontend den Lauf Filiale-für-Filiale fahren und live anzeigen kann, an
    /// welcher es gerade arbeitet (kein „hängt es?"-Eindruck, kein Gateway-
    /// Timeout). Gleiche Logik wie initial-import, nur pro Filiale.
    /// </summary>
    [HttpPost("sync/employees/initial-import-branch")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> SyncEmployeesInitialImportBranch([FromBody] InitialImportBranchDto dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var since = dto.Since ?? new DateOnly(2021, 1, 1);
        var res = await _empSync.CommitAsync(new Services.EasyAtWork.EasyAtWorkEmployeeSyncService.SyncRequest
        {
            CompanyProfileId          = dto.CompanyProfileId,
            OnlyActive                = false,
            EmployeeCutoffOverride    = since,
            AltSuffixForPreMirusExits = true,
            SkipDetailCalls           = dto.SkipDetailCalls ?? true,   // Tief-Import: standardmässig schnell
            SkipContracts             = true,   // Tiefenimport = NUR Stammdaten, NIE Verträge (Walter 08.07.2026)
        }, ct: ct);
        return Ok(new { companyProfileId = dto.CompanyProfileId, inserted = res.CountInserted, updated = res.CountUpdated, total = res.CountTotal, existing = res.CountExisting,
                        blocked = res.Blocked, numberConflicts = res.NumberConflicts });
    }

    /// <summary>
    /// EINMALIGER Tief-Import (Walter-Vorgabe 21.06.2026): importiert ALLE
    /// inaktiven MA ALLER gemappten Filialen zurück bis zum Stichtag (Default
    /// 1.1.2021), AUTOMATISCH ohne Vorschau. Pre-Mirus-Austritte (Austritt vor
    /// 1.1.2025) bekommen den „alt"-Suffix an die Personalnummer, damit sie
    /// nicht mit den aktuellen Mirus-Nummern kollidieren. Danach lassen sich die
    /// alten Stempelzeiten dieser MA importieren.
    /// </summary>
    [HttpPost("sync/employees/initial-import")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> SyncEmployeesInitialImport([FromBody] InitialImportDto? dto, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var since = dto?.Since ?? new DateOnly(2021, 1, 1);

        var branchIds = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .Select(m => m.CompanyProfileId).Distinct().ToListAsync(ct);
        var branchNames = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => branchIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => string.IsNullOrWhiteSpace(c.BranchName) ? c.CompanyName : c.BranchName, ct);

        int totalInserted = 0, totalUpdated = 0;
        var perBranch = new List<object>();
        foreach (var cpId in branchIds)
        {
            var branchName = branchNames.TryGetValue(cpId, out var nm) ? nm : $"#{cpId}";
            try
            {
                var res = await _empSync.CommitAsync(new Services.EasyAtWork.EasyAtWorkEmployeeSyncService.SyncRequest
                {
                    CompanyProfileId          = cpId,
                    OnlyActive                = false,
                    EmployeeCutoffOverride    = since,
                    AltSuffixForPreMirusExits = true,
                    SkipDetailCalls           = dto?.SkipDetailCalls ?? true,
                    SkipContracts             = true,   // Tiefenimport = NUR Stammdaten, NIE Verträge (Walter 08.07.2026)
                }, ct: ct);
                totalInserted += res.CountInserted;
                totalUpdated  += res.CountUpdated;
                perBranch.Add(new { companyProfileId = cpId, branch = branchName, inserted = res.CountInserted, updated = res.CountUpdated, total = res.CountTotal,
                                    blocked = res.Blocked, numberConflicts = res.NumberConflicts });
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Initial-Import Filiale {Cp} fehlgeschlagen", cpId);
                perBranch.Add(new { companyProfileId = cpId, branch = branchName, error = ex.Message });
            }
        }
        return Ok(new { since, branches = branchIds.Count, totalInserted, totalUpdated, perBranch });
    }

    /// <summary>
    /// On-Demand: liefert für EINEN easy@work-MA die fiscal_info (Bewilligung,
    /// Bank, Ehepartner-Permit) + ALLE Custom Fields/Properties (key+value) —
    /// read-only, zur Anzeige/Validierung in der MA-Sync-Vorschau (Walter
    /// 19.06.2026). Schreibt nichts. Pro Klick nur 2 API-Aufrufe (nicht N×2).
    /// </summary>
    [HttpGet("employees/{companyProfileId:int}/{eawEmployeeId:int}/detail")]
    public async Task<IActionResult> GetEmployeeEasyworkDetail(int companyProfileId, int eawEmployeeId, CancellationToken ct)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED" });
        var mapping = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyProfileId == companyProfileId, ct);
        if (mapping == null) return NotFound(new { error = "NOT_MAPPED" });

        Services.EasyAtWork.EawFiscalInfo? fiscal = null;
        var props  = new List<Services.EasyAtWork.EawProperty>();
        var notes  = new List<string>();
        try { fiscal = await _client.GetFiscalInfoAsync(mapping.EasyAtWorkCustomerId, eawEmployeeId, ct); }
        catch (Exception ex) { notes.Add($"fiscal_info nicht abrufbar: {ex.Message}"); }
        try { props = await _client.GetAllPropertiesAsync(mapping.EasyAtWorkCustomerId, eawEmployeeId, ct); }
        catch (Exception ex) { notes.Add($"properties nicht abrufbar: {ex.Message}"); }

        return Ok(new
        {
            fiscal,
            properties = props
                .OrderBy(p => p.Key)
                .Select(p => new { key = p.Key, value = p.Value, from = p.From, to = p.To }),
            notes
        });
    }

    // ──────────── easy@work-ID-Aliase (alte/zweite IDs pro MA) ───────────
    // Walter-Vorgabe 18.06.2026: Wenn die easy@work-employee_id eines MA
    // mittendrin wechselt, hängen alte Stempel an der alten ID. Hier hinterlegen
    // wir diese alten IDs, damit der Stempel-Sync sie auflösen kann.

    public record AliasCreateDto(int EasyAtWorkId, int CoworkEmployeeId, string? Note);

    /// <summary>Liste aller hinterlegten Alias-IDs (mit MA-Name).</summary>
    [HttpGet("aliases")]
    public async Task<IActionResult> GetAliases(CancellationToken ct)
    {
        var raw = await _db.EasyAtWorkEmployeeAliases.AsNoTracking()
            .OrderByDescending(a => a.Id)
            .Select(a => new
            {
                a.Id,
                a.EasyAtWorkId,
                a.EmployeeId,
                FirstName = a.Employee!.FirstName,
                LastName  = a.Employee.LastName,
                EmployeeNumber = a.Employee.EmployeeNumber,
                a.Note,
                a.CreatedAt,
                a.CreatedBy,
            })
            .ToListAsync(ct);
        var rows = raw.Select(a => new
        {
            a.Id,
            a.EasyAtWorkId,
            a.EmployeeId,
            employeeNumber = a.EmployeeNumber,
            employeeName   = ((a.FirstName ?? "") + " " + (a.LastName ?? "")).Trim(),
            a.Note,
            a.CreatedAt,
            a.CreatedBy,
        });
        return Ok(rows);
    }

    /// <summary>
    /// Hinterlegt eine alte/zweite easy@work-ID für einen MA (Upsert: dieselbe
    /// easy@work-ID kann nur EINEM MA gehören → vorhandener Eintrag wird umgehängt).
    /// </summary>
    [HttpPost("aliases")]
    public async Task<IActionResult> CreateAlias([FromBody] AliasCreateDto dto, CancellationToken ct)
    {
        if (dto.EasyAtWorkId <= 0) return BadRequest(new { error = "EAW_ID_INVALID", message = "Ungültige easy@work-ID." });
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == dto.CoworkEmployeeId, ct);
        if (emp == null) return NotFound(new { error = "EMPLOYEE_NOT_FOUND", message = "Mitarbeiter nicht gefunden." });

        var actor = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                 ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var existing = await _db.EasyAtWorkEmployeeAliases
            .FirstOrDefaultAsync(a => a.EasyAtWorkId == dto.EasyAtWorkId, ct);
        if (existing != null)
        {
            existing.EmployeeId = dto.CoworkEmployeeId;
            existing.Note       = dto.Note;
            existing.CreatedAt  = DateTime.UtcNow;
            existing.CreatedBy  = actor;
        }
        else
        {
            _db.EasyAtWorkEmployeeAliases.Add(new EasyAtWorkEmployeeAlias
            {
                EasyAtWorkId = dto.EasyAtWorkId,
                EmployeeId   = dto.CoworkEmployeeId,
                Note         = dto.Note,
                CreatedAt    = DateTime.UtcNow,
                CreatedBy    = actor,
            });
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    /// <summary>Entfernt einen Alias wieder.</summary>
    [HttpDelete("aliases/{id:int}")]
    public async Task<IActionResult> DeleteAlias(int id, CancellationToken ct)
    {
        var row = await _db.EasyAtWorkEmployeeAliases.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row == null) return NotFound();
        _db.EasyAtWorkEmployeeAliases.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

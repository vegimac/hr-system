using System.Net.Http.Headers;
using System.Text.Json;

namespace HrSystem.Services.EasyAtWork;

/// <summary>
/// HTTP-Client für die easy@work-API. Holt sich beim ersten Aufruf via
/// OAuth2-client-credentials einen Bearer-Token und cached den im Speicher
/// bis 60 s vor Ablauf (Default 1 h gemäss API). Bei 401 wird einmalig
/// refresht und der Aufruf wiederholt.
///
/// Registriert als <b>Singleton</b> in Program.cs — der Token-Cache lebt
/// dann über alle Requests. Falls die Settings nicht konfiguriert sind
/// (kein ClientId/Secret/BaseUrl), wirft <see cref="EnsureConfigured"/>
/// einen InvalidOperationException; Aufrufer sollten vorher
/// <see cref="IsConfigured"/> prüfen oder den Controller-Endpoint
/// 503 zurückgeben lassen.
/// </summary>
public class EasyAtWorkClient
{
    private readonly HttpClient _http;
    private readonly EasyAtWorkSettings _settings;
    private readonly ILogger<EasyAtWorkClient> _log;

    // Token-Cache (in-memory, Singleton-Scope)
    private string? _token;
    private DateTime _tokenExpiresAtUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            // easy@work liefert teils "yyyy-MM-dd HH:mm:ss" (Space statt T) →
            // Standardparser scheitert. Toleranter Converter.
            new FlexibleDateTimeConverter(),
            new FlexibleDateOnlyConverter(),
        }
    };

    public EasyAtWorkClient(
        HttpClient http,
        EasyAtWorkSettings settings,
        ILogger<EasyAtWorkClient> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    public bool IsConfigured => _settings.IsConfigured;

    public string BaseUrl    => _settings.BaseUrl;
    public string ClientId   => _settings.ClientId;
    // Secret bewusst NICHT als Property — Reflection/Logs sollen das nicht greifen.

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
            throw new InvalidOperationException(
                "easy@work nicht konfiguriert — bitte EASYATWORK_CLIENT_ID, "
                + "EASYATWORK_CLIENT_SECRET und EASYATWORK_BASE_URL setzen.");
    }

    // ─────────────────────────── Token ──────────────────────────────

    /// <summary>Gibt einen gültigen Bearer-Token zurück (cached oder neu geholt).</summary>
    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        EnsureConfigured();

        // Schnellpfad ohne Lock
        if (_token != null && DateTime.UtcNow < _tokenExpiresAtUtc)
            return _token;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token != null && DateTime.UtcNow < _tokenExpiresAtUtc)
                return _token;

            var tokenUrl = $"{_settings.BaseUrl.TrimEnd('/')}/oauth/token";
            var req = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"]    = "client_credentials",
                    ["client_id"]     = _settings.ClientId,
                    ["client_secret"] = _settings.ClientSecret,
                })
            };

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"easy@work-Token-Request fehlgeschlagen ({(int)resp.StatusCode}): {body}");

            var token = JsonSerializer.Deserialize<EawTokenResponse>(body, JsonOpts)
                ?? throw new InvalidOperationException("Token-Response leer.");

            _token = token.AccessToken;
            // 60 s Sicherheitsmarge, damit ein Request nicht genau auf der Ablaufkante stirbt.
            _tokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn) - 60);

            _log.LogInformation("easy@work: Token erneuert, gültig bis {ExpiresAt:o}", _tokenExpiresAtUtc);
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>Token-Cache verwerfen (bei 401 oder manuellem Reset).</summary>
    public void InvalidateToken()
    {
        _token = null;
        _tokenExpiresAtUtc = DateTime.MinValue;
    }

    // ─────────────────────────── HTTP ───────────────────────────────

    /// <summary>
    /// Führt einen GET aus, deserialisiert die Antwort als T. Bei 401 wird
    /// einmal der Token refresht und neu versucht.
    /// </summary>
    public async Task<T> GetJsonAsync<T>(string path, CancellationToken ct = default)
    {
        EnsureConfigured();

        async Task<HttpResponseMessage> SendOnce()
        {
            var token = await GetTokenAsync(ct);
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_settings.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return await _http.SendAsync(req, ct);
        }

        using var resp1 = await SendOnce();
        if (resp1.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            InvalidateToken();
            using var resp2 = await SendOnce();
            return await DeserializeOrThrow<T>(resp2, path, ct);
        }
        return await DeserializeOrThrow<T>(resp1, path, ct);
    }

    private static async Task<T> DeserializeOrThrow<T>(HttpResponseMessage resp, string path, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"easy@work GET /{path} fehlgeschlagen ({(int)resp.StatusCode}): {body}",
                null, resp.StatusCode);   // StatusCode mitgeben → Caller kann 404 gezielt behandeln
        var data = JsonSerializer.Deserialize<T>(body, JsonOpts);
        if (data == null)
            throw new InvalidOperationException($"easy@work GET /{path}: leere Antwort.");
        return data;
    }

    /// <summary>
    /// GET, gibt den ROHEN JSON-Body + HTTP-Status zurück (für Diagnose/Dump —
    /// auch nicht gemappte Felder werden so sichtbar). Bei 401 einmal Token-
    /// Refresh + Retry. Wirft NICHT bei Fehler-Status — der Aufrufer entscheidet.
    /// </summary>
    public async Task<(int status, string body)> GetRawAsync(string path, CancellationToken ct = default)
    {
        EnsureConfigured();
        async Task<(int, string)> SendOnce()
        {
            var token = await GetTokenAsync(ct);
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_settings.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return ((int)resp.StatusCode, body);
        }
        var r = await SendOnce();
        if (r.Item1 == 401) { InvalidateToken(); r = await SendOnce(); }
        return r;
    }

    // ──────────────────── Convenience-Endpoints ─────────────────────

    /// <summary>Liste aller für den Client sichtbaren Customers (Filialen).</summary>
    public Task<EawPaginated<EawCustomer>> GetCustomersAsync(CancellationToken ct = default)
        => GetJsonAsync<EawPaginated<EawCustomer>>("customers", ct);

    public Task<EawPaginated<EawEmployee>> GetEmployeesAsync(int customerId, bool includeInactive = false, CancellationToken ct = default)
    {
        var path = includeInactive
            ? $"customers/{customerId}/employees?include_inactive=1"
            : $"customers/{customerId}/employees";
        return GetJsonAsync<EawPaginated<EawEmployee>>(path, ct);
    }

    /// <summary>
    /// Lädt ALLE MA der Filiale, die zum gegebenen Stichtag aktiv WAREN
    /// (`active=YYYY-MM-DD`). Folgt der Pagination und gibt das vollständige
    /// Result zurück. Default-Page-Size 200 (das obere Limit der API).
    /// </summary>
    public Task<List<EawEmployee>> GetAllEmployeesActiveAtAsync(
        int customerId, DateOnly activeAt, CancellationToken ct = default)
        => GetAllEmployeesPagedAsync(customerId, $"active={activeAt:yyyy-MM-dd}", ct);

    /// <summary>
    /// Lädt ALLE MA der Filiale INKL. ausgetretener (`include_inactive=true`).
    /// Wenn die API mit 422 antwortet, fällt sie automatisch auf `=1` zurück
    /// (manche easy@work-Setups akzeptieren den Boolean-String nicht).
    /// </summary>
    public virtual async Task<List<EawEmployee>> GetAllEmployeesIncludingInactiveAsync(
        int customerId, CancellationToken ct = default)
    {
        try
        {
            return await GetAllEmployeesPagedAsync(customerId, "include_inactive=true", ct);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("422"))
        {
            // Fallback für strenge API-Validatoren
            return await GetAllEmployeesPagedAsync(customerId, "include_inactive=1", ct);
        }
    }

    private async Task<List<EawEmployee>> GetAllEmployeesPagedAsync(
        int customerId, string filterQuery, CancellationToken ct)
    {
        var all = new List<EawEmployee>();
        int page = 1;
        const int perPage = 200;
        while (true)
        {
            var path = $"customers/{customerId}/employees"
                     + $"?{filterQuery}"
                     + $"&per_page={perPage}"
                     + $"&page={page}";
            var res = await GetJsonAsync<EawPaginated<EawEmployee>>(path, ct);
            if (res.Data != null) all.AddRange(res.Data);
            if (res.LastPage == null || page >= res.LastPage.Value) break;
            page++;
            if (page > 50) break;
        }
        return all;
    }

    /// <summary>
    /// Notfallkontakte des Customers (Walter 26.08.2026, Endpunkt per Probe
    /// bestätigt). Kleine Liste (~20 Einträge) — komplett paginiert laden.
    /// </summary>
    public async Task<List<EawEmergencyContact>> GetEmergencyContactsAsync(int customerId, CancellationToken ct = default)
    {
        var all = new List<EawEmergencyContact>();
        int page = 1;
        while (true)
        {
            var res = await GetJsonAsync<EawPaginated<EawEmergencyContact>>(
                $"customers/{customerId}/emergency_contacts?per_page=200&page={page}", ct);
            if (res.Data != null) all.AddRange(res.Data);
            if (res.LastPage == null || page >= res.LastPage.Value) break;
            page++;
            if (page > 20) break;
        }
        return all;
    }

    public Task<EawPaginated<EawTimepunch>> GetTimepunchesAsync(int customerId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => GetJsonAsync<EawPaginated<EawTimepunch>>(
            $"customers/{customerId}/timepunches?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", ct);

    /// <summary>
    /// Einzelner Stempel inkl. Changelog (Walter 18.07.2026). Die List-API liefert
    /// die Audit-Zeilen «Ein/Aus vom … geändert» nicht — nur der Einzelabruf mit
    /// <c>include_changelog=true</c>. Best-effort: null bei Fehler/404.
    /// </summary>
    public virtual async Task<EawTimepunch?> GetTimepunchAsync(
        int customerId, int timepunchId, CancellationToken ct = default)
    {
        var path = $"customers/{customerId}/timepunches/{timepunchId}"
                 + "?include_changelog=true"
                 + "&with%5B%5D=comments";
        var (status, body) = await GetRawAsync(path, ct);
        if (status == 404 || string.IsNullOrWhiteSpace(body)) return null;
        if (status < 200 || status >= 300)
        {
            _log.LogDebug("easy@work Timepunch {Id} (Customer {C}): HTTP {Status}.", timepunchId, customerId, status);
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            // { "data": { … } } oder nacktes Objekt
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("data", out var dataEl)
                && dataEl.ValueKind == JsonValueKind.Object)
            {
                return dataEl.Deserialize<EawTimepunch>(JsonOpts);
            }
            return root.Deserialize<EawTimepunch>(JsonOpts);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "easy@work Timepunch {Id} (Customer {C}): JSON nicht lesbar.", timepunchId, customerId);
            return null;
        }
    }

    /// <summary>
    /// Lädt ALLE Stempelzeiten der Filiale im Zeitraum (folgt der Pagination).
    /// Per-Page 200 (API-Max). Sicherheits-Stop bei 500 Seiten (= 100k Stempel,
    /// das wäre absurd).
    /// </summary>
    public virtual Task<List<EawTimepunch>> GetAllTimepunchesAsync(
        int customerId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => GetAllTimepunchesAsync(customerId, from, to, pageProgress: null, ct);

    /// <summary>Wie oben, zusätzlich mit Seiten-Fortschritt (done, total) für
    /// den Async-Import-Job (Walter-Vorgabe 09.07.2026). Separate Überladung,
    /// damit bestehende Test-Overrides der 4-Parameter-Signatur intakt bleiben.</summary>
    public virtual async Task<List<EawTimepunch>> GetAllTimepunchesAsync(
        int customerId, DateOnly from, DateOnly to, Action<int, int>? pageProgress, CancellationToken ct = default)
    {
        // Bewusst OHNE with[]=comments (Walter-Bug 18.07.2026): die List-API
        // knallt bei kaputten Comment-Joins mit 500 «Trying to get property of
        // non-object». Comments + Changelog holen wir nur für bearbeitete
        // Stempel nach (GetTimepunchAsync / EnrichEditedChangelogsAsync).
        try
        {
            return await GetAllTimepunchesRangeAsync(customerId, from, to, pageProgress, ct);
        }
        catch (HttpRequestException ex) when ((int?)ex.StatusCode is >= 500 and < 600
                                              && !(from.Year == to.Year && from.Month == to.Month))
        {
            // easy@work 500 auf dem ganzen Quartal → monatsweise PARALLEL nachladen
            // (Walter 18.07.2026: sequenzieller Fallback war der Flaschenhals).
            _log.LogWarning(ex,
                "easy@work timepunches {From}–{To} (Customer {C}) → {Status}; Fallback monatsweise parallel.",
                from, to, customerId, (int?)ex.StatusCode);

            var months = new List<(DateOnly From, DateOnly To)>();
            var cursor = new DateOnly(from.Year, from.Month, 1);
            var endMonth = new DateOnly(to.Year, to.Month, 1);
            while (cursor <= endMonth)
            {
                var chunkFrom = cursor < from ? from : cursor;
                var chunkTo = chunkFrom.AddMonths(1).AddDays(-1);
                if (chunkTo > to) chunkTo = to;
                months.Add((chunkFrom, chunkTo));
                cursor = cursor.AddMonths(1);
            }

            var all = new List<EawTimepunch>();
            var seen = new HashSet<int>();
            var doneMonths = 0;
            // Max. 2 Monate gleichzeitig — jeder Monat lädt Seiten selbst parallel
            // (Drossel 4) → Gesamtlast bleibt unter ~8 Requests.
            var monthSem = new SemaphoreSlim(2);
            var monthTasks = months.Select(async m =>
            {
                await monthSem.WaitAsync(ct);
                try
                {
                    try
                    {
                        return await GetAllTimepunchesRangeAsync(
                            customerId, m.From, m.To, pageProgress: null, ct);
                    }
                    catch (HttpRequestException ex2) when ((int?)ex2.StatusCode is >= 500 and < 600)
                    {
                        _log.LogWarning(ex2,
                            "easy@work timepunches Monat {From}–{To} (Customer {C}) → 5xx; Fallback wochenweise parallel.",
                            m.From, m.To, customerId);
                        return await GetAllTimepunchesWeeklyAsync(customerId, m.From, m.To, ct);
                    }
                }
                finally
                {
                    monthSem.Release();
                    var d = Interlocked.Increment(ref doneMonths);
                    pageProgress?.Invoke(d, months.Count);
                }
            }).ToList();

            foreach (var chunk in await Task.WhenAll(monthTasks))
                foreach (var p in chunk)
                    if (seen.Add(p.Id)) all.Add(p);
            return all;
        }
    }

    /// <summary>Wochenweise parallel laden (5xx-Fallback innerhalb eines Monats).</summary>
    private async Task<List<EawTimepunch>> GetAllTimepunchesWeeklyAsync(
        int customerId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var weeks = new List<(DateOnly From, DateOnly To)>();
        var weekStart = from;
        while (weekStart <= to)
        {
            var weekEnd = weekStart.AddDays(6);
            if (weekEnd > to) weekEnd = to;
            weeks.Add((weekStart, weekEnd));
            weekStart = weekEnd.AddDays(1);
        }

        var all = new List<EawTimepunch>();
        var seen = new HashSet<int>();
        var weekSem = new SemaphoreSlim(4);
        var weekTasks = weeks.Select(async w =>
        {
            await weekSem.WaitAsync(ct);
            try
            {
                return await GetAllTimepunchesRangeAsync(
                    customerId, w.From, w.To, pageProgress: null, ct);
            }
            catch (HttpRequestException ex3) when ((int?)ex3.StatusCode is >= 500 and < 600)
            {
                _log.LogError(ex3,
                    "easy@work timepunches Woche {From}–{To} (Customer {C}) übersprungen (5xx).",
                    w.From, w.To, customerId);
                return new List<EawTimepunch>();
            }
            finally { weekSem.Release(); }
        }).ToList();

        foreach (var week in await Task.WhenAll(weekTasks))
            foreach (var p in week)
                if (seen.Add(p.Id)) all.Add(p);
        return all;
    }

    /// <summary>Eine Range paginiert laden (ohne comments).</summary>
    private async Task<List<EawTimepunch>> GetAllTimepunchesRangeAsync(
        int customerId, DateOnly from, DateOnly to, Action<int, int>? pageProgress, CancellationToken ct)
    {
        const int perPage = 200;
        string PathFor(int page) =>
            $"customers/{customerId}/timepunches"
            + $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"
            + $"&per_page={perPage}"
            + $"&page={page}";

        // Seite 1 sequenziell (liefert LastPage + wärmt den Token-Cache),
        // Rest PARALLEL mit Drossel — Walter-Vorgabe 09.07.2026.
        var first = await GetJsonAsync<EawPaginated<EawTimepunch>>(PathFor(1), ct);
        var all = new List<EawTimepunch>(first.Data ?? new());
        var lastPage = Math.Min(first.LastPage ?? 1, 500);
        pageProgress?.Invoke(1, lastPage);
        if (lastPage <= 1) return all;

        var sem = new SemaphoreSlim(4);
        var doneCount = 1;
        var tasks = Enumerable.Range(2, lastPage - 1).Select(async page =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var res = await GetJsonAsync<EawPaginated<EawTimepunch>>(PathFor(page), ct);
                pageProgress?.Invoke(Interlocked.Increment(ref doneCount), lastPage);
                return (page, res);
            }
            finally { sem.Release(); }
        }).ToList();
        var pages = await Task.WhenAll(tasks);
        foreach (var (_, res) in pages.OrderBy(p => p.page))
            if (res.Data != null) all.AddRange(res.Data);
        return all;
    }

    /// <summary>
    /// Lädt ALLE seit <paramref name="lastSync"/> geänderten/neuen/gelöschten
    /// Stempelzeiten der Filiale (Delta-Feed `/timepunch_updates?last_sync=…`).
    /// Folgt der Pagination, lädt die Comments mit. Für den inkrementellen
    /// Auto-Sync (Walter-Vorgabe 19.06.2026). `last_sync` wird als ISO-8601-UTC
    /// gesendet. Gibt EawTimepunch-Objekte zurück (inkl. solcher mit
    /// gesetztem <c>deleted_at</c> = in easy@work gelöscht).
    /// </summary>
    public virtual async Task<List<EawTimepunch>> GetAllTimepunchUpdatesAsync(
        int customerId, DateTime lastSync, CancellationToken ct = default)
    {
        var lastSyncUtc = (lastSync.Kind == DateTimeKind.Utc ? lastSync : lastSync.ToUniversalTime())
            .ToString("yyyy-MM-ddTHH:mm:ssZ");
        var all = new List<EawTimepunch>();
        int page = 1;
        const int perPage = 200;
        while (true)
        {
            // Ohne with[]=comments — siehe GetAllTimepunchesAsync (easy@work-500).
            var path = $"customers/{customerId}/timepunch_updates"
                     + $"?last_sync={Uri.EscapeDataString(lastSyncUtc)}"
                     + $"&per_page={perPage}"
                     + $"&page={page}";
            var res = await GetJsonAsync<EawPaginated<EawTimepunch>>(path, ct);
            if (res.Data != null) all.AddRange(res.Data);
            if (res.LastPage == null || page >= res.LastPage.Value) break;
            page++;
            if (page > 500) break;
        }
        return all;
    }

    /// <summary>
    /// Einzel-Abruf eines MA per easy@work-ID (Walter-Vorgabe 20.06.2026) — auch
    /// für MA, die NICHT mehr in der (inkl. inaktiv) Liste stehen (gelöscht/
    /// archiviert). Best-effort: liefert null bei 404/Fehler. Die API kann einen
    /// endgültig gelöschten MA dennoch verweigern — dann bleibt nur Zuordnen/Skip.
    /// </summary>
    // Einzel-Antworten liefert easy@work mal MIT «data»-Hülle, mal das Objekt
    // DIREKT (Walter-Bug 05.08.2026, Julia Sanchez Büchi: der Single-Endpoint
    // gab das Employee-Objekt ungewrappt zurück → EawSingle.Data war null →
    // Sync meldete «nicht gefunden», obwohl die API 200 lieferte; der Dump
    // zeigte es). Deshalb tolerant parsen: erst «data» probieren, sonst root.
    private static T? UnwrapSingle<T>(JsonElement el) where T : class
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty("data", out var d)
            && d.ValueKind == JsonValueKind.Object)
            return d.Deserialize<T>(JsonOpts);
        if (el.ValueKind == JsonValueKind.Object)
            return el.Deserialize<T>(JsonOpts);
        return null;
    }

    public virtual async Task<EawEmployee?> GetEmployeeByIdAsync(int customerId, int employeeId, CancellationToken ct = default)
    {
        // NUR 404 = «gibt es hier nicht» → null. Alle anderen Fehler (Timeout,
        // 401, 429, 500) werfen — sonst meldet der Sync fälschlich «MA nicht
        // gefunden», obwohl die API nur gerade nicht erreichbar war
        // (Walter-Bug 09.07.2026, Behija/580009).
        try
        {
            var el = await GetJsonAsync<JsonElement>(
                $"customers/{customerId}/employees/{employeeId}", ct);
            return UnwrapSingle<EawEmployee>(el);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        { return null; }
    }

    /// <summary>
    /// Legacy-Reparaturpfad: einzelne Person per Personalnummer holen
    /// (API-Syntax: n + Nummer), um alte Cowork-Werte zu korrigieren, bei denen
    /// versehentlich user_id statt employee.id gespeichert wurde.
    /// </summary>
    public virtual async Task<EawEmployee?> GetEmployeeByNumberAsync(int customerId, string employeeNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(employeeNumber)) return null;
        try
        {
            var number = employeeNumber.Trim();
            if (!number.StartsWith("n", StringComparison.OrdinalIgnoreCase))
                number = "n" + number;
            var el = await GetJsonAsync<JsonElement>(
                $"customers/{customerId}/employees/{Uri.EscapeDataString(number)}", ct);
            return UnwrapSingle<EawEmployee>(el);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        { return null; }
    }

    // Gelöschte Verträge/Tarife (Soft-Delete deleted_at) werden ZENTRAL hier
    // herausgefiltert (Walter-Vorgabe 22.07.2026): easy@work liefert sie mit,
    // ein gelöschter offener Vertrag überlappt sonst alles → STRICT-Skip und
    // der aktuelle Vertrag wird nie importiert. Kein Consumer will Gelöschte.
    public virtual async Task<EawPaginated<EawContract>> GetContractsAsync(int customerId, int employeeId, CancellationToken ct = default)
    {
        var res = await GetJsonAsync<EawPaginated<EawContract>>(
            $"customers/{customerId}/employees/{employeeId}/contracts", ct);
        if (res?.Data != null) res.Data = res.Data.Where(c => !c.IsDeleted).ToList();
        return res!;
    }

    /// <summary>
    /// Vertragstypen des Customers (Name zu type_id). Die MA-Contracts liefern oft
    /// nur type_id ohne Name — ohne Katalog fällt die Klassifizierung fälschlich
    /// auf die 17h-Heuristik (MTP mit 17 Std → FLEX). Walter 02.08.2026.
    /// </summary>
    public virtual async Task<Dictionary<int, string>> GetContractTypesByIdAsync(
        int customerId, CancellationToken ct = default)
    {
        var map = new Dictionary<int, string>();
        const int perPage = 200;
        var page = 1;
        while (true)
        {
            var res = await GetJsonAsync<EawPaginated<EawContractType>>(
                $"customers/{customerId}/contract_types?per_page={perPage}&page={page}", ct);
            var rows = res?.Data ?? new List<EawContractType>();
            foreach (var t in rows)
            {
                if (t.Id <= 0 || string.IsNullOrWhiteSpace(t.Name)) continue;
                map[t.Id] = t.Name!.Trim();
            }
            var last = res?.LastPage ?? page;
            if (page >= last || rows.Count == 0) break;
            page++;
        }
        return map;
    }

    public virtual async Task<EawPaginated<EawPayRate>> GetPayRatesAsync(int customerId, int employeeId, CancellationToken ct = default)
    {
        var res = await GetJsonAsync<EawPaginated<EawPayRate>>(
            $"customers/{customerId}/employees/{employeeId}/pay_rates", ct);
        if (res?.Data != null) res.Data = res.Data.Where(r => !r.IsDeleted).ToList();
        return res!;
    }

    /// <summary>Funktionen/Positionen eines MA (Name = job_group.code). Walter 22.06.2026.</summary>
    public virtual Task<EawPaginated<EawPosition>> GetPositionsAsync(int customerId, int employeeId, CancellationToken ct = default)
        => GetJsonAsync<EawPaginated<EawPosition>>(
            $"customers/{customerId}/employees/{employeeId}/positions", ct);

    /// <summary>Verfügbarkeiten eines MA (Walter 09.07.2026, Endpunkt von easy@work
    /// bestätigt). per_page hochgesetzt, damit die Historie in einem Call kommt.</summary>
    public virtual Task<EawPaginated<EawAvailability>> GetAvailabilitiesAsync(int customerId, int employeeId, CancellationToken ct = default)
        => GetJsonAsync<EawPaginated<EawAvailability>>(
            $"customers/{customerId}/employees/{employeeId}/availabilities?per_page=200", ct);

    /// <summary>Wochenmuster-Tage EINER Verfügbarkeit (max. 7 Zeilen bei repeat=7).</summary>
    public virtual Task<EawPaginated<EawAvailabilityDay>> GetAvailabilityDaysAsync(int customerId, int employeeId, long availabilityId, CancellationToken ct = default)
        => GetJsonAsync<EawPaginated<EawAvailabilityDay>>(
            $"customers/{customerId}/employees/{employeeId}/availabilities/{availabilityId}/days?per_page=200", ct);

    /// <summary>
    /// fiscal_info eines MA. easy@work liefert 404, wenn für den MA schlicht
    /// KEIN fiscal_info-Datensatz erfasst ist (kein Fehler) — das wird hier
    /// als <c>null</c> zurückgegeben statt als Exception (Walter 08.07.2026,
    /// vorher erschien in der Sync-Detail-Ansicht eine irritierende ⚠-Notiz).
    /// Andere Fehler (401/500/…) werfen weiterhin.
    /// </summary>
    public virtual async Task<EawFiscalInfo?> GetFiscalInfoAsync(int customerId, int employeeId, CancellationToken ct = default)
    {
        try
        {
            return await GetJsonAsync<EawFiscalInfo?>(
                $"customers/{customerId}/employees/{employeeId}/fiscal_info", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Custom Fields / „Properties" eines MA (Walter-Vorgabe 19.06.2026). Hier
    /// liegen AHV-Nummer, Familienstand, Funktion, Qualification CCNT etc., je
    /// als <c>{ key, value, from, to }</c>. Folgt der Pagination.
    /// </summary>
    public virtual async Task<List<EawProperty>> GetAllPropertiesAsync(
        int customerId, int employeeId, CancellationToken ct = default)
    {
        var all = new List<EawProperty>();
        int page = 1;
        const int perPage = 200;
        while (true)
        {
            var path = $"customers/{customerId}/employees/{employeeId}/properties"
                     + $"?per_page={perPage}&page={page}";
            var res = await GetJsonAsync<EawPaginated<EawProperty>>(path, ct);
            if (res.Data != null) all.AddRange(res.Data);
            if (res.LastPage == null || page >= res.LastPage.Value) break;
            page++;
            if (page > 50) break;
        }
        return all;
    }
}

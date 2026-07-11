using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HrSystem.Controllers;

/// <summary>
/// d.velop-documents-API — Etappe 1 (Walter-Vorgabe 10.07.2026): Konfiguration
/// (BaseUrl + API-Key), Verbindungstest und Roh-Probe. Zweck: API-Voll-Scan
/// aller Personaldossiers, damit beim Umzug kein Dokument vergessen geht.
/// Der eigentliche Scanner (Etappe 2) wird gebaut, sobald die echte
/// JSON-Struktur über die Probe gesichtet ist — gleiche Discovery-Doktrin
/// wie beim easy@work-Sync. Alles admin-only, Etappe 1 ist komplett read-only
/// gegenüber d.velop.
/// </summary>
[ApiController]
[Route("api/dvelop-api")]
[Authorize(Roles = "admin")]
public class DvelopApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SimpleAesService _aes;
    private readonly IHttpClientFactory _httpFactory;

    public DvelopApiController(AppDbContext db, SimpleAesService aes, IHttpClientFactory httpFactory)
    {
        _db = db;
        _aes = aes;
        _httpFactory = httpFactory;
    }

    private async Task<DvelopSetting> GetOrCreateAsync()
    {
        var s = await _db.DvelopSettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (s == null)
        {
            s = new DvelopSetting { Id = 1 };
            _db.DvelopSettings.Add(s);
            await _db.SaveChangesAsync();
        }
        return s;
    }

    public record SettingsDto(string? BaseUrl, string? ApiKey);

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var s = await GetOrCreateAsync();
        return Ok(new
        {
            baseUrl = s.BaseUrl,
            hasApiKey = !string.IsNullOrEmpty(s.ApiKeyEncrypted),
            updatedAt = s.UpdatedAt,
        });
    }

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] SettingsDto dto)
    {
        var s = await GetOrCreateAsync();
        s.BaseUrl = string.IsNullOrWhiteSpace(dto.BaseUrl) ? null : dto.BaseUrl.Trim().TrimEnd('/');
        // API-Key nur überschreiben, wenn nicht-leer geschickt (analog eCall).
        if (!string.IsNullOrWhiteSpace(dto.ApiKey))
            s.ApiKeyEncrypted = _aes.Encrypt(dto.ApiKey.Trim());
        s.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Roher GET auf die d.velop-API. path z.B. «dms/r» (Repository-
    /// Liste = Verbindungstest). Antwort: Status + Body (JSON wenn parsebar).</summary>
    [HttpGet("probe")]
    public async Task<IActionResult> Probe([FromQuery] string? path, CancellationToken ct)
    {
        var s = await GetOrCreateAsync();
        if (string.IsNullOrEmpty(s.BaseUrl) || string.IsNullOrEmpty(s.ApiKeyEncrypted))
            return BadRequest(new { error = "NOT_CONFIGURED", message = "Bitte zuerst Base-URL und API-Key speichern." });

        var clean = (path ?? "dms/r").TrimStart('/');
        var url = $"{s.BaseUrl}/{clean}";
        string apiKey;
        try { apiKey = _aes.Decrypt(s.ApiKeyEncrypted); }
        catch { return StatusCode(500, new { error = "KEY_DECRYPT_FAILED" }); }

        try
        {
            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            // d.velop cloud: API-Key als Bearer-Token (App-Session wird bei
            // Bedarf automatisch etabliert). Accept: HAL-JSON der DMS-App.
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            req.Headers.TryAddWithoutValidation("Accept", "application/hal+json, application/json");
            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            object parsed;
            try { parsed = JsonSerializer.Deserialize<JsonElement>(body); }
            catch { parsed = body.Length > 20000 ? body[..20000] + "…" : body; }
            return Ok(new { url, status = (int)resp.StatusCode, body = parsed });
        }
        catch (Exception ex)
        {
            return Ok(new { url, status = -1, error = ex.Message });
        }
    }
}

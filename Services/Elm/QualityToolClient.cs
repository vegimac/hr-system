using System.Net.Http.Headers;

namespace HrSystem.Services.Elm;

/// <summary>
/// REST-Schnittstelle des Swissdec Quality Tools (Lohntest, Walter 15.10.2026): Bericht holen,
/// gesendete Meldungen zurücksetzen, neuen Lauf mit der Referenz laden. Nur auf der Testinstanz.
/// Der API-Schlüssel steht nur in der Server-Konfiguration (<c>Swissdec:QualityToolApiKey</c>,
/// systemd <c>Swissdec__QualityToolApiKey</c> in /etc/hr-system/test.env) und verlässt den Server nie.
/// Die Meldungen selbst gehen nicht hierüber, sondern signiert über <see cref="ElmEndpunkte.QualityToolUrl"/>.
/// </summary>
public class QualityToolClient
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _cfg;
    private readonly ElmEinstellungen _einst;

    public QualityToolClient(IHttpClientFactory http, IConfiguration cfg, ElmEinstellungen einst)
    {
        _http = http;
        _cfg = cfg;
        _einst = einst;
    }

    private string? Schluessel => string.IsNullOrWhiteSpace(_cfg["Swissdec:QualityToolApiKey"]) ? null : _cfg["Swissdec:QualityToolApiKey"]!.Trim();

    /// <summary>Grund, warum das Quality Tool hier nicht nutzbar ist — NULL = bereit.</summary>
    public string? NichtBereit()
    {
        if (!ElmEndpunkte.IstTestinstanz()) return "Das Quality Tool gibt es nur auf der Testinstanz.";
        if (Schluessel == null) return "Kein API-Schlüssel für das Quality Tool auf dem Server hinterlegt.";
        if (string.IsNullOrWhiteSpace(_einst.MonitoringId)) return "Keine MonitoringID eingetragen.";
        return null;
    }

    public string? MonitoringId => _einst.MonitoringId;

    public Task<QualityToolBericht.Bericht> BerichtAsync(CancellationToken ct)
        => AufrufAsync(HttpMethod.Get, "current/report", ct);

    /// <summary>Gesendete Meldungen des laufenden Laufs löschen, Referenz bleibt.</summary>
    public Task<QualityToolBericht.Bericht> SendungenZuruecksetzenAsync(CancellationToken ct)
        => AufrufAsync(HttpMethod.Delete, "current/requests/delete", ct);

    /// <summary>
    /// Laufenden Lauf archivieren (Ergebnisse bleiben im Quality Tool einsehbar) und einen neuen mit der
    /// aktuellen deutschen Referenz anlegen — Rundungstoleranz 0.00 wie bisher von Hand eingestellt.
    /// </summary>
    public async Task<QualityToolBericht.Bericht> NeuerLaufAsync(CancellationToken ct)
    {
        await AufrufAsync(HttpMethod.Post, "current/archive", ct);
        return await AufrufAsync(HttpMethod.Post,
            "current/reset/de?roundingTolerancePerson=0&roundingToleranceTotal=0&ignoreCase=false&ignoreSpecialCharacters=false", ct);
    }

    private async Task<QualityToolBericht.Bericht> AufrufAsync(HttpMethod methode, string pfad, CancellationToken ct)
    {
        if (NichtBereit() is { } grund) throw new InvalidOperationException(grund);
        var url = ElmEndpunkte.QualityToolApiUrl + Uri.EscapeDataString(_einst.MonitoringId!) + "/" + pfad;
        using var req = new HttpRequestMessage(methode, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Schluessel);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        HttpResponseMessage res;
        try { res = await client.SendAsync(req, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("Quality Tool nicht erreichbar: " + ex.Message);
        }
        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            if ((int)res.StatusCode is 401 or 403)
                throw new InvalidOperationException("Das Quality Tool lehnt den API-Schlüssel ab — bitte im Swissdec-Profil prüfen und auf dem Server neu hinterlegen.");
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Quality Tool antwortet mit Fehler {(int)res.StatusCode}"
                    + (text.Length > 0 ? ": " + (text.Length > 300 ? text[..300] : text) : "."));
            return QualityToolBericht.Lies(text.Length == 0 ? "{}" : text);
        }
    }
}

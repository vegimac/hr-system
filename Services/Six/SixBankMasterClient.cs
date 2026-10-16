using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HrSystem.Services.Six;

/// <summary>
/// Client für die öffentliche SIX Swiss Bank Master API v3
/// (https://api.six-group.com/api/epcd/bankmaster/v3) — ohne API-Schlüssel.
/// </summary>
public sealed class SixBankMasterClient
{
    public const string HttpClientName = "SixBankMaster";
    public const string DefaultBaseUrl = "https://api.six-group.com/api/epcd/bankmaster/v3";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _http;
    private readonly ILogger<SixBankMasterClient> _log;

    public SixBankMasterClient(IHttpClientFactory http, ILogger<SixBankMasterClient> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<SixBankMasterResponse> FetchBankMasterAsync(CancellationToken ct = default)
    {
        var client = _http.CreateClient(HttpClientName);
        using var res = await client.GetAsync("bankmaster", ct);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<SixBankMasterResponse>(JsonOpts, ct)
            ?? throw new InvalidOperationException("SIX bankmaster: leere Antwort.");
        if (body.Entries is null || body.Entries.Count == 0)
            throw new InvalidOperationException("SIX bankmaster: keine Einträge.");
        return body;
    }

    public async Task<SixIbanResponse> ValidateIbanAsync(string iban, CancellationToken ct = default)
    {
        var clean = new string(iban.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var client = _http.CreateClient(HttpClientName);
        using var res = await client.GetAsync($"iban?iban={Uri.EscapeDataString(clean)}", ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<SixIbanResponse>(JsonOpts, ct)
            ?? new SixIbanResponse { ValidationResult = "UNKNOWN" };
    }
}

public sealed class SixBankMasterResponse
{
    public int TotalSize { get; set; }
    public DateOnly? ValidOn { get; set; }
    public DateTimeOffset? ReadTime { get; set; }
    public List<SixBankMasterEntry> Entries { get; set; } = new();
}

public sealed class SixBankMasterEntry
{
    public string? EntryType { get; set; }
    public int Iid { get; set; }
    public int? NewIid { get; set; }
    public string? BankOrInstitutionName { get; set; }
    public string? Bic { get; set; }
    public string? StreetName { get; set; }
    public string? BuildingNumber { get; set; }
    public string? PostCode { get; set; }
    public string? TownName { get; set; }
    public string? Country { get; set; }
    public string? IidType { get; set; }
    public DateOnly? ValidOn { get; set; }
}

public sealed class SixIbanResponse
{
    [JsonPropertyName("validationResult")]
    public string? ValidationResult { get; set; }
    public int? Iid { get; set; }
}

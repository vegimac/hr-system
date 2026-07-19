using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// SMS-Versand über eCall (F24 Schweiz) via REST-API.
///
/// Endpoint: POST https://rest.ecall.ch/api/message (HTTPS/TLS only).
/// Auth: HTTP Basic (base64(username:password)).
/// Request-Body (JSON, UTF-8):
///   { "channel": "Sms", "from": "&lt;Absender&gt;", "to": "+41…",
///     "content": { "type": "Text", "text": "…" } }
/// Enum-Werte «Sms»/«Text» sind case-SENSITIV → als literale Strings
/// serialisiert, nicht aus einem C#-Enum.
///
/// Konfiguration kommt aus der DB (ecall_setting, Singleton-Row).
/// Passwort AES-verschlüsselt (SimpleAesService, wie SMTP).
/// </summary>
public class EcallSmsService
{
    private const string EcallEndpoint = "https://rest.ecall.ch/api/message";

    private readonly AppDbContext _db;
    private readonly SimpleAesService _aes;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<EcallSmsService> _log;

    public EcallSmsService(AppDbContext db, SimpleAesService aes,
                           IHttpClientFactory httpFactory, ILogger<EcallSmsService> log)
    {
        _db = db;
        _aes = aes;
        _httpFactory = httpFactory;
        _log = log;
    }

    public record EcallSendResult(bool Ok, string? MessageId, string? Error);

    /// <summary>
    /// SMS senden. <paramref name="purpose"/> + <paramref name="employeeId"/>
    /// sind reine Protokoll-Metadaten (sms_log): VERTRAG / MOMENT / POSTFACH /
    /// BEWILLIGUNG / TEST. JEDER Versandversuch wird geloggt (best-effort) —
    /// auch Fehlschläge; die Test-Umleitung landet in redirected_to.
    /// </summary>
    public async Task<EcallSendResult> SendSmsAsync(string toPhone, string text,
        string? purpose = null, int? employeeId = null, CancellationToken ct = default)
    {
        var originalTo = (toPhone ?? "").Trim();

        async Task<EcallSendResult> Fail(string error, string? redirectedTo = null)
        {
            await TryWriteLogAsync(purpose, employeeId, originalTo, redirectedTo, false, null, error, ct);
            return new EcallSendResult(false, null, error);
        }

        var row = await _db.EcallSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == 1, ct);
        if (row == null || !row.Enabled
            || string.IsNullOrWhiteSpace(row.Username)
            || string.IsNullOrWhiteSpace(row.PasswordEncrypted)
            || string.IsNullOrWhiteSpace(row.Sender))
        {
            return await Fail("eCall ist nicht konfiguriert oder deaktiviert.");
        }

        if (string.IsNullOrWhiteSpace(toPhone))
            return await Fail("Zielnummer fehlt.");
        if (string.IsNullOrWhiteSpace(text))
            return await Fail("Nachrichtentext fehlt.");

        var password = _aes.Decrypt(row.PasswordEncrypted);
        if (string.IsNullOrEmpty(password))
            return await Fail("eCall-Passwort konnte nicht entschlüsselt werden.");

        var to = NormalizePhone(toPhone);
        string? redirect = null;

        // Test-Umleitung (analog EmailService): solange test_redirect_to gesetzt
        // ist, gehen ALLE SMS an diese Nummer; Original-Empfänger im Text-Präfix.
        if (!string.IsNullOrWhiteSpace(row.TestRedirectTo))
        {
            text = $"[TEST → {to}] {text}";
            to = NormalizePhone(row.TestRedirectTo);
            redirect = to;
            _log.LogInformation("[EcallSms] Test-Umleitung aktiv — SMS geht an {Redirect}", to);
        }

        // Body manuell serialisieren, damit die case-sensitiven Enum-Strings
        // «Sms» und «Text» exakt stimmen (nicht aus einem C#-Enum ableiten).
        var payload = new
        {
            channel = "Sms",
            from = row.Sender!.Trim(),
            to = to,
            content = new { type = "Text", text = text }
        };
        var json = JsonSerializer.Serialize(payload);

        try
        {
            var client = _httpFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, EcallEndpoint);
            var basic = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{row.Username!.Trim()}:{password}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var res = await client.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);

            if (res.IsSuccessStatusCode)
            {
                string? messageId = null;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("messageId", out var mid))
                        messageId = mid.GetString();
                }
                catch { /* Body nicht als JSON parsebar — trotzdem 2xx = ok */ }

                _log.LogInformation("[EcallSms] SMS an {To} gesendet (messageId={MessageId})", to, messageId);
                await TryWriteLogAsync(purpose, employeeId, originalTo, redirect, true, messageId, null, ct);
                return new EcallSendResult(true, messageId, null);
            }

            // Fehler-Body auswerten (errorCode/errorMessage), sonst HTTP-Status + Auszug.
            var error = ExtractError(body) ?? $"HTTP {(int)res.StatusCode}: {Truncate(body, 300)}";
            _log.LogWarning("[EcallSms] SMS an {To} fehlgeschlagen — {Error}", to, error);
            await TryWriteLogAsync(purpose, employeeId, originalTo, redirect, false, null, error, ct);
            return new EcallSendResult(false, null, error);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[EcallSms] SMS-Versand an {To} fehlgeschlagen", to);
            await TryWriteLogAsync(purpose, employeeId, originalTo, redirect, false, null, ex.Message, ct);
            return new EcallSendResult(false, null, ex.Message);
        }
    }

    // sms_log-Eintrag schreiben — best-effort, ein Log-Fehler darf den Versand
    // nie beeinflussen.
    private async Task TryWriteLogAsync(string? purpose, int? employeeId, string? toPhone,
        string? redirectedTo, bool ok, string? messageId, string? error, CancellationToken ct)
    {
        try
        {
            _db.SmsLogs.Add(new Models.SmsLog
            {
                CreatedAt    = DateTime.Now,
                Purpose      = purpose,
                EmployeeId   = employeeId,
                ToPhone      = string.IsNullOrWhiteSpace(toPhone) ? null : toPhone,
                RedirectedTo = redirectedTo,
                Ok           = ok,
                MessageId    = messageId,
                Error        = error,
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[EcallSms] sms_log-Eintrag konnte nicht geschrieben werden");
        }
    }

    /// <summary>
    /// Robuste Normalisierung auf internationales Format. Führendes «+»
    /// bleibt; «0041…» bleibt; eine Schweizer «07…»-Nummer wird zu «+41 7…».
    /// Bei Unklarheit bleibt die Nummer unverändert.
    /// </summary>
    private static string NormalizePhone(string raw)
    {
        var t = raw.Trim();
        if (t.StartsWith("+")) return "+" + KeepDigits(t.Substring(1));
        var digits = KeepDigits(t);
        if (string.IsNullOrEmpty(digits)) return t;

        if (digits.StartsWith("0041")) return "+" + digits.Substring(2);   // 0041… → +41…
        if (digits.StartsWith("41") && digits.Length >= 11) return "+" + digits; // 41791234567 → +41791234567
        if (digits.StartsWith("0")) return "+41" + digits.Substring(1);    // 0791234567 → +41791234567

        return t; // unklar → unverändert lassen
    }

    private static string KeepDigits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    private static string? ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string? code = root.TryGetProperty("errorCode", out var c) ? c.GetString() : null;
            string? msg = root.TryGetProperty("errorMessage", out var m) ? m.GetString() : null;

            // errorDetails.errors[] enthält die EIGENTLICHE Ursache pro Parameter
            // (z.B. parameter=«To», messages=[«'0041…' is an invalid receiver.»]).
            // Ohne diese Details ist «InvalidContent» nicht diagnostizierbar.
            string? details = null;
            if (root.TryGetProperty("errorDetails", out var d)
                && d.ValueKind == JsonValueKind.Object
                && d.TryGetProperty("errors", out var errs)
                && errs.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var e in errs.EnumerateArray())
                {
                    string? param = e.TryGetProperty("parameter", out var p) ? p.GetString() : null;
                    var msgs = new List<string>();
                    if (e.TryGetProperty("messages", out var ms) && ms.ValueKind == JsonValueKind.Array)
                        foreach (var mm in ms.EnumerateArray())
                            if (mm.GetString() is { Length: > 0 } s) msgs.Add(s);
                    var joined = string.Join(" ", msgs);
                    parts.Add(string.IsNullOrWhiteSpace(param) ? joined : $"{param}: {joined}");
                }
                if (parts.Count > 0) details = string.Join(" | ", parts);
            }

            if (!string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(msg) || !string.IsNullOrWhiteSpace(details))
                return string.Join(" — ", new[] { code, msg, details }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
        catch { /* kein JSON */ }
        return null;
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");
}

using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace HrSystem.Services.WebStamp;

/// <summary>
/// HTTP-Teil zum Webservice WebStamp (Walter 24.09.2026). Schickt die von
/// <see cref="WebStampSoap"/> gebauten Nachrichten und liefert Fault bzw.
/// Resultat-Element zurück. Die Nachricht selbst wird NIE geloggt — sie
/// enthält das WSWS-Passwort und bei Briefen das ganze PDF.
/// </summary>
public class WebStampClient
{
    private readonly ILogger<WebStampClient> _log;

    public WebStampClient(ILogger<WebStampClient> log) => _log = log;

    // Nur TLS 1.2/1.3. Grosszügiges Timeout: ein Brief-PDF geht Base64 im Body mit.
    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12
                                | System.Security.Authentication.SslProtocols.Tls13,
        },
    })
    { Timeout = TimeSpan.FromSeconds(90) };

    public record Antwort(
        bool Ok,
        int HttpStatus,
        long DauerMs,
        WebStampSoap.Fault? Fault,
        string? Fehler,
        XElement? Resultat);

    public async Task<Antwort> RufeAsync(string url, string methode, string envelope, CancellationToken ct)
    {
        var uhr = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "text/xml"),
            };
            req.Headers.TryAddWithoutValidation("SOAPAction", WebStampSoap.SoapAction(methode));
            using var res = await _http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            uhr.Stop();

            XDocument doc;
            try { doc = XDocument.Parse(body); }
            catch (XmlException)
            {
                _log.LogWarning("[WebStamp] {Methode}: keine XML-Antwort (HTTP {Status})", methode, (int)res.StatusCode);
                return new Antwort(false, (int)res.StatusCode, uhr.ElapsedMilliseconds, null,
                    $"Keine gültige Antwort der Post (HTTP {(int)res.StatusCode}).", null);
            }

            var fault = WebStampSoap.LiesFault(doc);
            if (fault != null)
            {
                _log.LogInformation("[WebStamp] {Methode}: Fault {Nr} {Text} (request {Req})",
                    methode, fault.FehlerNummer, fault.Text, fault.RequestId);
                return new Antwort(false, (int)res.StatusCode, uhr.ElapsedMilliseconds, fault, fault.Text, null);
            }

            var resultat = WebStampSoap.LiesResultat(doc, methode);
            if (resultat == null)
                return new Antwort(false, (int)res.StatusCode, uhr.ElapsedMilliseconds, null,
                    "Die Antwort der Post enthält kein Resultat.", null);
            return new Antwort(true, (int)res.StatusCode, uhr.ElapsedMilliseconds, null, null, resultat);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            uhr.Stop();
            _log.LogWarning(ex, "[WebStamp] {Methode}: Verbindung fehlgeschlagen", methode);
            return new Antwort(false, 0, uhr.ElapsedMilliseconds, null,
                ex is TaskCanceledException ? "Zeitüberschreitung — die Post hat nicht geantwortet."
                                            : "Verbindung zur Post fehlgeschlagen: " + ex.Message, null);
        }
    }
}

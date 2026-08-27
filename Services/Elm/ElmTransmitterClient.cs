using System.Text;
using System.Xml.Linq;

namespace HrSystem.Services.Elm;

/// <summary>
/// Swissdec-ELM-6.0-Transmitter — Etappe E1 (Walter 27.08.2026,
/// docs/swissdec-elm6-konzept.md): Ping + CheckInteroperability gegen den
/// Distributor bzw. den Refapps Receiver der Testinfrastruktur.
///
/// Technik laut «Richtlinien für Lohndatentransmitter» + WSDL
/// (docs/swissdec/Transmitter_Richtlinien/schema):
///   • SOAP 1.1, document/literal, SOAPAction leer
///   • Ping = UserAgent + SystemDateTime (unsigniert/unverschlüsselt)
///   • CheckInteroperability = UserAgent + UmlautString + zwei Operanden
///     + SystemDateTime (testet Encoding + Zahlformat auf dem ganzen Weg)
/// WICHTIG (Richtlinien Kap. 4): Ping wird NUR manuell ausgelöst — nie
/// automatisiert/zyklisch aufrufen.
/// </summary>
public class ElmTransmitterClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(40) };

    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Sdst = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration:service:types";
    private static readonly XNamespace Ep   = "urn:ch:swissdec:basis:v1:20260306:components";

    public record ElmCallResult(bool Ok, int HttpStatus, long DauerMs, string RequestXml, string ResponseXml, string? Error);

    /// <summary>UserAgent gemäss UserAgentType (alle Felder Pflicht).</summary>
    private static XElement UserAgent() => new(Ep + "UserAgent",
        new XElement(Ep + "Producer", "Schaub Restaurants GmbH"),
        new XElement(Ep + "Name", "OneCrew"),
        new XElement(Ep + "Version", "2026.08"),
        new XElement(Ep + "StandardVersion", "6.0"),
        new XElement(Ep + "Certificate", "n/a"));

    private static string Envelope(XElement body)
    {
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soapenv", Soap),
                new XAttribute(XNamespace.Xmlns + "sdst", Sdst),
                new XAttribute(XNamespace.Xmlns + "ep", Ep),
                new XElement(Soap + "Header"),
                new XElement(Soap + "Body", body)));
        return doc.Declaration + Environment.NewLine + doc.ToString();
    }

    private static async Task<ElmCallResult> PostAsync(string url, string envelope, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");
            req.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");
            using var res = await _http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            sw.Stop();
            string pretty = body;
            try { pretty = XDocument.Parse(body).ToString(); } catch { /* Rohtext lassen */ }
            return new ElmCallResult(res.IsSuccessStatusCode, (int)res.StatusCode, sw.ElapsedMilliseconds,
                envelope, pretty, res.IsSuccessStatusCode ? null : $"HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ElmCallResult(false, 0, sw.ElapsedMilliseconds, envelope, "",
                ex.GetBaseException().Message);
        }
    }

    /// <summary>Erreichbarkeits-Test (UC018) — Zeitvergleich Transmitter/Distributor.</summary>
    public Task<ElmCallResult> PingAsync(string url, CancellationToken ct = default)
    {
        var body = new XElement(Sdst + "Ping",
            UserAgent(),
            new XElement(Ep + "SystemDateTime",
                DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")));
        return PostAsync(url, Envelope(body), ct);
    }

    /// <summary>
    /// Interoperabilitäts-Test: Umlaute (Encoding) + zwei Beträge, die der
    /// Empfänger verarbeitet zurückgibt — beweist die ganze SOAP-Strecke.
    /// </summary>
    public Task<ElmCallResult> CheckInteroperabilityAsync(string url, CancellationToken ct = default)
    {
        var body = new XElement(Sdst + "CheckInteroperability",
            UserAgent(),
            // Vorgegebene Testreihe aus dem XSD-Kommentar («use following
            // UmlautString») — prüft das Encoding von Sonderzeichen.
            new XElement(Ep + "UmlautString", "ÄËÖÜÁÉÓÚÀÈÒÙÂÊÔÛ"),
            new XElement(Ep + "FirstOperand", "1234.55"),
            new XElement(Ep + "SecondOperand", "8765.40"),
            new XElement(Ep + "SystemDateTime",
                DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")));
        return PostAsync(url, Envelope(body), ct);
    }
}

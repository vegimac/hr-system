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

    public record ElmCallResult(bool Ok, int HttpStatus, long DauerMs, string RequestXml, string ResponseXml, string? Error)
    {
        /// <summary>Systemzeit des Empfängers aus der Antwort (NULL = keine gelesen).</summary>
        public DateTimeOffset? DistributorZeit { get; init; }
        /// <summary>Unsere Systemzeit im Moment des Vergleichs.</summary>
        public DateTimeOffset? LokaleZeit { get; init; }
        /// <summary>Lokale Zeit minus Empfängerzeit in Sekunden (positiv = wir gehen vor).</summary>
        public double? DiffSekunden { get; init; }
        /// <summary>Foundation-Test F01_03: Abweichung über einer Minute.</summary>
        public bool ZeitAbweichung => DiffSekunden.HasValue && Math.Abs(DiffSekunden.Value) > ZeitToleranzSekunden;

        /// <summary>
        /// Simulierter Zeitversatz dieses Aufrufs in Sekunden (0 = normal).
        /// Nur für den Foundation-Test F01_03 — siehe <see cref="PingAsync"/>.
        /// </summary>
        public int VersatzSekunden { get; init; }

        /// <summary>SOAP-Fault-Code aus der Antwort, z.B. «Client.security».</summary>
        public string? FaultCode { get; init; }
        /// <summary>Klartext des Faults, z.B. «security requirements not met».</summary>
        public string? FaultText { get; init; }
    }

    /// <summary>
    /// SOAP-Fault aus der Antwort lesen. Ein abgewiesener Aufruf kommt mit HTTP 500
    /// und einem Fault-Element — der Grund steht dort im Klartext und gehört auf den
    /// Bildschirm, statt nur «HTTP 500» zu zeigen (Walter 24.09.2026).
    /// Deckt SOAP 1.1 (faultcode/faultstring) und 1.2 (Code/Value, Reason/Text) ab.
    /// </summary>
    public static ElmCallResult MitFault(ElmCallResult r)
    {
        if (string.IsNullOrWhiteSpace(r.ResponseXml)) return r;
        try
        {
            var doc = XDocument.Parse(r.ResponseXml);
            var fault = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault");
            if (fault == null) return r;
            string? Wert(params string[] namen) => fault.Descendants()
                .FirstOrDefault(e => namen.Contains(e.Name.LocalName) && !e.HasElements)?.Value.Trim();
            var code = Wert("faultcode", "Value");
            var text = Wert("faultstring", "Text");
            if (code == null && text == null) return r;
            return r with { FaultCode = code, FaultText = text };
        }
        catch { return r; }
    }

    /// <summary>
    /// Toleranz für den Systemzeit-Vergleich (Foundation-Test F01_03, Walter 24.09.2026):
    /// «Bei einer Abweichung &gt;1 Minute wird der Zeitunterschied in Form einer Fehlermeldung
    /// dargestellt.»
    /// </summary>
    public const int ZeitToleranzSekunden = 60;

    /// <summary>
    /// Systemzeit des Empfängers aus der Ping-Antwort lesen und mit unserer
    /// vergleichen. Die Antwort führt &lt;SystemDateTime&gt; im Basis-Namensraum
    /// (dort als Default-Namensraum deklariert) — wir suchen den lokalen Namen,
    /// damit auch eine abweichende Präfix-Schreibweise gefunden wird.
    /// Verglichen wird über <see cref="DateTimeOffset"/>, also inklusive
    /// Zeitzonen-Versatz: «10:49+02:00» und «08:49Z» sind derselbe Moment.
    /// </summary>
    /// <param name="versatzSekunden">
    /// Simulierte Verstellung unserer Serveruhr (Foundation-Test F01_03, Walter 24.09.2026).
    /// Der Wert gilt für die GESENDETE Zeit UND für die Vergleichsbasis — nur so verhält sich
    /// das Programm wie mit einer echt falsch gehenden Uhr und zeigt ab 60 Sekunden den Fehler.
    /// 0 = normaler Betrieb.
    /// </param>
    public static ElmCallResult MitZeitvergleich(ElmCallResult r, int versatzSekunden = 0)
    {
        if (string.IsNullOrWhiteSpace(r.ResponseXml)) return r with { VersatzSekunden = versatzSekunden };
        try
        {
            var doc = XDocument.Parse(r.ResponseXml);
            var el = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "SystemDateTime");
            if (el == null) return r with { VersatzSekunden = versatzSekunden };
            if (!DateTimeOffset.TryParse(el.Value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var fern))
                return r with { VersatzSekunden = versatzSekunden };
            var hier = DateTimeOffset.Now.AddSeconds(versatzSekunden);
            return r with
            {
                DistributorZeit = fern,
                LokaleZeit      = hier,
                DiffSekunden    = Math.Round((hier - fern).TotalSeconds, 1),
                VersatzSekunden = versatzSekunden,
            };
        }
        catch { return r with { VersatzSekunden = versatzSekunden }; }   // unlesbare Antwort ändert nichts am Ergebnis
    }

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

    /// <summary>
    /// Erreichbarkeits-Test (UC018) — Foundation F01_02 «Erreichbarkeit» und
    /// F01_03 «Systemzeit»: die Antwort wird gleich auf den Zeitunterschied
    /// zwischen Empfänger und uns geprüft.
    /// </summary>
    public async Task<ElmCallResult> PingAsync(string url, int versatzSekunden = 0, CancellationToken ct = default)
    {
        var body = new XElement(Sdst + "Ping",
            UserAgent(),
            new XElement(Ep + "SystemDateTime", UnsereZeit(versatzSekunden)));
        return MitFault(MitZeitvergleich(await PostAsync(url, Envelope(body), ct), versatzSekunden));
    }

    /// <summary>Unsere Systemzeit im Swissdec-Format, optional simuliert verstellt.</summary>
    private static string UnsereZeit(int versatzSekunden) =>
        DateTime.Now.AddSeconds(versatzSekunden).ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");

    /// <summary>
    /// Interoperabilitäts-Test: Umlaute (Encoding) + zwei Beträge, die der
    /// Empfänger verarbeitet zurückgibt — beweist die ganze SOAP-Strecke.
    /// </summary>
    public async Task<ElmCallResult> CheckInteroperabilityAsync(string url, int versatzSekunden = 0, CancellationToken ct = default)
    {
        var body = new XElement(Sdst + "CheckInteroperability",
            UserAgent(),
            // Vorgegebene Testreihe aus dem XSD-Kommentar («use following
            // UmlautString») — prüft das Encoding von Sonderzeichen.
            new XElement(Ep + "UmlautString", "ÄËÖÜÁÉÓÚÀÈÒÙÂÊÔÛ"),
            new XElement(Ep + "FirstOperand", "1234.55"),
            new XElement(Ep + "SecondOperand", "8765.40"),
            new XElement(Ep + "SystemDateTime", UnsereZeit(versatzSekunden)));
        return MitFault(MitZeitvergleich(await PostAsync(url, Envelope(body), ct), versatzSekunden));
    }
}

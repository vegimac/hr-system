using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
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
    /// <summary>
    /// Nachweis der Transportsicherheit (Foundation-Test F02_01, Walter 24.09.2026):
    /// welches TLS ausgehandelt wurde, mit welcher Chiffre und welchem Serverzertifikat.
    /// </summary>
    public record TlsInfo(string Protokoll, string Chiffre, string Zertifikat, string Aussteller, DateTime GueltigBis);

    /// <summary>Details der zuletzt aufgebauten Verbindung im aktuellen Aufruf-Kontext.</summary>
    private static readonly AsyncLocal<TlsInfo?> _tls = new();

    /// <summary>
    /// Eigener Handler, damit wir den Sicherheitsnachweis führen können:
    ///  • `SslOptions` erlaubt ausschliesslich TLS 1.2 und 1.3 — ältere, gebrochene
    ///    Versionen (SSL 3, TLS 1.0/1.1) sind damit ausgeschlossen.
    ///  • `PlaintextStreamFilter` läuft NACH dem Handshake und bekommt den fertigen
    ///    `SslStream` — dort lesen wir Protokoll, Chiffre und Serverzertifikat ab.
    ///  • `PooledConnectionLifetime = 0` erzwingt für jeden manuellen Test eine frische
    ///    Verbindung, sonst käme beim zweiten Klick eine wiederverwendete ohne Handshake
    ///    (und damit ohne Nachweis). Bei zwei Aufrufen pro Tag ist das unproblematisch.
    /// </summary>
    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.Zero,
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12
                                | System.Security.Authentication.SslProtocols.Tls13,
        },
        PlaintextStreamFilter = (ctx, _) =>
        {
            if (ctx.PlaintextStream is System.Net.Security.SslStream ssl)
            {
                var zert = ssl.RemoteCertificate as System.Security.Cryptography.X509Certificates.X509Certificate2
                           ?? (ssl.RemoteCertificate != null
                               ? new System.Security.Cryptography.X509Certificates.X509Certificate2(ssl.RemoteCertificate)
                               : null);
                _tls.Value = new TlsInfo(
                    ssl.SslProtocol.ToString(),
                    ssl.NegotiatedCipherSuite.ToString(),
                    zert?.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false) ?? "—",
                    zert?.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, true) ?? "—",
                    zert?.NotAfter ?? default);
            }
            return ValueTask.FromResult(ctx.PlaintextStream);
        },
    })
    { Timeout = TimeSpan.FromSeconds(40) };

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

        /// <summary>Ausgehandelte Transportsicherheit (Foundation F02_01).</summary>
        public TlsInfo? Tls { get; init; }

        /// <summary>SOAP-Fault-Code aus der Antwort, z.B. «Client.security».</summary>
        public string? FaultCode { get; init; }
        /// <summary>Klartext des Faults, z.B. «security requirements not met».</summary>
        public string? FaultText { get; init; }

        /// <summary>Nachgerechnete Interoperabilitäts-Antwort (Foundation F03); NULL = keine geprüft.</summary>
        public ElmInterop.Befund? Interop { get; init; }

        /// <summary>WS-Security-Prüfung der Antwort (F02 / F07); NULL = nicht geprüft.</summary>
        public ElmWsSecurity.PruefErgebnis? Security { get; init; }
    }

    /// <summary>
    /// SOAP senden mit WS-Security: signieren mit ERP-Zertifikat, optional
    /// verschlüsseln mit Empfängerzertifikat (Foundation F02/F07). Antwort wird
    /// entschlüsselt und die Signatur geprüft, sofern unser Zertifikat da ist.
    /// </summary>
    public async Task<ElmCallResult> PostGesichertAsync(
        string url, XElement body,
        X509Certificate2 erpZertifikat,
        X509Certificate2? empfaengerZertifikat,
        CancellationToken ct = default)
    {
        var envelopeXml = Envelope(body);
        var doc = new XmlDocument { PreserveWhitespace = true };
        // Einmal serialisieren und neu laden — sonst kanonisiert die Signatur anders
        // als beim Empfänger (Falle aus ElmWsSecurity-Kommentar).
        doc.LoadXml(XDocument.Parse(envelopeXml).ToString(SaveOptions.DisableFormatting));

        ElmWsSecurity.Signiere(doc, erpZertifikat);
        if (empfaengerZertifikat != null)
            ElmWsSecurity.Verschluessele(doc, empfaengerZertifikat);

        var gesichert = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + doc.OuterXml;
        var r = await PostAsync(url, gesichert, ct);
        r = MitFault(r);

        if (string.IsNullOrWhiteSpace(r.ResponseXml)) return r;
        try
        {
            var antw = new XmlDocument { PreserveWhitespace = true };
            antw.LoadXml(r.ResponseXml);
            // Wenn wir nicht verschlüsselt haben, verlangen wir auch keine Verschlüsselung zurück
            // (sonst blockiert der erste Register-Versuch ohne Empfängerzertifikat).
            var verschlPflicht = empfaengerZertifikat != null;
            var pruef = ElmWsSecurity.Pruefe(antw, erpZertifikat,
                verschluesselungPflicht: verschlPflicht,
                signaturPflicht: true);
            var klartext = antw.OuterXml;
            try { klartext = XDocument.Parse(klartext).ToString(); } catch { /* roh lassen */ }
            return r with { ResponseXml = klartext, Security = pruef };
        }
        catch
        {
            return r;
        }
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
        _tls.Value = null;   // Nachweis dieses Aufrufs, nicht des vorherigen
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
                envelope, pretty, res.IsSuccessStatusCode ? null : $"HTTP {(int)res.StatusCode} {res.ReasonPhrase}")
                { Tls = _tls.Value };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ElmCallResult(false, 0, sw.ElapsedMilliseconds, envelope, "",
                ex.GetBaseException().Message) { Tls = _tls.Value };
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
    public async Task<ElmCallResult> CheckInteroperabilityAsync(string url, decimal zweiterOperand,
        int versatzSekunden = 0, CancellationToken ct = default)
    {
        var body = new XElement(Sdst + "CheckInteroperability",
            UserAgent(),
            // Zeichenkette und erste Zahl sind FEST vorgegeben (Foundation F03_01) —
            // sie dürfen nie aus der Oberfläche kommen.
            new XElement(Ep + "UmlautString", ElmInterop.UmlautString),
            new XElement(Ep + "FirstOperand", ElmInterop.Betrag(ElmInterop.FirstOperand)),
            // Der zweite Operand ist wählbar (F03_02), aber immer mit zwei
            // Nachkommastellen formatiert (F03_03).
            new XElement(Ep + "SecondOperand", ElmInterop.Betrag(zweiterOperand)),
            new XElement(Ep + "SystemDateTime", UnsereZeit(versatzSekunden)));

        var r = MitFault(MitZeitvergleich(await PostAsync(url, Envelope(body), ct), versatzSekunden));
        // Die Antwort wird nachgerechnet, nicht geglaubt (F03_04/F03_05).
        return r with { Interop = ElmInterop.Pruefe(r.ResponseXml, zweiterOperand) };
    }
}

using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Linq;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Live gegen RefApps — nur lokal mit Netzwerk und ELMv6-.p12 in Downloads.
/// Standardmässig übersprungen (kein CI). Manuell: ELM_LIVE_PROBE=1 dotnet test --filter ElmLiveProbe.
/// </summary>
public class ElmLiveProbeTests
{
    [Fact]
    public async Task Live_CheckInterop_MitElm6Pfx_GegenRefApps()
    {
        if (Environment.GetEnvironmentVariable("ELM_LIVE_PROBE") != "1")
            return; // still grün im CI

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = Path.Combine(home, "Downloads", "Swissdec-ELMv6-Transmitter");
        var p12 = Path.Combine(dir, "SwissdecAllTransmittersTest_Test-ELM6-Transmitter.p12");
        var pwdPfad = Path.Combine(dir, "SwissdecAllTransmittersTest_Test-ELM6-Transmitter_privateKeyPassword.txt");
        Assert.True(File.Exists(p12), "ELMv6-.p12 fehlt in Downloads");
        var pwd = File.ReadAllText(pwdPfad).Trim();
        var empPfad = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "Assets", "Swissdec", "SwissdecDistributorELMv6Test.pem"));
        if (!File.Exists(empPfad))
            empPfad = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),
                "Assets", "Swissdec", "SwissdecDistributorELMv6Test.pem"));
        // ELM_LIVE_EMPF=receiver → klassisches RefApps-Receiver (erwartet Fault 110)
        if (Environment.GetEnvironmentVariable("ELM_LIVE_EMPF") == "receiver")
        {
            empPfad = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "Assets", "Swissdec", "RefApps-Receiver.cer"));
            if (!File.Exists(empPfad))
                empPfad = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),
                    "Assets", "Swissdec", "RefApps-Receiver.cer"));
        }
        Assert.True(File.Exists(empPfad), "Empfängerzertifikat fehlt: " + empPfad);

        var erp = X509CertificateLoader.LoadPkcs12FromFile(p12, pwd, X509KeyStorageFlags.Exportable);
        var emp = X509CertificateLoader.LoadCertificateFromFile(empPfad);
        Assert.True(erp.HasPrivateKey);
        Assert.Contains("All Transmitters Test", erp.Subject);

        XNamespace soap = ElmWsSecurity.NsSoap;
        XNamespace sdst = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration:service:types";
        XNamespace ep = "urn:ch:swissdec:basis:v1:20260306:components";

        var body = new XElement(sdst + "CheckInteroperability",
            new XElement(ep + "UserAgent",
                new XElement(ep + "Producer", "Schaub Restaurants GmbH"),
                new XElement(ep + "Name", "OneCrew"),
                new XElement(ep + "Version", "2026.09"),
                new XElement(ep + "StandardVersion", "6.0"),
                new XElement(ep + "Certificate", "n/a")),
            new XElement(ep + "UmlautString", ElmInterop.UmlautString),
            new XElement(ep + "FirstOperand", ElmInterop.Betrag(ElmInterop.FirstOperand)),
            new XElement(ep + "SecondOperand", ElmInterop.Betrag(0.01m)),
            new XElement(ep + "SystemDateTime", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")),
            new XElement(ep + "MonitoringID", "schaub"));

        var envelope = new XElement(soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soap", soap),
            new XElement(soap + "Header"),
            new XElement(soap + "Body", body));

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(envelope.ToString(SaveOptions.DisableFormatting));
        ElmWsSecurity.Signiere(doc, erp);
        // Probe: mit/ohne Verschlüsselung — ELM_LIVE_PROBE_PLAIN=1 = nur Signatur
        var plain = Environment.GetEnvironmentVariable("ELM_LIVE_PROBE_PLAIN") == "1";
        if (!plain)
            ElmWsSecurity.Verschluessele(doc, emp);

        var url = Environment.GetEnvironmentVariable("ELM_LIVE_URL") ?? ElmEndpunkte.TestUrl;
        Console.WriteLine($"URL={url} plain={plain}");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        using var content = new StringContent(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + doc.OuterXml,
            System.Text.Encoding.UTF8, "text/xml");
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        req.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");
        var resp = await http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();

        // Antwort ggf. entschlüsseln (RefApps verschlüsselt mit unserem TX-Public-Key).
        var klar = text;
        if (text.Contains("EncryptedData"))
        {
            try
            {
                var antw = new XmlDocument { PreserveWhitespace = true };
                antw.LoadXml(text);
                var pruef = ElmWsSecurity.Pruefe(antw, erp, verschluesselungPflicht: false, signaturPflicht: false);
                klar = antw.OuterXml + "\n<!-- pruef: " + pruef.Befund + " " + pruef.Meldung + " -->";
            }
            catch (Exception ex)
            {
                klar = text[..Math.Min(800, text.Length)] + "\n<!-- decrypt fail: " + ex.Message + " -->";
            }
        }

        var code = "";
        var m = System.Text.RegularExpressions.Regex.Match(klar, @"DescriptionCode>(\d+)<");
        if (m.Success) code = m.Groups[1].Value;
        var fault = System.Text.RegularExpressions.Regex.Match(klar, @"faultstring>([^<]+)<").Groups[1].Value;
        var ok = klar.Contains("CheckInteroperabilityResponse") || klar.Contains("UmlautString");

        Assert.Fail(
            $"HTTP {(int)resp.StatusCode} code={code} fault={fault} ok={ok}\n"
            + klar[..Math.Min(2500, klar.Length)]);
    }
}

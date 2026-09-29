using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Foundation F02_08 «Falsches Zertifikat» (Walter 29.09.2026): RefApps signiert die Antwort
/// mit einem unbekannten Schlüssel und legt das passende Zertifikat bei. Die Signatur ist
/// rechnerisch korrekt — erkannt wird der Fall nur über die Vertrauensliste.
/// </summary>
public class ElmVertrauenTests
{
    private static X509Certificate2 MitSchluessel(X509Certificate2 z) =>
        X509CertificateLoader.LoadPkcs12(z.Export(X509ContentType.Pfx, "t"), "t", X509KeyStorageFlags.Exportable);

    private static X509Certificate2 Selbstsigniert(string name)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return MitSchluessel(req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1)));
    }

    private static X509Certificate2 Stelle(string name)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        return MitSchluessel(req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-2), DateTimeOffset.Now.AddYears(2)));
    }

    private static X509Certificate2 AusgestelltVon(X509Certificate2 stelle, string name)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var serial = RandomNumberGenerator.GetBytes(8);
        using var ohneSchluessel = req.Create(stelle, DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1), serial);
        return MitSchluessel(ohneSchluessel.CopyWithPrivateKey(rsa));
    }

    private static XmlDocument SignierteAntwort(X509Certificate2 signierer)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml($"""
            <soap:Envelope xmlns:soap="{ElmWsSecurity.NsSoap}"><soap:Header /><soap:Body>
              <CheckInteroperabilityResponse xmlns="urn:ch:swissdec:elm:v6:20260306:salarydeclaration:service:types">
                <UmlautString>äëöü</UmlautString>
              </CheckInteroperabilityResponse>
            </soap:Body></soap:Envelope>
            """);
        ElmWsSecurity.Signiere(doc, signierer);
        var neu = new XmlDocument { PreserveWhitespace = true };
        neu.LoadXml(doc.OuterXml);
        return neu;
    }

    private static X509Certificate2 Distributor() =>
        X509CertificateLoader.LoadCertificateFromFile(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Swissdec", "SwissdecDistributorELMv6Test.cer"));

    [Fact]
    public void UnbekannterSchluessel_MitPassendemZertifikat_WirdAbgelehnt()
    {
        var fremd = Selbstsigniert("CN=Distributor ELMv6 Test, O=Swissdec, C=CH");
        var r = ElmWsSecurity.PruefeMitSchluesseln(SignierteAntwort(fremd), Array.Empty<X509Certificate2>(),
            verschluesselungPflicht: false, vertrauensliste: new[] { Distributor() });

        Assert.Equal(ElmWsSecurity.Befund.ZertifikatNichtVertrauenswuerdig, r.Befund);
        Assert.Contains("nicht vertrauenswürdig", r.Meldung);
    }

    [Fact]
    public void OhneVertrauensliste_BleibtAltesVerhalten()
    {
        var fremd = Selbstsigniert("CN=Irgendwer");
        var r = ElmWsSecurity.PruefeMitSchluesseln(SignierteAntwort(fremd), Array.Empty<X509Certificate2>(),
            verschluesselungPflicht: false);
        Assert.True(r.Ok, r.Meldung);
    }

    [Fact]
    public void BekanntesZertifikat_WirdAkzeptiert()
    {
        var bekannt = Selbstsigniert("CN=Distributor Probe");
        var liste = new[] { Distributor(), X509CertificateLoader.LoadCertificate(bekannt.RawData) };
        var r = ElmWsSecurity.PruefeMitSchluesseln(SignierteAntwort(bekannt), Array.Empty<X509Certificate2>(),
            verschluesselungPflicht: false, vertrauensliste: liste);
        Assert.True(r.Ok, r.Meldung);
    }

    [Fact]
    public void VonHinterlegterStelleAusgestellt_WirdAkzeptiert_FremdeStelleNicht()
    {
        var stelle = Stelle("CN=Test ELM Transmitter CA, O=Swissdec, C=CH");
        var andere = Stelle("CN=Fremde Stelle");
        var signierer = AusgestelltVon(stelle, "CN=Distributor Produktiv, O=Swissdec, C=CH");
        var nurOeffentlich = new[] { X509CertificateLoader.LoadCertificate(stelle.RawData) };

        Assert.True(ElmWsSecurity.IstVertrauenswuerdig(X509CertificateLoader.LoadCertificate(signierer.RawData), nurOeffentlich));
        Assert.False(ElmWsSecurity.IstVertrauenswuerdig(X509CertificateLoader.LoadCertificate(signierer.RawData),
            new[] { X509CertificateLoader.LoadCertificate(andere.RawData) }));

        var r = ElmWsSecurity.PruefeMitSchluesseln(SignierteAntwort(signierer), Array.Empty<X509Certificate2>(),
            verschluesselungPflicht: false, vertrauensliste: nurOeffentlich);
        Assert.True(r.Ok, r.Meldung);
    }

    [Fact]
    public void Vertrauensliste_EnthaeltDistributorUndRefAppReceiver_UndOrdnerVertrauen()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "elm-vertrauen-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ElmZertifikatStore(new MiniConfig(tmp));
            var liste = store.LadeVertrauensliste();
            Assert.Contains(liste, z => z.Subject.Contains("Distributor ELMv6 Test"));
            Assert.Contains(liste, z => z.Subject.Contains("RefApp Receiver"));

            var zusatz = Selbstsigniert("CN=Produktiv Distributor");
            Directory.CreateDirectory(Path.Combine(tmp, "vertrauen"));
            File.WriteAllBytes(Path.Combine(tmp, "vertrauen", "prod.cer"), zusatz.RawData);
            Assert.Contains(store.LadeVertrauensliste(), z => z.Thumbprint == zusatz.Thumbprint);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    private sealed class MiniConfig : Microsoft.Extensions.Configuration.IConfiguration
    {
        private readonly string _pfad;
        public MiniConfig(string pfad) => _pfad = pfad;
        public string? this[string key]
        {
            get => key == "Swissdec:CertStoragePath" ? _pfad : null;
            set { }
        }
        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() =>
            Array.Empty<Microsoft.Extensions.Configuration.IConfigurationSection>();
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => new NoopToken();
        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) =>
            throw new NotSupportedException();

        private sealed class NoopToken : Microsoft.Extensions.Primitives.IChangeToken
        {
            public bool HasChanged => false;
            public bool ActiveChangeCallbacks => false;
            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => new Leer();
            private sealed class Leer : IDisposable { public void Dispose() { } }
        }
    }
}

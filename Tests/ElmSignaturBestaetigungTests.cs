using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Foundation F04_02 — die SignatureConfirmation der Antwort muss genau unsere
/// Signatur(en) bestätigen und von der Antwort-Signatur abgedeckt sein (Walter 29.09.2026).
/// </summary>
public class ElmSignaturBestaetigungTests
{
    private const string NsWsse11 = "http://docs.oasis-open.org/wss/oasis-wss-wssecurity-secext-1.1.xsd";

    private static X509Certificate2 Zertifikat(string name)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var zert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(zert.Export(X509ContentType.Pfx, "test"), "test",
            X509KeyStorageFlags.Exportable);
    }

    private static string Anfrage(params string[] zertifikate)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml($"""
            <soap:Envelope xmlns:soap="{ElmWsSecurity.NsSoap}">
              <soap:Header />
              <soap:Body><Ping xmlns="urn:test" /></soap:Body>
            </soap:Envelope>
            """);
        if (zertifikate.Length > 0)
            ElmWsSecurity.Signiere(doc, zertifikate.Select(Zertifikat).ToArray());
        return doc.OuterXml;
    }

    private static List<string> Werte(string anfrage)
    {
        var d = new XmlDocument { PreserveWhitespace = true };
        d.LoadXml(anfrage);
        return ElmSignaturBestaetigung.SignaturWerte(d);
    }

    /// <summary>Antwort wie vom Distributor: SignatureConfirmation im Header, SignedInfo verweist auf die Ids.</summary>
    private static string Antwort(IEnumerable<string> werte, IEnumerable<string>? signierteIds = null)
    {
        var liste = werte.ToList();
        var sc = string.Concat(liste.Select((w, i) =>
            $"<wsse11:SignatureConfirmation wsu:Id=\"SC-{i + 1}\" Value=\"{w}\" />"));
        var ids = signierteIds ?? liste.Select((_, i) => $"SC-{i + 1}");
        var refs = string.Concat(ids.Select(id => $"<ds:Reference URI=\"#{id}\" />"));
        return $"""
            <soap:Envelope xmlns:soap="{ElmWsSecurity.NsSoap}" xmlns:wsse="{ElmWsSecurity.NsWsse}"
                           xmlns:wsse11="{NsWsse11}" xmlns:wsu="{ElmWsSecurity.NsWsu}" xmlns:ds="{ElmWsSecurity.NsDs}">
              <soap:Header>
                <wsse:Security>
                  {sc}
                  <ds:Signature><ds:SignedInfo><ds:Reference URI="#id-body" />{refs}</ds:SignedInfo>
                    <ds:SignatureValue>QU5UV09SVA==</ds:SignatureValue></ds:Signature>
                </wsse:Security>
              </soap:Header>
              <soap:Body wsu:Id="id-body"><PingResponse xmlns="urn:test" /></soap:Body>
            </soap:Envelope>
            """;
    }

    [Fact]
    public void EinfacheSignatur_Bestaetigt()
    {
        var anfrage = Anfrage("CN=ERP");
        var r = ElmSignaturBestaetigung.Pruefe(anfrage, Antwort(Werte(anfrage)));
        Assert.Equal(ElmSignaturBestaetigung.Stand.Bestaetigt, r.Stand);
        Assert.True(r.Ok);
        Assert.Equal(1, r.Erwartet);
        Assert.Equal(1, r.Passend);
    }

    [Fact]
    public void Doppelsignatur_BeideBestaetigt()
    {
        var anfrage = Anfrage("CN=ERP", "CN=SUA");
        var werte = Werte(anfrage);
        Assert.Equal(2, werte.Count);
        var r = ElmSignaturBestaetigung.Pruefe(anfrage, Antwort(werte));
        Assert.True(r.Ok);
        Assert.Contains("ERP + SUA", r.Meldung);
    }

    [Fact]
    public void Doppelsignatur_NurEineBestaetigt_Abweichend()
    {
        var anfrage = Anfrage("CN=ERP", "CN=SUA");
        var r = ElmSignaturBestaetigung.Pruefe(anfrage, Antwort(Werte(anfrage).Take(1)));
        Assert.Equal(ElmSignaturBestaetigung.Stand.Abweichend, r.Stand);
        Assert.Equal(1, r.Passend);
        Assert.Equal(2, r.Erwartet);
    }

    [Fact]
    public void FremderWert_Abweichend()
    {
        var anfrage = Anfrage("CN=ERP");
        var andere = Anfrage("CN=ERP");
        var r = ElmSignaturBestaetigung.Pruefe(anfrage, Antwort(Werte(andere)));
        Assert.Equal(ElmSignaturBestaetigung.Stand.Abweichend, r.Stand);
        Assert.Equal(0, r.Passend);
        Assert.Contains("fremde", r.Meldung);
    }

    [Fact]
    public void ZusaetzlicherFremderWert_Abweichend()
    {
        var anfrage = Anfrage("CN=ERP");
        var r = ElmSignaturBestaetigung.Pruefe(anfrage, Antwort(Werte(anfrage).Append("RlJFTUQ=")));
        Assert.Equal(ElmSignaturBestaetigung.Stand.Abweichend, r.Stand);
        Assert.Equal(1, r.Passend);
    }

    [Fact]
    public void OhneSignatureConfirmation_Fehlt()
    {
        var anfrage = Anfrage("CN=ERP");
        var r = ElmSignaturBestaetigung.Pruefe(anfrage, Antwort(Array.Empty<string>()));
        Assert.Equal(ElmSignaturBestaetigung.Stand.Fehlt, r.Stand);
        Assert.False(r.Ok);
    }

    [Fact]
    public void NichtVonAntwortSigniert_NichtSigniert()
    {
        var anfrage = Anfrage("CN=ERP");
        var r = ElmSignaturBestaetigung.Pruefe(anfrage, Antwort(Werte(anfrage), signierteIds: Array.Empty<string>()));
        Assert.Equal(ElmSignaturBestaetigung.Stand.NichtSigniert, r.Stand);
    }

    [Fact]
    public void UnsignierteAnfrage_NichtVerlangt()
    {
        var r = ElmSignaturBestaetigung.Pruefe(Anfrage(), Antwort(Array.Empty<string>()));
        Assert.Equal(ElmSignaturBestaetigung.Stand.NichtVerlangt, r.Stand);
        Assert.Equal("NichtVerlangt", r.Art);
    }

    [Fact]
    public void ZeilenumbruchImWert_ZaehltNicht()
    {
        var anfrage = Anfrage("CN=ERP");
        var umgebrochen = Werte(anfrage).Select(w => w[..20] + "\n  " + w[20..]);
        var r = ElmSignaturBestaetigung.Pruefe(anfrage, Antwort(umgebrochen));
        Assert.True(r.Ok);
    }

    [Fact]
    public void UnlesbareAntwort_Fehlt()
    {
        var r = ElmSignaturBestaetigung.Pruefe(Anfrage("CN=ERP"), "<kaputt");
        Assert.Equal(ElmSignaturBestaetigung.Stand.Fehlt, r.Stand);
    }

    [Fact]
    public void Archiv_AntwortFindetJuengsteAnfrageGleichenNamens()
    {
        var dir = Directory.CreateTempSubdirectory("elm-archiv-");
        try
        {
            foreach (var n in new[]
            {
                "20260929-220000-sua-register-request.xml",
                "20260929-220001-sua-register-response.xml",
                "20260929-221500-sua-register-request.xml",
                "20260929-221500-sua-register-response.xml",   // gleiche Sekunde
                "20260929-221600-sua-synchronize-request.xml",
                "20260929-230000-request-request.xml",          // Standardname
                "20260929-230001-request-response.xml",
                "20260929-231000-register-response.xml",        // «register» ≠ «sua-register»
            })
                File.WriteAllText(Path.Combine(dir.FullName, n), "");
            var alle = dir.GetFiles("*.xml").OrderBy(f => f.Name, StringComparer.Ordinal).ToList();

            Assert.Equal("20260929-220000-sua-register-request.xml",
                ElmZertifikatStore.ArchivAnfrageZu("20260929-220001-sua-register-response.xml", alle)?.Name);
            Assert.Equal("20260929-221500-sua-register-request.xml",
                ElmZertifikatStore.ArchivAnfrageZu("20260929-221500-sua-register-response.xml", alle)?.Name);
            Assert.Equal("20260929-230000-request-request.xml",
                ElmZertifikatStore.ArchivAnfrageZu("20260929-230001-request-response.xml", alle)?.Name);
            Assert.Null(ElmZertifikatStore.ArchivAnfrageZu("20260929-221600-sua-synchronize-request.xml", alle));
            Assert.Null(ElmZertifikatStore.ArchivAnfrageZu("20260929-215959-sua-register-response.xml", alle));
            Assert.Null(ElmZertifikatStore.ArchivAnfrageZu("20260929-231000-register-response.xml", alle));
        }
        finally { dir.Delete(recursive: true); }
    }
}

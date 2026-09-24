using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// WS-Security für Swissdec (Foundation-Test **F02**, Walter 24.09.2026).
///
/// Die Prüfpunkte F02_02 bis F02_11 verlangen, dass wir signieren, verschlüsseln und
/// jede Verfehlung der Gegenseite **erkennen und melden**. Genau das prüfen diese Tests —
/// mit selbst erzeugten Zertifikaten, damit sie ohne das Swissdec-Zertifikat laufen.
/// Die Fehlerfälle bauen wir hier nach (Signatur verfälschen, Verschlüsselung weglassen,
/// fremdes Zertifikat), weil sie sich sonst nur mit Swissdecs RefApps-Einstellungen
/// auslösen liessen.
/// </summary>
public class ElmWsSecurityTests
{
    /// <summary>Selbst erzeugtes Testzertifikat mit privatem Schlüssel.</summary>
    private static X509Certificate2 Zertifikat(string name = "CN=OneCrew Test")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var zert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
        // Auf macOS/Linux muss der Schlüssel über PKCS#12 wandern, damit er exportierbar bleibt.
        return X509CertificateLoader.LoadPkcs12(zert.Export(X509ContentType.Pfx, "test"), "test",
            X509KeyStorageFlags.Exportable);
    }

    private static XmlDocument Nachricht(string inhalt = "Guten Tag") => Lade($"""
        <soap:Envelope xmlns:soap="{ElmWsSecurity.NsSoap}">
          <soap:Header />
          <soap:Body>
            <CheckInteroperability xmlns="urn:ch:swissdec:elm:v6:20260306:salarydeclaration:service:types">
              <UmlautString>{inhalt} ÄËÖÜ</UmlautString>
              <FirstOperand>1234.55</FirstOperand>
            </CheckInteroperability>
          </soap:Body>
        </soap:Envelope>
        """);

    private static XmlDocument Lade(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        return doc;
    }

    // ── F02_05 Signatur senden ───────────────────────────────────────────────

    [Fact]
    public void SignierteNachricht_HatAlleGefordertenTeile()
    {
        var doc = Nachricht();
        ElmWsSecurity.Signiere(doc, Zertifikat());
        var xml = doc.OuterXml;

        Assert.Contains("BinarySecurityToken", xml);          // Zertifikat reist mit
        Assert.Contains("SecurityTokenReference", xml);       // direkte Referenz darauf
        Assert.Contains("Timestamp", xml);
        Assert.Contains("Expires", xml);                      // Pflicht gegen Wiedereinspielen
        Assert.Contains("mustUnderstand=\"1\"", xml);
        Assert.Contains("xmldsig-more#rsa-sha256", xml);      // geforderte Algorithmen
        Assert.Contains("xmlenc#sha256", xml);
        Assert.Contains("xml-exc-c14n#", xml);
        // Body UND Timestamp sind referenziert.
        Assert.Equal(2, doc.GetElementsByTagName("Reference", ElmWsSecurity.NsDs).Count);
    }

    [Fact]
    public void EigeneSignatur_WirdAlsGueltigErkannt()
    {
        var zert = Zertifikat();
        var doc = Nachricht();
        ElmWsSecurity.Signiere(doc, zert);

        var r = ElmWsSecurity.Pruefe(Lade(doc.OuterXml), zert, verschluesselungPflicht: false);
        Assert.True(r.Ok, r.Meldung);
        Assert.Equal("OneCrew Test", r.Zertifikat);
    }

    // ── F02_06 / F02_11 Verfälschte Signatur ─────────────────────────────────

    [Fact]
    public void VerfaelschteNachricht_WirdZurueckgewiesen()
    {
        var zert = Zertifikat();
        var doc = Nachricht();
        ElmWsSecurity.Signiere(doc, zert);

        // Nach dem Signieren den Inhalt ändern — klassischer Manipulationsfall.
        var verfaelscht = doc.OuterXml.Replace("1234.55", "9999.99");

        var r = ElmWsSecurity.Pruefe(Lade(verfaelscht), zert, verschluesselungPflicht: false);
        Assert.Equal(ElmWsSecurity.Befund.SignaturUngueltig, r.Befund);
        Assert.Contains("nicht akzeptiert", r.Meldung);
    }

    [Fact]
    public void FremdesZertifikat_WirdAlsUngueltigErkannt()
    {
        // Signiert mit A, in der Antwort steckt aber das Zertifikat von B (F02_08).
        var a = Zertifikat("CN=Echter Absender");
        var b = Zertifikat("CN=Fremder Schluessel");
        var doc = Nachricht();
        ElmWsSecurity.Signiere(doc, a);
        var xml = doc.OuterXml.Replace(Convert.ToBase64String(a.RawData), Convert.ToBase64String(b.RawData));

        var r = ElmWsSecurity.Pruefe(Lade(xml), a, verschluesselungPflicht: false);
        Assert.Equal(ElmWsSecurity.Befund.SignaturUngueltig, r.Befund);
    }

    // ── F02_07 Unsignierte Antwort ───────────────────────────────────────────

    [Fact]
    public void FehlendeSignatur_WirdGemeldet()
    {
        var r = ElmWsSecurity.Pruefe(Nachricht(), Zertifikat(), verschluesselungPflicht: false);
        Assert.Equal(ElmWsSecurity.Befund.SignaturFehlt, r.Befund);
        Assert.Contains("nicht signiert", r.Meldung);
    }

    [Fact]
    public void FehlendeSignatur_IstInOrdnung_WennNichtVerlangt()
    {
        var r = ElmWsSecurity.Pruefe(Nachricht(), Zertifikat(),
            verschluesselungPflicht: false, signaturPflicht: false);
        Assert.True(r.Ok);
    }

    // ── F02_02 / F02_04 Verschlüsselung ──────────────────────────────────────

    [Fact]
    public void VerschluesselteNachricht_VerbirgtDenInhalt()
    {
        var zert = Zertifikat();
        var doc = Nachricht("Geheimer Lohn");
        ElmWsSecurity.Signiere(doc, zert);
        ElmWsSecurity.Verschluessele(doc, zert);
        var xml = doc.OuterXml;

        Assert.DoesNotContain("Geheimer Lohn", xml);
        Assert.DoesNotContain("1234.55", xml);
        Assert.Contains("EncryptedData", xml);
        Assert.Contains("EncryptedKey", xml);
        Assert.Contains("xmlenc#aes256-cbc", xml);   // geforderte Algorithmen
        Assert.Contains("rsa-oaep", xml);
    }

    [Fact]
    public void VerschluesseltUndSigniert_LaesstSichWiederPruefen()
    {
        var zert = Zertifikat();
        var doc = Nachricht("Geheimer Lohn");
        ElmWsSecurity.Signiere(doc, zert);
        ElmWsSecurity.Verschluessele(doc, zert);

        var empfangen = Lade(doc.OuterXml);
        var r = ElmWsSecurity.Pruefe(empfangen, zert);
        Assert.True(r.Ok, r.Meldung);
        // Nach dem Entschlüsseln steht der Klartext wieder da.
        Assert.Contains("Geheimer Lohn", empfangen.OuterXml);
    }

    [Fact]
    public void FehlendeVerschluesselung_WirdGemeldet()
    {
        var zert = Zertifikat();
        var doc = Nachricht();
        ElmWsSecurity.Signiere(doc, zert);

        var r = ElmWsSecurity.Pruefe(Lade(doc.OuterXml), zert, verschluesselungPflicht: true);
        Assert.Equal(ElmWsSecurity.Befund.VerschluesselungFehlt, r.Befund);
        Assert.Contains("unverschlüsselt", r.Meldung);
    }

    // ── F02_03 Verfälschte Verschlüsselung ───────────────────────────────────

    [Fact]
    public void UnlesbareVerschluesselung_WirdGemeldet()
    {
        var zert = Zertifikat();
        var doc = Nachricht();
        ElmWsSecurity.Signiere(doc, zert);
        ElmWsSecurity.Verschluessele(doc, zert);

        // Ein Zeichen im Chiffrat kippen — so wirkt «Tamper Encryption» der RefApps.
        var xml = doc.OuterXml;
        var ci = xml.IndexOf("<CipherValue", StringComparison.Ordinal);
        var start = xml.IndexOf('>', ci) + 1;
        xml = xml[..start] + (xml[start] == 'A' ? 'B' : 'A') + xml[(start + 1)..];

        var r = ElmWsSecurity.Pruefe(Lade(xml), zert);
        Assert.Equal(ElmWsSecurity.Befund.EntschluesselungFehlgeschlagen, r.Befund);
    }

    [Fact]
    public void VerschluesseltOhneSchluessel_MeldetKlartext()
    {
        var zert = Zertifikat();
        var doc = Nachricht();
        ElmWsSecurity.Signiere(doc, zert);
        ElmWsSecurity.Verschluessele(doc, zert);

        var r = ElmWsSecurity.Pruefe(Lade(doc.OuterXml), unserZertifikat: null);
        Assert.Equal(ElmWsSecurity.Befund.EntschluesselungFehlgeschlagen, r.Befund);
        Assert.Contains("kein privater Schlüssel", r.Meldung);
    }

    // ── Ping bleibt aussen vor ───────────────────────────────────────────────

    [Fact]
    public void PingBleibtUnsigniert_LautRichtlinie()
    {
        // Dokumentiert die Regel «ausser Ping»: eine unsignierte, unverschlüsselte
        // Antwort ist dort kein Fehler.
        var r = ElmWsSecurity.Pruefe(Nachricht(), null,
            verschluesselungPflicht: false, signaturPflicht: false);
        Assert.True(r.Ok);
    }
}

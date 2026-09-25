using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace HrSystem.Services.Elm;

/// <summary>
/// WS-Security für Swissdec-Übermittlungen (Foundation-Test F02, Walter 24.09.2026).
///
/// Grundlage: «Richtlinien für Swissdec-Übermittlungen: Sicherheit (Transmitter)»
/// (`docs/swissdec/SecurityTransmitter_d.pdf`) und UC015 der Technischen Spezifikation:
///
///  • Jede Operation **ausser Ping** wird signiert und verschlüsselt.
///  • Signiert werden mindestens **SOAP-Body und Timestamp**; der Timestamp hat immer
///    ein `Expires` (Schutz gegen Wiedereinspielen).
///  • Das Zertifikat reist als Base64-`BinarySecurityToken` mit und wird über eine
///    **direkte** `SecurityTokenReference` referenziert.
///  • `mustUnderstand="1"` auf dem Security-Element.
///  • Algorithmen: exklusive Kanonisierung, **RSA-SHA256**, Digest **SHA256**;
///    Schlüsseltransport **RSA-OAEP**, Nutzdaten **AES-256-CBC**.
///  • **Reihenfolge: zuerst signieren, dann verschlüsseln.**
///  • Antworten mit ungültiger Signatur **dürfen nicht akzeptiert werden** — deshalb
///    liefert <see cref="Pruefe"/> einen Befund und keine stillschweigende Annahme.
///
/// Eingehend sind wir bewusst grosszügiger als ausgehend: Der Distributor darf laut
/// Richtlinie andere Algorithmen verwenden, also akzeptieren wir, was .NET entschlüsseln
/// kann, und prüfen nur, DASS verschlüsselt und signiert wurde.
/// </summary>
public static class ElmWsSecurity
{
    public const string NsSoap  = "http://schemas.xmlsoap.org/soap/envelope/";
    public const string NsWsse  = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    public const string NsWsu   = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
    public const string NsDs    = "http://www.w3.org/2000/09/xmldsig#";
    public const string NsXenc  = "http://www.w3.org/2001/04/xmlenc#";

    private const string ValueTypeX509   = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3";
    private const string EncodingBase64  = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";
    private const string AlgoRsaSha256   = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
    private const string AlgoSha256      = "http://www.w3.org/2001/04/xmlenc#sha256";
    private const string AlgoAes256Cbc   = "http://www.w3.org/2001/04/xmlenc#aes256-cbc";

    /// <summary>Gültigkeitsdauer des Timestamps — kurz genug gegen Wiedereinspielen, lang genug für langsame Leitungen.</summary>
    public const int TimestampGueltigSekunden = 300;

    // ── Signieren ────────────────────────────────────────────────────────────

    /// <summary>
    /// Signiert Body und Timestamp der SOAP-Nachricht mit dem Transmitter-Zertifikat.
    /// Das Dokument wird an Ort verändert.
    /// </summary>
    public static void Signiere(XmlDocument doc, X509Certificate2 zertifikat)
    {
        if (zertifikat.GetRSAPrivateKey() == null)
            throw new InvalidOperationException("Das Zertifikat enthält keinen privaten Schlüssel — Signieren nicht möglich.");

        doc.PreserveWhitespace = true;
        var security = HoleOderErzeugeSecurityHeader(doc);
        var kennung  = Guid.NewGuid().ToString("N")[..8];

        // 1) Timestamp (muss signiert werden, Expires ist Pflicht)
        var ts = doc.CreateElement("wsu", "Timestamp", NsWsu);
        var tsId = "TS-" + kennung;
        ts.SetAttribute("Id", NsWsu, tsId);
        var jetzt = DateTime.UtcNow;
        ts.AppendChild(TextElement(doc, "wsu", "Created", NsWsu, Zeit(jetzt)));
        ts.AppendChild(TextElement(doc, "wsu", "Expires", NsWsu, Zeit(jetzt.AddSeconds(TimestampGueltigSekunden))));
        security.AppendChild(ts);

        // 2) Zertifikat als BinarySecurityToken mit direkter Referenz
        var bst = doc.CreateElement("wsse", "BinarySecurityToken", NsWsse);
        var bstId = "X509-" + kennung;
        bst.SetAttribute("EncodingType", EncodingBase64);
        bst.SetAttribute("ValueType", ValueTypeX509);
        bst.SetAttribute("Id", NsWsu, bstId);
        bst.InnerText = Convert.ToBase64String(zertifikat.RawData);
        security.AppendChild(bst);

        // 3) Body bekommt eine Kennung, damit die Signatur ihn referenzieren kann
        var body = EinzigesElement(doc, NsSoap, "Body")
                   ?? throw new InvalidOperationException("SOAP-Body nicht gefunden.");
        var bodyId = "id-" + kennung;
        body.SetAttribute("Id", NsWsu, bodyId);

        // 4) Namensraum-Deklarationen festschreiben, BEVOR signiert wird.
        //    Ein frisch im Speicher gebauter DOM kanonisiert anders als dieselbe
        //    Nachricht, nachdem sie einmal serialisiert und wieder gelesen wurde —
        //    die Signatur wäre beim Empfänger ungültig, obwohl nichts verändert
        //    wurde. Einmal durch Text und zurück beseitigt den Unterschied.
        //    Danach zeigen die alten Verweise ins Leere, also neu holen.
        doc.PreserveWhitespace = true;
        doc.LoadXml(doc.OuterXml);
        security = EinzigesElement(doc, NsWsse, "Security")!;

        // 5) Signatur über Timestamp UND Body
        var signed = new WsuSignedXml(doc)
        {
            SigningKey = zertifikat.GetRSAPrivateKey(),
        };
        signed.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
        signed.SignedInfo.SignatureMethod = AlgoRsaSha256;
        foreach (var id in new[] { tsId, bodyId })
        {
            // RefApps-Proben (09.09.2026) signieren mit rsa-sha256, Digest aber sha1.
            // Richtlinie empfiehlt sha256 — wir bleiben bei sha256; bei Bedarf umstellbar.
            var r = new Reference("#" + id) { DigestMethod = AlgoSha256 };
            r.AddTransform(new XmlDsigExcC14NTransform());
            signed.AddReference(r);
        }
        signed.KeyInfo = SecurityTokenReference(doc, bstId);
        signed.ComputeSignature();
        security.AppendChild(doc.ImportNode(signed.GetXml(), true));
    }

    /// <summary>KeyInfo mit direkter Referenz auf das mitgeschickte Zertifikat (Richtlinie Punkt 3).</summary>
    private static KeyInfo SecurityTokenReference(XmlDocument doc, string bstId)
    {
        var str = doc.CreateElement("wsse", "SecurityTokenReference", NsWsse);
        var reference = doc.CreateElement("wsse", "Reference", NsWsse);
        reference.SetAttribute("URI", "#" + bstId);
        reference.SetAttribute("ValueType", ValueTypeX509);
        str.AppendChild(reference);
        var ki = new KeyInfo();
        ki.AddClause(new KeyInfoNode(str));
        return ki;
    }

    // ── Verschlüsseln ────────────────────────────────────────────────────────

    /// <summary>
    /// Verschlüsselt den INHALT des SOAP-Body mit einem frischen AES-256-Schlüssel und
    /// legt diesen — mit dem Zertifikat des Empfängers via RSA-OAEP geschützt — als
    /// <c>EncryptedKey</c> in den Security-Header. Nach <see cref="Signiere"/> aufrufen.
    ///
    /// Wichtig: Am EncryptedKey muss ein KeyInfo mit Empfänger-Hinweis stehen
    /// (SubjectKeyIdentifier), sonst findet der Java-Receiver den privaten Schlüssel
    /// nicht und meldet Fault 100 «not signed» (RefApps-Proben 09.09.2026).
    /// </summary>
    public static void Verschluessele(XmlDocument doc, X509Certificate2 empfaengerZertifikat)
    {
        var body = EinzigesElement(doc, NsSoap, "Body")
                   ?? throw new InvalidOperationException("SOAP-Body nicht gefunden.");
        var rsa = empfaengerZertifikat.GetRSAPublicKey()
                  ?? throw new InvalidOperationException("Empfängerzertifikat ohne RSA-Schlüssel.");

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.GenerateKey();
        aes.GenerateIV();

        var enc = new EncryptedXml();
        var verschluesselt = enc.EncryptData(body, aes, content: true);

        var ekId = "EK-" + Guid.NewGuid().ToString("N")[..8];
        var edId = "ED-" + Guid.NewGuid().ToString("N")[..8];

        var ed = new EncryptedData
        {
            Type = EncryptedXml.XmlEncElementContentUrl,
            Id = edId,
            EncryptionMethod = new EncryptionMethod(AlgoAes256Cbc),
            CipherData = new CipherData(verschluesselt),
        };

        // EncryptedData → EncryptedKey verknüpfen (RefApps/WSS4J verlangt das,
        // sonst «has not been encrypted» / Code 110 trotz vorhandenem CipherValue).
        var edStr = doc.CreateElement("wsse", "SecurityTokenReference", NsWsse);
        edStr.SetAttribute("TokenType",
            "http://docs.oasis-open.org/wss/oasis-wss-wssecurity-secext-1.1.xsd",
            "http://docs.oasis-open.org/wss/oasis-wss-soap-message-security-1.1#EncryptedKey");
        // wsse11:TokenType — Attribut im 1.1-Namensraum setzen
        var wsse11 = "http://docs.oasis-open.org/wss/oasis-wss-wssecurity-secext-1.1.xsd";
        var tokenType = doc.CreateAttribute("wsse11", "TokenType", wsse11);
        tokenType.Value = "http://docs.oasis-open.org/wss/oasis-wss-soap-message-security-1.1#EncryptedKey";
        edStr.Attributes.Append(tokenType);
        var edRef = doc.CreateElement("wsse", "Reference", NsWsse);
        edRef.SetAttribute("URI", "#" + ekId);
        edStr.AppendChild(edRef);
        var edKi = new KeyInfo();
        edKi.AddClause(new KeyInfoNode(edStr));
        ed.KeyInfo = edKi;

        var ek = new EncryptedKey
        {
            Id = ekId,
            EncryptionMethod = new EncryptionMethod(EncryptedXml.XmlEncRSAOAEPUrl),
            CipherData = new CipherData(rsa.Encrypt(aes.Key, RSAEncryptionPadding.OaepSHA1)),
        };
        ek.ReferenceList.Add(new DataReference("#" + edId));

        // Empfänger-Hinweis (SKI), analog zu den funktionierenden RefApps-Proben.
        var ski = EmpfaengerSki(empfaengerZertifikat);
        if (ski != null)
        {
            var str = doc.CreateElement("wsse", "SecurityTokenReference", NsWsse);
            var kid = doc.CreateElement("wsse", "KeyIdentifier", NsWsse);
            kid.SetAttribute("EncodingType", EncodingBase64);
            kid.SetAttribute("ValueType",
                "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509SubjectKeyIdentifier");
            kid.InnerText = Convert.ToBase64String(ski);
            str.AppendChild(kid);
            var ki = new KeyInfo();
            ki.AddClause(new KeyInfoNode(str));
            ek.KeyInfo = ki;
        }

        EncryptedXml.ReplaceElement(body, ed, content: true);

        var security = HoleOderErzeugeSecurityHeader(doc);
        var ekNode = doc.ImportNode(ek.GetXml(), true);
        security.InsertBefore(ekNode, security.FirstChild);
    }

    /// <summary>SubjectKeyIdentifier aus dem Zertifikat (Extension).</summary>
    private static byte[]? EmpfaengerSki(X509Certificate2 zert)
    {
        var ski = zert.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault();
        if (ski == null) return null;
        // SubjectKeyIdentifier ist Hex ohne Trenner; in Bytes wandeln.
        var hex = ski.SubjectKeyIdentifier;
        if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0) return null;
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    // ── Prüfen und Entschlüsseln ─────────────────────────────────────────────

    /// <summary>Ergebnis der Sicherheitsprüfung einer Antwort.</summary>
    public enum Befund
    {
        /// <summary>Signatur gültig (und, sofern verlangt, verschlüsselt gewesen).</summary>
        Gueltig,
        /// <summary>Keine Signatur in der Antwort — bei Pflicht ein Fehler (F02_07).</summary>
        SignaturFehlt,
        /// <summary>Signatur vorhanden, aber rechnerisch falsch (F02_06, F02_11).</summary>
        SignaturUngueltig,
        /// <summary>Kein Zertifikat in der Antwort gefunden.</summary>
        ZertifikatFehlt,
        /// <summary>Zertifikat nicht vertrauenswürdig oder abgelaufen (F02_08).</summary>
        ZertifikatNichtVertrauenswuerdig,
        /// <summary>Antwort war unverschlüsselt, obwohl Verschlüsselung Pflicht ist (F02_04).</summary>
        VerschluesselungFehlt,
        /// <summary>Verschlüsselt, aber nicht entschlüsselbar (F02_03).</summary>
        EntschluesselungFehlgeschlagen,
    }

    public record PruefErgebnis(Befund Befund, string Meldung, string? Zertifikat = null)
    {
        public bool Ok => Befund == Befund.Gueltig;
    }

    /// <summary>
    /// Prüft eine Antwort: entschlüsseln (falls verschlüsselt), Signatur verifizieren,
    /// Zertifikat beurteilen. <paramref name="doc"/> wird bei Erfolg entschlüsselt
    /// zurückgelassen, damit der Aufrufer den Klartext anzeigen kann.
    /// </summary>
    /// <param name="unserZertifikat">Privater Schlüssel zum Entschlüsseln (unser Transmitter-Zertifikat).</param>
    /// <param name="verschluesselungPflicht">F02_04: fehlende Verschlüsselung ist ein Fehler.</param>
    /// <param name="signaturPflicht">F02_07: fehlende Signatur ist ein Fehler.</param>
    public static PruefErgebnis Pruefe(XmlDocument doc, X509Certificate2? unserZertifikat,
        bool verschluesselungPflicht = true, bool signaturPflicht = true)
    {
        // 1) Verschlüsselung
        var hatteVerschluesselung = doc.GetElementsByTagName("EncryptedData", NsXenc).Count > 0;
        if (hatteVerschluesselung)
        {
            if (unserZertifikat?.GetRSAPrivateKey() == null)
                return new PruefErgebnis(Befund.EntschluesselungFehlgeschlagen,
                    "Die Antwort ist verschlüsselt, aber es ist kein privater Schlüssel hinterlegt.");
            try
            {
                Entschluessele(doc, unserZertifikat.GetRSAPrivateKey()!);
            }
            catch (Exception ex)
            {
                return new PruefErgebnis(Befund.EntschluesselungFehlgeschlagen,
                    $"Die Antwort konnte nicht entschlüsselt werden: {ex.GetBaseException().Message}");
            }
        }
        else if (verschluesselungPflicht)
        {
            return new PruefErgebnis(Befund.VerschluesselungFehlt,
                "Die Antwort kam unverschlüsselt — die Richtlinie verlangt verschlüsselte Nutzdaten.");
        }

        // 2) Signatur
        var sigListe = doc.GetElementsByTagName("Signature", NsDs);
        if (sigListe.Count == 0)
        {
            return signaturPflicht
                ? new PruefErgebnis(Befund.SignaturFehlt,
                    "Die Antwort ist nicht signiert — die Richtlinie verlangt eine Signatur.")
                : new PruefErgebnis(Befund.Gueltig, "Antwort ohne Signatur (nicht verlangt).");
        }

        var zert = ZertifikatAusAntwort(doc);
        if (zert == null)
            return new PruefErgebnis(Befund.ZertifikatFehlt,
                "Die Antwort ist signiert, enthält aber kein Zertifikat zum Prüfen.");

        var signed = new WsuSignedXml(doc);
        signed.LoadXml((XmlElement)sigListe[0]!);
        bool gueltig;
        try { gueltig = signed.CheckSignature(zert, verifySignatureOnly: true); }
        catch (Exception ex)
        {
            return new PruefErgebnis(Befund.SignaturUngueltig,
                $"Die Signatur der Antwort konnte nicht geprüft werden: {ex.GetBaseException().Message}",
                Name(zert));
        }
        if (!gueltig)
            return new PruefErgebnis(Befund.SignaturUngueltig,
                "Die Signatur der Antwort ist ungültig — die Nachricht wurde unterwegs verändert "
                + "oder mit einem anderen Schlüssel signiert. Die Antwort wird nicht akzeptiert.",
                Name(zert));

        // 3) Zertifikat beurteilen (F02_08: unbekannter Schlüssel)
        var jetzt = DateTime.Now;
        if (jetzt < zert.NotBefore || jetzt > zert.NotAfter)
            return new PruefErgebnis(Befund.ZertifikatNichtVertrauenswuerdig,
                $"Das Zertifikat der Antwort ist nicht gültig (Laufzeit {zert.NotBefore:dd.MM.yyyy} – {zert.NotAfter:dd.MM.yyyy}).",
                Name(zert));

        return new PruefErgebnis(Befund.Gueltig,
            $"Signatur gültig{(hatteVerschluesselung ? ", Antwort war verschlüsselt" : "")}.", Name(zert));
    }

    /// <summary>
    /// Entschlüsselt eine WS-Security-Nachricht. `EncryptedXml.DecryptDocument` kann das
    /// nicht selbst: Es sucht den Schlüssel im `KeyInfo` der Daten, WS-Security legt den
    /// `EncryptedKey` aber in den Security-Header und verweist von dort über eine
    /// `ReferenceList` auf die Daten. Also holen wir den Sitzungsschlüssel selbst und
    /// entschlüsseln damit jeden `EncryptedData`-Block.
    /// </summary>
    public static void Entschluessele(XmlDocument doc, RSA privaterSchluessel)
    {
        var ekListe = doc.GetElementsByTagName("EncryptedKey", NsXenc);
        if (ekListe.Count == 0)
            throw new CryptographicException("Die Nachricht enthält keinen verschlüsselten Sitzungsschlüssel.");

        var ek = new EncryptedKey();
        ek.LoadXml((XmlElement)ekListe[0]!);
        // «rsa-oaep-mgf1p» ist die Vorgabe; nur das alte rsa-1_5 wäre ohne OAEP.
        var oaep = !(ek.EncryptionMethod?.KeyAlgorithm ?? "").EndsWith("rsa-1_5", StringComparison.OrdinalIgnoreCase);
        var sitzungsSchluessel = EncryptedXml.DecryptKey(ek.CipherData.CipherValue!, privaterSchluessel, oaep);

        var enc = new EncryptedXml(doc);
        // Liste vorher festhalten: das Ersetzen verändert den Baum.
        var daten = doc.GetElementsByTagName("EncryptedData", NsXenc).Cast<XmlElement>().ToList();
        if (daten.Count == 0)
            throw new CryptographicException("Die Nachricht enthält keine verschlüsselten Daten.");

        foreach (var el in daten)
        {
            var ed = new EncryptedData();
            ed.LoadXml(el);
            using var aes = Aes.Create();
            aes.Key = sitzungsSchluessel;
            var klartext = enc.DecryptData(ed, aes);
            enc.ReplaceData(el, klartext);
        }
    }

    /// <summary>Das Zertifikat aus dem BinarySecurityToken oder aus KeyInfo/X509Data.</summary>
    public static X509Certificate2? ZertifikatAusAntwort(XmlDocument doc)
    {
        var bst = doc.GetElementsByTagName("BinarySecurityToken", NsWsse);
        if (bst.Count > 0 && !string.IsNullOrWhiteSpace(bst[0]!.InnerText))
        {
            try { return X509CertificateLoader.LoadCertificate(Convert.FromBase64String(bst[0]!.InnerText.Trim())); }
            catch { /* weiter mit X509Data */ }
        }
        var x509 = doc.GetElementsByTagName("X509Certificate", NsDs);
        if (x509.Count > 0 && !string.IsNullOrWhiteSpace(x509[0]!.InnerText))
        {
            try { return X509CertificateLoader.LoadCertificate(Convert.FromBase64String(x509[0]!.InnerText.Trim())); }
            catch { /* kein Zertifikat lesbar */ }
        }
        return null;
    }

    private static string Name(X509Certificate2 z) =>
        z.GetNameInfo(X509NameType.SimpleName, false) is { Length: > 0 } n ? n : z.Subject;

    // ── Hilfen ───────────────────────────────────────────────────────────────

    private static string Zeit(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    private static XmlElement TextElement(XmlDocument doc, string praefix, string name, string ns, string wert)
    {
        var el = doc.CreateElement(praefix, name, ns);
        el.InnerText = wert;
        return el;
    }

    private static XmlElement? EinzigesElement(XmlDocument doc, string ns, string name)
        => doc.GetElementsByTagName(name, ns).Cast<XmlElement>().FirstOrDefault();

    /// <summary>Security-Header holen; Header und Security-Element werden bei Bedarf angelegt.</summary>
    private static XmlElement HoleOderErzeugeSecurityHeader(XmlDocument doc)
    {
        var vorhanden = EinzigesElement(doc, NsWsse, "Security");
        if (vorhanden != null) return vorhanden;

        var envelope = EinzigesElement(doc, NsSoap, "Envelope")
                       ?? throw new InvalidOperationException("SOAP-Envelope nicht gefunden.");
        var header = EinzigesElement(doc, NsSoap, "Header");
        if (header == null)
        {
            header = doc.CreateElement("soap", "Header", NsSoap);
            envelope.InsertBefore(header, envelope.FirstChild);
        }
        var security = doc.CreateElement("wsse", "Security", NsWsse);
        security.SetAttribute("mustUnderstand", NsSoap, "1");   // Richtlinie Punkt 5
        header.AppendChild(security);
        return security;
    }

    /// <summary>
    /// `SignedXml` findet Referenzen normalerweise nur über ein `Id`-Attribut ohne
    /// Namensraum. WS-Security kennzeichnet Body und Timestamp aber mit **wsu:Id** —
    /// ohne diese Erweiterung fände die Prüfung die referenzierten Teile nicht.
    /// </summary>
    private sealed class WsuSignedXml : SignedXml
    {
        public WsuSignedXml(XmlDocument doc) : base(doc) { }

        public override XmlElement? GetIdElement(XmlDocument? doc, string id)
        {
            var treffer = base.GetIdElement(doc, id);
            if (treffer != null || doc == null) return treffer;

            var nsm = new XmlNamespaceManager(doc.NameTable);
            nsm.AddNamespace("wsu", NsWsu);
            return doc.SelectSingleNode($"//*[@wsu:Id='{id}']", nsm) as XmlElement;
        }
    }
}

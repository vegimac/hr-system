using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services.Elm;

/// <summary>
/// Foundation F07 — SUA-Zertifikat anfordern (Walter/Cursor 24.09.2026).
/// Ablauf laut Bauanleitung: Register (ohne CSR) → Synchronize bis verified
/// → SignCertificate mit CSR aus Empfänger-Subject-DN + Einmalpasswort.
/// </summary>
public class ElmSuaService
{
    private static readonly XNamespace Sdst = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration:service:types";
    private static readonly XNamespace Sdc  = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration:container";
    private static readonly XNamespace Sd   = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration";
    private static readonly XNamespace Ep   = "urn:ch:swissdec:basis:v1:20260306:components";
    private static readonly XNamespace C    = "urn:ch:swissdec:common:v3:20260306";

    private readonly ElmTransmitterClient _client;
    private readonly ElmZertifikatStore _store;
    private readonly AppDbContext _db;
    private readonly ElmEinstellungen _einst;
    private readonly ElmXmlValidator _validator;

    public ElmSuaService(ElmTransmitterClient client, ElmZertifikatStore store, AppDbContext db,
        ElmEinstellungen einstellungen, ElmXmlValidator validator)
    {
        _client = client;
        _store = store;
        _db = db;
        _einst = einstellungen;
        _validator = validator;
    }

    /// <summary>Versicherungszweige laut Schema (`InstitutionDomainType`).</summary>
    public static readonly string[] Domains = { "UVG-LAA", "UVGZ-LAAC", "KTG-AMC", "BVG-LPP" };

    public const string StandardDomain = "UVG-LAA";

    /// <summary>Unbekannter Zweig ⇒ Standard, damit nie ein ungültiger Wert ins XML kommt.</summary>
    public static string DomainOderStandard(string? wert)
        => Domains.Contains((wert ?? "").Trim().ToUpperInvariant()) ? wert!.Trim().ToUpperInvariant() : StandardDomain;

    public record SuaErgebnis(
        ElmTransmitterClient.ElmCallResult Call,
        string? State,
        string? Meldung,
        ElmSuaFall? Fall,
        bool SuaVorhanden);

    /// <summary>Status für die UI — Zertifikate + laufender Fall.</summary>
    public async Task<object> StatusAsync(CancellationToken ct)
    {
        var hs = await _db.Hauptsitze.AsNoTracking()
            .Where(h => h.IsActive)
            .OrderBy(h => h.Id)
            .FirstOrDefaultAsync(ct);
        var fall = _store.LadeFall();
        return new
        {
            certPfad = _store.RootPfad,
            monitoringId = _einst.MonitoringId,
            erp = _store.ErpInfo(),
            empfaengerZertifikat = _store.HatEmpfaengerZertifikat(),
            empfaenger = _store.EmpfaengerInfo(),
            sua = _store.HatSuaZertifikat(),
            fall,
            hauptsitz = hs == null ? null : new { hs.Name, hs.Uid, hs.Ort, hs.KantonCode, hs.Plz, hs.Strasse },
        };
    }

    public object ErzeugeErp()
    {
        var z = _store.ErzeugeErp();
        return new
        {
            ok = true,
            message = "ERP-/Transmitter-Zertifikat erzeugt und gespeichert (ausserhalb des Deploy-Verzeichnisses). "
                + "Selbst signiert — gegen RefApps braucht es noch das Swissdec-.pfx (Import).",
            erp = _store.ErpInfo(),
            subject = z.Subject,
        };
    }

    /// <summary>Swissdec-TX-.pfx importieren (ersetzt selbst signiertes ERP).</summary>
    public object ImportiereErpPfx(byte[] pfxBytes, string? passwort)
    {
        var z = _store.ImportiereErpPfx(pfxBytes, passwort);
        var info = _store.ErpInfo();
        return new
        {
            ok = true,
            message = "Swissdec-.pfx importiert — CheckInterop / Register können gegen RefApps laufen.",
            erp = info,
            subject = z.Subject,
        };
    }

    /// <summary>F07_01 — RegisterOrganizationAuthentication (noch ohne CSR).</summary>
    public async Task<SuaErgebnis> RegisterAsync(string url, ElmSuaRegisterDto dto, CancellationToken ct)
    {
        var erp = _store.LadeErp()
            ?? throw new InvalidOperationException("Zuerst das ERP-Zertifikat erzeugen (Knopf «ERP-Zertifikat erzeugen»).");

        var uid = (dto.Uid ?? "").Trim();
        var name = (dto.CompanyName ?? "").Trim();
        var kontakt = (dto.ContactName ?? "").Trim();
        var addresseeId = string.IsNullOrWhiteSpace(dto.AddresseeIdentification)
            ? "1234" : dto.AddresseeIdentification.Trim();
        if (uid.Length == 0 || name.Length == 0 || kontakt.Length == 0)
            throw new InvalidOperationException("UID, Firmenname und Kontakt sind Pflicht.");

        var domain = DomainOderStandard(dto.Domain);
        var body = BaueRegisterBody(uid, name, kontakt, addresseeId,
            dto.Zip ?? "6000", dto.City ?? "Luzern", domain,
            dto.InsuranceName ?? "Test Versicherer",
            dto.CustomerIdentity ?? "CustomerIdentity",
            dto.ContractIdentity ?? "ContractIdentity",
            dto.AlsTestfall);
        PruefeSchema(body, "Register");

        var call = await _client.PostGesichertAsync(url, body, erp, _store.LadeEmpfaenger(), "sua-register", ct);
        var abgewiesen = ElmTransmitterClient.DeuteSicherheitsFault(call);
        if (abgewiesen != null)
        {
            return new SuaErgebnis(call, null, abgewiesen.Meldung, _store.LadeFall(),
                _store.HatSuaZertifikat());
        }
        var (requestId, key, password) = ParseRegisterAntwort(call.ResponseXml);

        ElmSuaFall? fall = null;
        string? meldung = null;
        if (requestId != null && key != null && password != null)
        {
            fall = new ElmSuaFall
            {
                CertificateRequestId = requestId,
                CredentialKey = key,
                CredentialPassword = password,
                AddresseeId = "#addressee",
                AddresseeIdentification = addresseeId,
                Uid = uid,
                CompanyName = name,
                AlsTestfall = dto.AlsTestfall,
                Domain = domain,
                LetzterState = null,
                UpdatedAt = DateTime.Now,
            };
            _store.SpeichereFall(fall);
            meldung = "Register erfolgreich — CertificateRequestID und Credentials gespeichert. Als Nächstes «Status abfragen».";
        }
        else if (call.FaultCode != null)
        {
            meldung = $"Abgewiesen — {call.FaultCode}: {call.FaultText}";
        }
        else if (!call.Ok)
        {
            meldung = call.Error ?? "Register ohne Success-Antwort.";
        }
        else
        {
            meldung = "Antwort erhalten, aber keine CertificateRequestID/Credentials gefunden — Antwort-XML prüfen.";
        }

        return new SuaErgebnis(call, null, meldung, fall ?? _store.LadeFall(), _store.HatSuaZertifikat());
    }

    /// <summary>
    /// F07_03–06 — Synchronize. Bei State=verified und oneTimePassword gesetzt:
    /// SignCertificate mit CSR aus Quittance-Subject.
    /// </summary>
    public async Task<SuaErgebnis> SynchronizeAsync(string url, string? oneTimePassword, bool renew,
        CancellationToken ct)
    {
        var erp = _store.LadeErp()
            ?? throw new InvalidOperationException("ERP-Zertifikat fehlt.");
        var fall = _store.LadeFall()
            ?? throw new InvalidOperationException("Kein laufender SUA-Fall — zuerst «Registrieren».");

        XElement? signBlock = null;
        RSA? csrKey = null;
        if (renew)
        {
            if (fall.Subject == null)
                throw new InvalidOperationException("Für Renew fehlt der Subject-DN aus einer früheren Quittung.");
            (signBlock, csrKey) = BaueRenewBlock(fall.Subject);
        }
        else if (!string.IsNullOrWhiteSpace(oneTimePassword))
        {
            if (fall.Subject == null)
                throw new InvalidOperationException(
                    "Subject-DN noch unbekannt. Zuerst Status abfragen bis «verified» (ohne Einmalpasswort) — "
                    + "die Quittung liefert den DN. Dann Signieren mit Einmalpasswort.");
            if (!string.Equals(fall.LetzterState, "verified", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"SignCertificate erst bei Status «verified» (aktuell: «{fall.LetzterState ?? "—"}»).");
            (signBlock, csrKey) = BaueSignBlock(fall.Subject!, oneTimePassword.Trim());
        }

        var body = BaueSynchronizeBody(fall, signBlock);
        PruefeSchema(body, "Synchronize");
        var call = await _client.PostGesichertAsync(url, body, erp, _store.LadeEmpfaenger(), "sua-sync", ct);
        var abgewiesen = ElmTransmitterClient.DeuteSicherheitsFault(call);
        if (abgewiesen != null)
        {
            return new SuaErgebnis(call, fall.LetzterState, abgewiesen.Meldung, fall,
                _store.HatSuaZertifikat());
        }

        var geparst = ParseSynchronizeAntwort(call.ResponseXml);
        if (geparst.State != null)
        {
            fall.LetzterState = geparst.State;
            fall.UpdatedAt = DateTime.Now;
        }
        if (geparst.Subject != null)
            fall.Subject = geparst.Subject;

        string? meldung = geparst.Fehler ?? call.FaultText;
        if (geparst.State != null)
            meldung = StateMeldung(geparst.State);

        if (geparst.ZertifikatPem != null && csrKey != null)
        {
            var pemText = PemAusBase64(geparst.ZertifikatPem);
            _store.SpeichereSua(pemText, csrKey);
            meldung = (meldung ?? "") + " · SUA-Zertifikat gespeichert.";
        }

        _store.SpeichereFall(fall);
        return new SuaErgebnis(call, geparst.State, meldung, fall, _store.HatSuaZertifikat());
    }

    // ── XML bauen ────────────────────────────────────────────────────────────

    /// <summary>Öffentlich, damit die Tests die Meldung gegen die Schemas prüfen können.</summary>
    public XElement BaueRegisterBody(
        string uid, string companyName, string contact, string addresseeId,
        string zip, string city, string domain, string insurance, string customerId, string contractId,
        bool testCase)
    {
        var addresseeRef = "#addressee";
        return new XElement(Sdst + "RegisterOrganizationAuthentication",
            new XAttribute("schemaVersion", "0.0"),
            new XAttribute(XNamespace.Xmlns + "sdst", Sdst),
            new XAttribute(XNamespace.Xmlns + "sdc", Sdc),
            new XAttribute(XNamespace.Xmlns + "sd", Sd),
            new XAttribute(XNamespace.Xmlns + "ep", Ep),
            new XAttribute(XNamespace.Xmlns + "c", C),
            RequestContext(companyName),
            new XElement(Sdc + "Job",
                new XElement(Sdc + "Addressee",
                    new XAttribute("addresseeID", addresseeRef),
                    new XElement(Ep + "AddresseeIdentification", addresseeId),
                    new XElement(Ep + "ProcessByDistributor", true)),
                // TestCase ist ein GESCHWISTER des Addressee im Job und gehört in den
                // Container-Namensraum (sdc). Im Addressee gibt es das Element nicht —
                // dort macht es die Meldung schema-ungültig (Walter 24.09.2026).
                testCase ? new XElement(Sdc + "TestCase") : null),
            new XElement(Sdc + "RegisterOrganization",
                new XElement(Sd + "Institution",
                    new XElement(Sd + domain,
                        new XAttribute("addresseeIDRef", addresseeRef),
                        new XElement(C + "InsuranceCompanyName", insurance),
                        new XElement(C + "CustomerIdentity", customerId),
                        new XElement(C + "ContractIdentity", contractId))),
                new XElement(Sdc + "CompanyDescription",
                    new XElement(C + "Name", new XElement(C + "HR-RC-Name", companyName)),
                    new XElement(C + "Address",
                        new XElement(C + "ZIP-Code", zip),
                        new XElement(C + "City", city)),
                    new XElement(C + "UID-BFS", new XElement(Ep + "UID", uid))),
                new XElement(Sdc + "Contact", new XElement(C + "Name", contact))));
    }

    /// <summary>Öffentlich, damit die Tests die Meldung gegen die Schemas prüfen können.</summary>
    public XElement BaueSynchronizeBody(ElmSuaFall fall, XElement? signOrRenew)
    {
        var caseEl = new XElement(Sdc + "Case",
            new XElement(C + "CaseContext",
                new XElement(Ep + "Credentials",
                    new XElement(Ep + "Key", fall.CredentialKey),
                    new XElement(Ep + "Password", fall.CredentialPassword)),
                new XElement(C + "CertificateRequestID", fall.CertificateRequestId),
                // Nur wenn die Anmeldung wirklich ein Testfall war — und dann im
                // c-Namensraum. Ein fest gesetztes TestCase hiesse: nie ein Zertifikat
                // (Richtlinie Anhang C.2.1.2, Walter 24.09.2026).
                fall.AlsTestfall ? new XElement(C + "TestCase") : null),
            fall.LetzterState != null
                ? new XElement(C + "ReceivedState", fall.LetzterState)
                : null,
            signOrRenew);

        // SignCertificate vs RenewCertificate: Elementname steckt schon im Block.
        return new XElement(Sdst + "SynchronizeRegisterOrganizationAuthentication",
            new XAttribute("schemaVersion", "0.0"),
            new XAttribute(XNamespace.Xmlns + "sdst", Sdst),
            new XAttribute(XNamespace.Xmlns + "sdc", Sdc),
            new XAttribute(XNamespace.Xmlns + "sd", Sd),
            new XAttribute(XNamespace.Xmlns + "ep", Ep),
            new XAttribute(XNamespace.Xmlns + "c", C),
            RequestContext(fall.CompanyName ?? "OneCrew"),
            new XElement(Ep + "Sender",
                new XElement(Ep + "UID-BFS", new XElement(Ep + "UID", fall.Uid ?? ""))),
            // Der Addressee des Synchronize ist ein ANDERER Typ als der im Register
            // (InstitutionAddresseeType): kein addresseeID, kein ProcessByDistributor,
            // dafür der Versicherungszweig.
            new XElement(Sdc + "Addressee",
                new XElement(Ep + "AddresseeIdentification", fall.AddresseeIdentification ?? "1234"),
                new XElement(Sd + "Domain", fall.Domain ?? StandardDomain)),
            caseEl);
    }

    /// <summary>
    /// Vor dem Senden gegen die ELM-Schemas prüfen (Walter 24.09.2026).
    ///
    /// Anlass: Die erste Fassung schickte `TestCase` an der falschen Stelle und den
    /// Synchronize-Addressee im Aufbau des Register-Addressee — beides schema-ungültig,
    /// beides erst am abgewiesenen Aufruf sichtbar. Der Distributor prüft als Erstes die
    /// Validität; eine ungültige Meldung verlässt uns also gar nicht erst.
    /// </summary>
    private void PruefeSchema(XElement body, string was)
    {
        var fehler = _validator.Validate(body.ToString());
        if (fehler.Count == 0) return;
        throw new InvalidOperationException(
            $"Die {was}-Meldung entspricht nicht dem ELM-Schema und wurde NICHT gesendet: "
            + string.Join(" · ", fehler.Take(3)));
    }

    private XElement RequestContext(string companyName) =>
        new(Ep + "RequestContext",
            new XElement(Ep + "UserAgent",
                new XElement(Ep + "Producer", "Schaub Restaurants GmbH"),
                new XElement(Ep + "Name", "OneCrew"),
                new XElement(Ep + "Version", "2026.09"),
                new XElement(Ep + "StandardVersion", "6.0"),
                new XElement(Ep + "Certificate", "n/a")),
            new XElement(Ep + "CompanyName", companyName),
            new XElement(Ep + "TransmissionDate", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")),
            new XElement(Ep + "RequestID", Guid.NewGuid().ToString("N")),
            new XElement(Ep + "LanguageCode", "de"),
            // Auf den Testsystemen zwingend: ordnet die Übermittlung in der
            // Referenzapplikation dem richtigen Benutzer zu (Walter 24.09.2026).
            _einst.MonitoringElement(Ep));

    /// <summary>CSR aus Empfänger-Subject — DN nicht selbst erfinden (Bauanleitung).</summary>
    public static (XElement Block, RSA Key) BaueSignBlock(ElmSuaSubject subject, string oneTimePassword)
    {
        var (pemB64, key) = ErzeugeCsrPemBase64(subject);
        // Creation/StoryID stammen aus ep:StoryBaseType; PEM/OTP aus dem c-Schema.
        var block = new XElement(C + "SignCertificate",
            new XElement(Ep + "Creation", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")),
            new XElement(Ep + "StoryID", Guid.NewGuid().ToString("N")),
            new XElement(C + "PEM", pemB64),
            new XElement(C + "OneTimePassword", oneTimePassword));
        return (block, key);
    }

    public static (XElement Block, RSA Key) BaueRenewBlock(ElmSuaSubject subject)
    {
        var (pemB64, key) = ErzeugeCsrPemBase64(subject);
        var block = new XElement(C + "RenewCertificate",
            new XElement(Ep + "Creation", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")),
            new XElement(Ep + "StoryID", Guid.NewGuid().ToString("N")),
            new XElement(C + "PEM", pemB64));
        return (block, key);
    }

    public static (string PemBase64, RSA Key) ErzeugeCsrPemBase64(ElmSuaSubject subject)
    {
        var dn = subject.AlsDn();
        if (string.IsNullOrWhiteSpace(dn))
            throw new InvalidOperationException("Subject-DN ist leer — Quittung prüfen.");
        var rsa = RSA.Create(2048);
        var req = new CertificateRequest(dn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var csrDer = req.CreateSigningRequest();
        var pem = "-----BEGIN CERTIFICATE REQUEST-----\n"
                + Convert.ToBase64String(csrDer, Base64FormattingOptions.InsertLineBreaks)
                + "\n-----END CERTIFICATE REQUEST-----\n";
        return (Convert.ToBase64String(Encoding.ASCII.GetBytes(pem)), rsa);
    }

    public static string PemAusBase64(string base64)
    {
        var raw = Convert.FromBase64String(base64.Trim());
        // Manchmal kommt schon PEM-Text als Base64, manchmal DER.
        var asText = Encoding.ASCII.GetString(raw);
        if (asText.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal))
            return asText;
        return "-----BEGIN CERTIFICATE-----\n"
             + Convert.ToBase64String(raw, Base64FormattingOptions.InsertLineBreaks)
             + "\n-----END CERTIFICATE-----\n";
    }

    public static string StateMeldung(string state) => state.ToLowerInvariant() switch
    {
        "processing" => "Status: processing — Anfrage in Bearbeitung.",
        "registered" => "Status: registered — registriert, nächster Schritt folgt.",
        "rejected"   => "Status: rejected — Anfrage abgelehnt.",
        "verified"   => "Status: verified — bereit zum Signieren (Einmalpasswort eingeben).",
        "expired"    => "Status: expired — Anfrage abgelaufen (sechster Zustand, auch anzeigen).",
        _            => $"Status: {state}",
    };

    // ── Antworten lesen ──────────────────────────────────────────────────────

    private static (string? RequestId, string? Key, string? Password) ParseRegisterAntwort(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return (null, null, null);
        try
        {
            var doc = XDocument.Parse(xml);
            string? Loc(string name) => doc.Descendants().FirstOrDefault(e => e.Name.LocalName == name && !e.HasElements)?.Value.Trim();
            return (Loc("CertificateRequestID"), Loc("Key"), Loc("Password"));
        }
        catch { return (null, null, null); }
    }

    private record SyncParse(string? State, ElmSuaSubject? Subject, string? ZertifikatPem, string? Fehler);

    private static SyncParse ParseSynchronizeAntwort(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return new(null, null, null, null);
        try
        {
            var doc = XDocument.Parse(xml);
            var fehler = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Error");
            string? fehlerText = null;
            if (fehler != null)
                fehlerText = fehler.Descendants().FirstOrDefault(e => e.Name.LocalName is "Message" or "faultstring" or "Text")
                    ?.Value.Trim() ?? "Error in Synchronize-Antwort";

            var state = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "State" && !e.HasElements)?.Value.Trim();

            ElmSuaSubject? subject = null;
            var x509 = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "X509Subject");
            if (x509 != null)
            {
                string? V(string n) => x509.Elements().FirstOrDefault(e => e.Name.LocalName == n)?.Value.Trim();
                subject = new ElmSuaSubject
                {
                    CommonName = V("CommonName") ?? "",
                    OrganizationName = V("OrganizationName") ?? "",
                    LocalityName = V("LocalityName") ?? "",
                    StateOrProvinceName = V("StateOrProvinceName") ?? "",
                    CountryName = V("CountryName") ?? "",
                    BusinessCategory = V("BusinessCategory"),
                };
            }

            // Zertifikat-PEM steckt in Certificate/PEM (nicht CSR-PEM).
            var certEl = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Certificate");
            var pem = certEl?.Descendants().FirstOrDefault(e => e.Name.LocalName == "PEM")?.Value.Trim();

            return new SyncParse(state, subject, pem, fehlerText);
        }
        catch (Exception ex)
        {
            return new(null, null, null, ex.GetBaseException().Message);
        }
    }
}

public record ElmSuaRegisterDto(
    string? Uid,
    string? CompanyName,
    string? ContactName,
    string? Zip,
    string? City,
    string? AddresseeIdentification,
    string? Domain,
    string? InsuranceName,
    string? CustomerIdentity,
    string? ContractIdentity,
    bool AlsTestfall = false);

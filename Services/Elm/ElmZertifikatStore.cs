using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.Configuration;

namespace HrSystem.Services.Elm;

/// <summary>
/// Ablage für ERP-/SUA-Zertifikate und den laufenden SUA-Fall (Foundation F07).
///
/// Pfad bewusst AUSSERHALB von /var/www: deploy.sh leert das Programmverzeichnis
/// (Claude-Hinweis 24.09.2026). Muster wie Documents:StoragePath —
/// Konfig <c>Swissdec:CertStoragePath</c>, Fallback neben Documents bzw. lokal
/// <c>data/swissdec-certs</c>.
/// </summary>
public class ElmZertifikatStore
{
    private const string ErpDatei = "erp-transmitter.pfx";
    private const string ErpPasswortDatei = "erp-transmitter.pwd";
    private const string SuaPemDatei = "sua-certificate.pem";
    private const string SuaKeyDatei = "sua-private.key";
    private const string EmpfaengerDatei = "empfaenger.cer";
    private const string FallDatei = "sua-fall.json";

    /// <summary>
    /// Klartext für Fault 100 / «non-certified digital certificate» —
    /// selbst signiertes ERP zählt bei RefApps nicht; Swissdec-.pfx nötig.
    /// </summary>
    /// <summary>
    /// Hinweis, wenn der Empfänger unser Zertifikat ablehnt. Bewusst ohne erfundenen
    /// Fehlercode — «Fault 100» gibt es im ELM-Standard nicht (Walter 24.09.2026).
    /// </summary>
    public const string ZertifikatAbgelehntHinweis =
        "Der Empfänger erkennt unser Zertifikat nicht an. Laut Sicherheitsrichtlinie prüft "
        + "der Distributor unseren öffentlichen Schlüssel gegen das Swissdec-CA-Zertifikat — "
        + "ein selbst erzeugtes Zertifikat besteht das nicht. Liegt ein von Swissdec "
        + "ausgestelltes .pfx vor, hier importieren; sonst bei Swissdec nachfragen, woher das "
        + "Transmitter-Zertifikat für den Foundation-Test kommt.";

    private readonly string _root;

    public ElmZertifikatStore(IConfiguration config)
    {
        var konfiguriert = (config["Swissdec:CertStoragePath"] ?? "").Trim();
        if (konfiguriert.Length > 0)
        {
            _root = konfiguriert;
        }
        else
        {
            var docs = (config["Documents:StoragePath"] ?? "").Trim();
            _root = docs.Length > 0
                ? Path.GetFullPath(Path.Combine(docs, "..", "swissdec-certs"))
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "data", "swissdec-certs"));
        }
        Directory.CreateDirectory(_root);
    }

    public string RootPfad => _root;

    // ── ERP / Transmitter ──────────────────────────────────────────────────────

    public bool HatErpZertifikat() => File.Exists(Path.Combine(_root, ErpDatei));

    /// <summary>Lädt das ERP-Zertifikat inkl. privatem Schlüssel; NULL wenn keines da.</summary>
    public X509Certificate2? LadeErp()
    {
        var pfx = Path.Combine(_root, ErpDatei);
        if (!File.Exists(pfx)) return null;
        var pwd = File.Exists(Path.Combine(_root, ErpPasswortDatei))
            ? File.ReadAllText(Path.Combine(_root, ErpPasswortDatei)).Trim()
            : "";
        return X509CertificateLoader.LoadPkcs12FromFile(pfx, pwd,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }

    /// <summary>
    /// Erzeugt ein selbst signiertes ERP-/Transmitter-Zertifikat (Expertin itserv:
    /// selbst in Foundation erstellen) und speichert es dauerhaft.
    /// </summary>
    public X509Certificate2 ErzeugeErp(string commonName = "CN=OneCrew Transmitter, O=Schaub Restaurants GmbH, C=CH")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(commonName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var zert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));

        var pwd = Guid.NewGuid().ToString("N");
        var bytes = zert.Export(X509ContentType.Pfx, pwd);
        var pfxPfad = Path.Combine(_root, ErpDatei);
        File.WriteAllBytes(pfxPfad, bytes);
        File.WriteAllText(Path.Combine(_root, ErpPasswortDatei), pwd);
        VersucheRechte600(pfxPfad);
        VersucheRechte600(Path.Combine(_root, ErpPasswortDatei));

        return X509CertificateLoader.LoadPkcs12(bytes, pwd,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }

    /// <summary>
    /// Swissdec-TX-.pfx importieren (ersetzt selbst signiertes ERP).
    /// Passwort wird mitgespeichert — gleiches Muster wie ErzeugeErp.
    /// </summary>
    public X509Certificate2 ImportiereErpPfx(byte[] pfxBytes, string? passwort)
    {
        if (pfxBytes == null || pfxBytes.Length == 0)
            throw new InvalidOperationException("PFX-Datei ist leer.");
        var pwd = passwort ?? "";
        X509Certificate2 zert;
        try
        {
            zert = X509CertificateLoader.LoadPkcs12(pfxBytes, pwd,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "PFX lässt sich nicht öffnen — Passwort prüfen. " + ex.GetBaseException().Message, ex);
        }
        if (!zert.HasPrivateKey)
            throw new InvalidOperationException("PFX enthält keinen privaten Schlüssel.");

        var pfxPfad = Path.Combine(_root, ErpDatei);
        File.WriteAllBytes(pfxPfad, pfxBytes);
        File.WriteAllText(Path.Combine(_root, ErpPasswortDatei), pwd);
        VersucheRechte600(pfxPfad);
        VersucheRechte600(Path.Combine(_root, ErpPasswortDatei));
        return zert;
    }

    public object ErpInfo()
    {
        var z = LadeErp();
        if (z == null) return new { vorhanden = false, pfad = _root };
        var selbstSigniert = string.Equals(z.Subject, z.Issuer, StringComparison.OrdinalIgnoreCase);
        return new
        {
            vorhanden = true,
            pfad = _root,
            subject = z.Subject,
            issuer = z.Issuer,
            selbstSigniert,
            notBefore = z.NotBefore,
            notAfter = z.NotAfter,
            thumbprint = z.Thumbprint,
        };
    }

    /// <summary>
    /// Foundation F04 — signierten Klartext (Request vor Encrypt / Response nach Decrypt)
    /// unter <c>archiv/</c> ablegen. Best-effort, nie Aufruf abbrechen.
    /// </summary>
    public void ArchiviereKlartext(string name, string xml)
    {
        try
        {
            var dir = Path.Combine(_root, "archiv");
            Directory.CreateDirectory(dir);
            var sicher = string.Join("_", (name ?? "msg").Split(Path.GetInvalidFileNameChars()));
            var datei = Path.Combine(dir,
                $"{DateTime.Now:yyyyMMdd-HHmmss}-{sicher}.xml");
            File.WriteAllText(datei, xml ?? "", Encoding.UTF8);
            VersucheRechte600(datei);
        }
        catch { /* Archiv best-effort */ }
    }

    // ── Empfängerzertifikat (für WS-Encryption) ──────────────────────────────

    public bool HatEmpfaengerZertifikat() => File.Exists(Path.Combine(_root, EmpfaengerDatei));

    public X509Certificate2? LadeEmpfaenger()
    {
        var p = Path.Combine(_root, EmpfaengerDatei);
        if (!File.Exists(p)) return null;
        return X509CertificateLoader.LoadCertificateFromFile(p);
    }

    /// <summary>
    /// Empfänger für WS-Encryption an RefApps ELMv6. Ein aus Fault-Antworten
    /// gelerntes klassisches RefApps-Receiver-.cer wird ignoriert zugunsten des
    /// Distributor-ELMv6-Assets (sonst Fault 100/110).
    /// </summary>
    public X509Certificate2? LadeEmpfaengerFuerVerschluesselung()
    {
        var gespeichert = LadeEmpfaenger();
        var fallback = LadeRefAppsEmpfaengerFallback();
        if (gespeichert == null) return fallback;
        if (IstKlassischerRefAppsReceiver(gespeichert)
            && fallback != null
            && fallback.Subject.Contains("Distributor", StringComparison.OrdinalIgnoreCase))
            return fallback;
        return gespeichert;
    }

    private static bool IstKlassischerRefAppsReceiver(X509Certificate2 z) =>
        z.Subject.Contains("RefApp", StringComparison.OrdinalIgnoreCase)
        && !z.Subject.Contains("Distributor", StringComparison.OrdinalIgnoreCase);

    public object EmpfaengerInfo()
    {
        var z = LadeEmpfaenger();
        if (z == null) return new { vorhanden = false };
        return new
        {
            vorhanden = true,
            subject = z.Subject,
            issuer = z.Issuer,
            notAfter = z.NotAfter,
            thumbprint = z.Thumbprint,
        };
    }

    public void SpeichereEmpfaenger(byte[] derOderPem)
    {
        var p = Path.Combine(_root, EmpfaengerDatei);
        File.WriteAllBytes(p, derOderPem);
        VersucheRechte600(p);
    }

    /// <summary>
    /// Empfängerzertifikat aus einer <b>erfolgreichen</b> Antwort übernehmen.
    /// Faults der RefApps ELMv6 sind oft mit dem klassischen «RefApps Receiver»
    /// signiert — den dürfen wir NICHT als Verschlüsselungsziel speichern
    /// (Live-Probe 25.09.2026: Encrypt damit → Fault 110/100).
    /// </summary>
    public bool UebernehmeEmpfaengerAusAntwort(XmlDocument antw)
    {
        if (HatEmpfaengerZertifikat()) return false;
        var istFault = antw.GetElementsByTagName("Fault", "http://schemas.xmlsoap.org/soap/envelope/").Count > 0
                    || antw.GetElementsByTagName("Fault").Count > 0;
        if (istFault) return false;
        var z = ElmWsSecurity.ZertifikatAusAntwort(antw);
        if (z == null) return false;
        // Nur Distributor-/ELMv6-Empfänger lernen — klassischer RefApps-Receiver ist falsch für V6.
        if (z.Subject.Contains("RefApp", StringComparison.OrdinalIgnoreCase)
            && !z.Subject.Contains("Distributor", StringComparison.OrdinalIgnoreCase))
            return false;
        SpeichereEmpfaenger(z.RawData);
        return true;
    }

    /// <summary>
    /// Eingebettetes Empfängerzertifikat (Assets), Fallback wenn noch keines
    /// gespeichert ist. Gegen RefApps stable V6 (Live-Probe 25.09.2026) muss
    /// <b>SwissdecDistributorELMv6Test</b> zum Verschlüsseln verwendet werden —
    /// RefApps-Receiver.cer führt dort zu Fault 110 «not encrypted».
    /// </summary>
    public static X509Certificate2? LadeRefAppsEmpfaengerFallback()
    {
        foreach (var name in new[]
                 {
                     "SwissdecDistributorELMv6Test.cer",
                     "SwissdecDistributorELMv6Test.pem",
                     "RefApps-Receiver.cer",
                 })
        {
            foreach (var kandidat in new[]
                     {
                         Path.Combine(AppContext.BaseDirectory, "Assets", "Swissdec", name),
                         Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Swissdec", name),
                     })
            {
                if (!File.Exists(kandidat)) continue;
                try { return X509CertificateLoader.LoadCertificateFromFile(kandidat); }
                catch { /* nächster Pfad */ }
            }
        }
        return null;
    }

    /// <summary>
    /// F02_08 — Zertifikate, mit denen eine Antwort signiert sein darf: der Distributor ELMv6
    /// (normale Antworten) und der klassische RefApp Receiver (Faults), beide aus Assets,
    /// plus alles unter <c>vertrauen/</c> neben den Zertifikaten (z.B. Produktiv-Distributor
    /// oder Swissdec-Stelle). Das gespeicherte Empfängerzertifikat zählt bewusst NICHT —
    /// es wurde früher ungeprüft aus Antworten übernommen.
    /// </summary>
    public List<X509Certificate2> LadeVertrauensliste()
    {
        var liste = new List<X509Certificate2>();
        void Hinzu(string pfad)
        {
            try
            {
                var z = X509CertificateLoader.LoadCertificateFromFile(pfad);
                if (!liste.Any(v => v.RawData.AsSpan().SequenceEqual(z.RawData))) liste.Add(z);
            }
            catch { /* unlesbare Datei überspringen */ }
        }

        foreach (var name in new[] { "SwissdecDistributorELMv6Test.cer", "RefApps-Receiver.cer" })
        {
            var pfad = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "Assets", "Swissdec", name),
                    Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Swissdec", name),
                }
                .FirstOrDefault(File.Exists);
            if (pfad != null) Hinzu(pfad);
        }

        var ordner = Path.Combine(_root, "vertrauen");
        if (Directory.Exists(ordner))
            foreach (var datei in Directory.GetFiles(ordner)
                         .Where(d => d.EndsWith(".cer", StringComparison.OrdinalIgnoreCase)
                                  || d.EndsWith(".crt", StringComparison.OrdinalIgnoreCase)
                                  || d.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(d => d))
                Hinzu(datei);
        return liste;
    }

    // ── SUA-Zertifikat (nach SignCertificate) ────────────────────────────────

    public bool HatSuaZertifikat() => File.Exists(Path.Combine(_root, SuaPemDatei));

    public void SpeichereSua(string pemZertifikat, RSA? privateKey = null)
    {
        var p = Path.Combine(_root, SuaPemDatei);
        File.WriteAllText(p, pemZertifikat.Trim() + "\n");
        VersucheRechte600(p);
        if (privateKey != null)
        {
            var k = Path.Combine(_root, SuaKeyDatei);
            File.WriteAllText(k, privateKey.ExportPkcs8PrivateKeyPem());
            VersucheRechte600(k);
        }
    }

    public X509Certificate2? LadeSua()
    {
        var p = Path.Combine(_root, SuaPemDatei);
        if (!File.Exists(p)) return null;
        var pem = File.ReadAllText(p);
        var k = Path.Combine(_root, SuaKeyDatei);
        if (File.Exists(k))
            return X509Certificate2.CreateFromPem(pem, File.ReadAllText(k));
        return X509Certificate2.CreateFromPem(pem);
    }

    /// <summary>
    /// SUA-Zertifikat für die zweite Signatur — nur mit privatem Schlüssel, sonst NULL
    /// (dann geht die Anfrage einfach signiert, wie vor dem SUA-Prozess).
    /// </summary>
    public X509Certificate2? LadeSuaZumSignieren()
    {
        var sua = LadeSua();
        return sua?.HasPrivateKey == true ? sua : null;
    }

    // ── Laufender SUA-Fall (CertificateRequestID + Credentials) ─────────────

    public ElmSuaFall? LadeFall()
    {
        var p = Path.Combine(_root, FallDatei);
        if (!File.Exists(p)) return null;
        return JsonSerializer.Deserialize<ElmSuaFall>(File.ReadAllText(p));
    }

    public void SpeichereFall(ElmSuaFall fall)
    {
        var p = Path.Combine(_root, FallDatei);
        File.WriteAllText(p, JsonSerializer.Serialize(fall, new JsonSerializerOptions { WriteIndented = true }));
        VersucheRechte600(p);
    }

    public void LoescheFall()
    {
        var p = Path.Combine(_root, FallDatei);
        if (File.Exists(p)) File.Delete(p);
    }

    // ── Foundation-Stand je Prüfpunkt (Walter 29.09.2026) ────────────────────
    // Bewusst Datei statt DB: gehört zur Testinfrastruktur wie Zertifikate und Fall.

    private const string FoundationDatei = "foundation-stand.json";

    public Dictionary<string, ElmFoundationEintrag> LadeFoundationStand()
    {
        var p = Path.Combine(_root, FoundationDatei);
        if (!File.Exists(p)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, ElmFoundationEintrag>>(File.ReadAllText(p)) ?? new(); }
        catch { return new(); }
    }

    public void SpeichereFoundationEintrag(string id, ElmFoundationEintrag eintrag)
    {
        var stand = LadeFoundationStand();
        stand[id] = eintrag;
        var p = Path.Combine(_root, FoundationDatei);
        File.WriteAllText(p, JsonSerializer.Serialize(stand, new JsonSerializerOptions { WriteIndented = true }));
        VersucheRechte600(p);
    }

    // ── Archiv lesen (Foundation F04_01_2: Archiv-Dateien im ERP prüfen) ─────

    private static readonly Regex ArchivName = new(@"^[0-9]{8}-[0-9]{6}-[A-Za-z0-9_\-]+\.xml$");

    /// <summary>Anzahl archivierter Nachrichten und ob der Archiv-Ordner beschreibbar ist.</summary>
    public (int Anzahl, bool Beschreibbar) ArchivZustand()
    {
        var dir = Path.Combine(_root, "archiv");
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".schreibprobe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return (Directory.GetFiles(dir, "*.xml").Length, true);
        }
        catch
        {
            return (Directory.Exists(dir) ? Directory.GetFiles(dir, "*.xml").Length : 0, false);
        }
    }

    public List<ElmArchivDatei> ListeArchiv(int max = 60)
    {
        var dir = Path.Combine(_root, "archiv");
        if (!Directory.Exists(dir)) return new();
        var alle = new DirectoryInfo(dir).GetFiles("*.xml").OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
        return alle
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Take(max)
            .Select(f =>
            {
                var inhalt = "";
                try { inhalt = File.ReadAllText(f.FullName); } catch { }
                ElmSignaturBestaetigung.Ergebnis? sc = null;
                var anfrage = ArchivAnfrageZu(f.Name, alle);
                if (anfrage != null)
                {
                    try { sc = ElmSignaturBestaetigung.Pruefe(File.ReadAllText(anfrage.FullName), inhalt); }
                    catch { /* Anzeige ohne Prüfung */ }
                }
                return new ElmArchivDatei(f.Name, f.LastWriteTime, f.Length,
                    inhalt.Contains("SignatureConfirmation", StringComparison.Ordinal),
                    inhalt.Contains("EncryptedData", StringComparison.Ordinal),
                    Regex.Matches(inhalt, @"<(\w+:)?Signature[\s>]").Count)
                { ScStand = sc?.Art, ScMeldung = sc?.Meldung, Anfrage = anfrage?.Name };
            })
            .ToList();
    }

    /// <summary>
    /// Zur Antwort «…-X-response.xml» die jüngste Anfrage «…-X-request.xml», die nicht
    /// später liegt. Beide tragen denselben Namen; die Anfrage wird vor dem Senden
    /// archiviert, die Antwort danach. NULL für Anfragen und wenn keine passt.
    /// </summary>
    public static FileInfo? ArchivAnfrageZu(string antwortName, IReadOnlyList<FileInfo> alleAufsteigend)
    {
        var m = Regex.Match(antwortName, @"^[0-9]{8}-[0-9]{6}-(?<art>.+)-response\.xml$");
        if (!m.Success) return null;
        var gesucht = m.Groups["art"].Value + "-request.xml";
        return alleAufsteigend
            .Where(f => f.Name.Length > 16 && f.Name[16..] == gesucht
                     && string.CompareOrdinal(f.Name, antwortName) < 0)
            .LastOrDefault();
    }

    public string? LeseArchiv(string name)
    {
        if (!ArchivName.IsMatch(name ?? "")) return null;
        var p = Path.Combine(_root, "archiv", name!);
        return File.Exists(p) ? File.ReadAllText(p) : null;
    }

    private static void VersucheRechte600(string pfad)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return;
            File.SetUnixFileMode(pfad,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { /* Rechte best-effort */ }
    }
}

/// <summary>Stand eines Foundation-Prüfpunkts im Kommunikations-Test (Walter 29.09.2026).</summary>
public class ElmFoundationEintrag
{
    /// <summary>«ok», «fehler» oder «offen».</summary>
    public string Status { get; set; } = "offen";
    public string? Notiz { get; set; }
    /// <summary>Kurzfassung des letzten Versuchs (was OneCrew angezeigt hat).</summary>
    public string? LetzterVersuch { get; set; }
    public DateTime? LetzterVersuchAm { get; set; }
    public DateTime GeaendertAm { get; set; } = DateTime.Now;
}

public record ElmArchivDatei(string Name, DateTime Zeit, long Bytes,
    bool SignatureConfirmation, bool Verschluesselt, int Signaturen)
{
    /// <summary>F04_02: Stand der SignatureConfirmation gegen die zugehörige Anfrage (nur Antworten).</summary>
    public string? ScStand { get; init; }
    public string? ScMeldung { get; init; }
    /// <summary>Archivierte Anfrage, gegen die geprüft wurde.</summary>
    public string? Anfrage { get; init; }
}

/// <summary>Persistierter Stand eines SUA-Antrags (F07).</summary>
public class ElmSuaFall
{
    public string CertificateRequestId { get; set; } = "";
    public string CredentialKey { get; set; } = "";
    public string CredentialPassword { get; set; } = "";
    public string? LetzterState { get; set; }
    public string? AddresseeId { get; set; }
    public string? AddresseeIdentification { get; set; }
    public string? Uid { get; set; }
    public string? CompanyName { get; set; }
    /// <summary>Kontakt der Registrierung — belegt das Feld nach einem Neuladen wieder vor.</summary>
    public string? ContactName { get; set; }
    /// <summary>Subject-DN-Teile aus der Quittung (für den CSR).</summary>
    public ElmSuaSubject? Subject { get; set; }

    /// <summary>
    /// Wurde der Fall als TESTFALL angemeldet? Muss gemerkt werden, weil jedes
    /// folgende Synchronize dieselbe Marke tragen muss wie die Anmeldung.
    /// ACHTUNG (Richtlinie Anhang C.2.1.2): Ein Testfall lässt sich «starten, jedoch
    /// nicht abschliessen» — statt der Quittung kommt immer ein Fehlercode. Wer hier
    /// blind TRUE setzt, bekommt NIE ein Zertifikat.
    /// </summary>
    public bool AlsTestfall { get; set; }

    /// <summary>
    /// Versicherungszweig des Empfängers (UVG-LAA / UVGZ-LAAC / KTG-AMC / BVG-LPP).
    /// Das Synchronize verlangt ihn im Addressee (`sd:Domain`) — dort ist der Addressee
    /// ein ANDERER Typ als im Register (kein addresseeID, kein ProcessByDistributor).
    /// </summary>
    public string? Domain { get; set; }

    /// <summary>
    /// Wurde der CSR OHNE StateOrProvince angenommen? Dann muss auch die Erneuerung
    /// ohne ST laufen — sonst kippt sie aus demselben Grund (Walter 28.09.2026).
    /// </summary>
    public bool CsrOhneStateOrProvince { get; set; }

    /// <summary>Wurde der CSR OHNE ORG_ID angenommen? Die Erneuerung nimmt dieselbe Variante.</summary>
    public bool CsrOhneOrgId { get; set; }

    /// <summary>Wurde das PEM-Feld als binärer CSR (DER) angenommen? Gilt auch fürs Erneuern.</summary>
    public bool CsrPemAlsDer { get; set; }

    /// <summary>
    /// CSR-Varianten (<see cref="ElmCsrVariante.Kennung"/>), die Swissdec bei DIESEM Antrag
    /// mit 2052 abgewiesen hat — werden nicht nochmals geschickt (Walter 29.09.2026).
    /// </summary>
    public List<string> CsrAbgewiesen { get; set; } = new();

    /// <summary>
    /// Aufbau der Anfrage, unter dem <see cref="CsrAbgewiesen"/> gesammelt wurde. Ändert
    /// sich der Aufbau (<see cref="ElmCsrVariante.AufbauAktuell"/>), zählen frühere
    /// Abweisungen nicht mehr.
    /// </summary>
    public int CsrAufbau { get; set; }

    /// <summary>
    /// StoryIDs, die der Distributor in diesem Fall geliefert hat (v.a. die Quittung).
    /// Jedes folgende Synchronize quittiert sie in <c>CaseContext/ReceivedStoryIDs</c>
    /// (Richtlinie ELM 6.0, UC008 Schritt 3).
    /// </summary>
    public List<string> ErhalteneStoryIds { get; set; } = new();

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Aufbau des CSR-Subjects. Die Richtlinie ist nicht eindeutig: Anhang C.3.2 zeigt das
/// fertige Zertifikat MIT ORG_ID und ST/L optional, C.3.3 verlangt einen CSR «gemäss
/// Quittung» — und die Quittung enthält keine ORG_ID. Die SUA-Spezifikation V1.5
/// (Tabellen 2 und 3) verlangt die ORG_ID im CSR — darum kommt sie zuerst.
/// </summary>
/// <param name="PemAlsDer">
/// Feld <c>PEM</c> (xs:base64Binary) als Base64 des binären CSR statt als Base64 des
/// PEM-Textes. Die Schema-Doku sagt nur «im PEM Format» — beides ist lesbar.
/// </param>
public record ElmCsrVariante(string Kennung, string Text, bool OhneSt, bool OhneOrgId, bool PemAlsDer = false)
{
    /// <summary>
    /// 1 = ohne Quittierung der Stories: am 29.09.2026 alle vier Varianten mit 2052
    /// abgewiesen. 2 = mit <c>ReceivedStoryIDs</c> (UC008).
    /// </summary>
    public const int AufbauAktuell = 2;

    // Angenommen am 29.09.2026 17:30: «der-richtlinie» (ORG_ID, ST «nA», PEM-Feld = DER).
    // Alle PEM-Text-Varianten waren vorher mit 2052 abgewiesen — sie bleiben nur als Rückfall.
    public static readonly IReadOnlyList<ElmCsrVariante> Reihenfolge = new[]
    {
        new ElmCsrVariante("der-richtlinie", "mit ORG_ID, PEM-Feld als binärer CSR (DER)", false, false, true),
        new ElmCsrVariante("der-quittung", "wie Quittung, PEM-Feld als binärer CSR (DER)", false, true, true),
        new ElmCsrVariante("richtlinie", "mit ORG_ID (SUA-Spez. Tabelle 2/3)", false, false),
        new ElmCsrVariante("richtlinie-ohne-st", "mit ORG_ID, ohne ST", true, false),
        new ElmCsrVariante("quittung", "exakt wie Quittung (ohne ORG_ID)", false, true),
        new ElmCsrVariante("quittung-ohne-st", "wie Quittung, ohne ST und ohne ORG_ID", true, true),
    };

    /// <summary>
    /// Noch nicht abgewiesene Varianten dieses Antrags, in Versuchsreihenfolge.
    /// Ohne ST bzw. ohne UID in der Quittung wären zwei Varianten derselbe CSR — die
    /// jeweils zweite fällt dann weg.
    /// </summary>
    public static List<ElmCsrVariante> Offene(ElmSuaFall fall)
    {
        var hatSt = !string.IsNullOrWhiteSpace(fall.Subject?.StateOrProvinceName);
        var hatOrgId = fall.Subject?.OrganizationIdentifier != null;
        return Reihenfolge
            .Where(v => !fall.CsrAbgewiesen.Contains(v.Kennung))
            .Where(v => hatSt || !v.OhneSt)
            .Where(v => hatOrgId || v.OhneOrgId)
            .ToList();
    }

    /// <summary>Die bei der Ausstellung angenommene Variante — für die Erneuerung.</summary>
    public static ElmCsrVariante Angenommen(ElmSuaFall fall) =>
        Reihenfolge.First(v => v.OhneSt == fall.CsrOhneStateOrProvince && v.OhneOrgId == fall.CsrOhneOrgId
                            && v.PemAlsDer == fall.CsrPemAlsDer);
}

public class ElmSuaSubject
{
    public string CommonName { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string LocalityName { get; set; } = "";
    public string StateOrProvinceName { get; set; } = "";
    public string CountryName { get; set; } = "";
    public string? BusinessCategory { get; set; }

    /// <summary>
    /// UID aus der Quittung (Element <c>CompanyUID-BFS</c> neben dem X509Subject),
    /// z.B. <c>CHE-999.999.996</c>. Aus ihr entsteht die ORG_ID.
    /// </summary>
    public string? CompanyUidBfs { get; set; }

    /// <summary>organizationIdentifier — RFC 4519, in X.500 die OID 2.5.4.97.</summary>
    public const string OrgIdOid = "2.5.4.97";

    /// <summary>Vorsilbe laut Transmitter-Richtlinien ELM 6.0, Anhang C.3.3.</summary>
    public const string OrgIdPrefix = "NTRCH-";

    /// <summary>
    /// ORG_ID des Zertifikatsantrags: «NTRCH-» + UID aus der Quittung
    /// (z.B. <c>NTRCH-CHE-999.999.996</c>). Ohne UID null — dann fehlt das Feld,
    /// und Swissdec weist den Antrag mit «NOT_plausible 2052» ab
    /// (Walter-Befund 28.09.2026: genau daran scheiterte F07_06).
    /// </summary>
    public string? OrganizationIdentifier =>
        string.IsNullOrWhiteSpace(CompanyUidBfs) ? null : OrgIdPrefix + CompanyUidBfs!.Trim();

    /// <summary>
    /// Subject für den CSR. Reihenfolge wie in den Transmitter-Richtlinien
    /// Anhang C.3.2/C.3.3 aufgeführt: C, ST, L, CN, O, organizationIdentifier.
    /// </summary>
    /// <param name="ohneStateOrProvince">
    /// ST weglassen. Die Quittung liefert dort teils «nA» — laut Tabelle ist das
    /// Feld optional, und Swissdec nimmt den Antrag ohne ST womöglich an, wenn er
    /// mit «nA» abgewiesen wird.
    /// </param>
    /// <param name="ohneOrgId">
    /// ORG_ID weglassen: CSR exakt wie das X509Subject der Quittung (Anhang C.3.3
    /// «muss der Quittung entsprechen»), die keine ORG_ID enthält.
    /// </param>
    public X500DistinguishedName AlsX500Name(bool ohneStateOrProvince = false, bool ohneOrgId = false)
    {
        // X500DistinguishedNameBuilder kodiert in UMGEKEHRTER Aufrufreihenfolge —
        // darum rückwärts hinzufügen, damit im CSR C, ST, L, CN, O, ORG_ID steht.
        var b = new X500DistinguishedNameBuilder();
        if (!ohneOrgId && OrganizationIdentifier is { } orgId) b.Add(OrgIdOid, orgId);
        if (!string.IsNullOrWhiteSpace(OrganizationName)) b.AddOrganizationName(OrganizationName.Trim());
        if (!string.IsNullOrWhiteSpace(CommonName)) b.AddCommonName(CommonName.Trim());
        if (!string.IsNullOrWhiteSpace(LocalityName)) b.AddLocalityName(LocalityName.Trim());
        if (!ohneStateOrProvince && !string.IsNullOrWhiteSpace(StateOrProvinceName))
            b.AddStateOrProvinceName(StateOrProvinceName.Trim());
        if (!string.IsNullOrWhiteSpace(CountryName)) b.AddCountryOrRegion(CountryName.Trim());
        return b.Build();
    }

    /// <summary>Lesbare Fassung desselben Subjects — für Anzeige, Log und Tests.</summary>
    public string AlsDn(bool ohneStateOrProvince = false, bool ohneOrgId = false)
    {
        var teile = new List<string>();
        if (!string.IsNullOrWhiteSpace(CommonName)) teile.Add("CN=" + Esc(CommonName));
        if (!string.IsNullOrWhiteSpace(OrganizationName)) teile.Add("O=" + Esc(OrganizationName));
        if (!string.IsNullOrWhiteSpace(LocalityName)) teile.Add("L=" + Esc(LocalityName));
        if (!ohneStateOrProvince && !string.IsNullOrWhiteSpace(StateOrProvinceName))
            teile.Add("S=" + Esc(StateOrProvinceName));
        if (!string.IsNullOrWhiteSpace(CountryName)) teile.Add("C=" + Esc(CountryName));
        if (!ohneOrgId && OrganizationIdentifier is { } orgId) teile.Add($"OID.{OrgIdOid}=" + Esc(orgId));
        return string.Join(", ", teile);
    }

    private static string Esc(string v) =>
        v.Contains(',') || v.Contains('=') || v.Contains('+') ? "\"" + v.Replace("\"", "\\\"") + "\"" : v;
}

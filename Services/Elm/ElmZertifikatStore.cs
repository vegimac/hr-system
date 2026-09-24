using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
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

    public object ErpInfo()
    {
        var z = LadeErp();
        if (z == null) return new { vorhanden = false, pfad = _root };
        return new
        {
            vorhanden = true,
            pfad = _root,
            subject = z.Subject,
            notBefore = z.NotBefore,
            notAfter = z.NotAfter,
            thumbprint = z.Thumbprint,
        };
    }

    // ── Empfängerzertifikat (für WS-Encryption) ──────────────────────────────

    public bool HatEmpfaengerZertifikat() => File.Exists(Path.Combine(_root, EmpfaengerDatei));

    public X509Certificate2? LadeEmpfaenger()
    {
        var p = Path.Combine(_root, EmpfaengerDatei);
        if (!File.Exists(p)) return null;
        return X509CertificateLoader.LoadCertificateFromFile(p);
    }

    public void SpeichereEmpfaenger(byte[] derOderPem)
    {
        var p = Path.Combine(_root, EmpfaengerDatei);
        File.WriteAllBytes(p, derOderPem);
        VersucheRechte600(p);
    }

    /// <summary>
    /// RefApps liefert sein Zertifikat in jeder signierten Antwort als
    /// BinarySecurityToken — einmal übernehmen, dann können wir verschlüsseln.
    /// </summary>
    public bool UebernehmeEmpfaengerAusAntwort(XmlDocument antw)
    {
        if (HatEmpfaengerZertifikat()) return false;
        var z = ElmWsSecurity.ZertifikatAusAntwort(antw);
        if (z == null) return false;
        SpeichereEmpfaenger(z.RawData);
        return true;
    }

    /// <summary>
    /// Eingebettetes RefApps-Receiver-Zertifikat (Assets), Fallback wenn noch
    /// keines gespeichert ist — sonst scheitert der erste Register-Aufruf.
    /// </summary>
    public static X509Certificate2? LadeRefAppsEmpfaengerFallback()
    {
        foreach (var kandidat in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "Assets", "Swissdec", "RefApps-Receiver.cer"),
                     Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Swissdec", "RefApps-Receiver.cer"),
                 })
        {
            if (!File.Exists(kandidat)) continue;
            try { return X509CertificateLoader.LoadCertificateFromFile(kandidat); }
            catch { /* nächster Pfad */ }
        }
        return null;
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
    /// <summary>Subject-DN-Teile aus der Quittung (für den CSR).</summary>
    public ElmSuaSubject? Subject { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public class ElmSuaSubject
{
    public string CommonName { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string LocalityName { get; set; } = "";
    public string StateOrProvinceName { get; set; } = "";
    public string CountryName { get; set; } = "";
    public string? BusinessCategory { get; set; }

    /// <summary>DN-String für CertificateRequest — Reihenfolge wie üblich CN, O, L, S, C.</summary>
    public string AlsDn()
    {
        var teile = new List<string>();
        if (!string.IsNullOrWhiteSpace(CommonName)) teile.Add("CN=" + Esc(CommonName));
        if (!string.IsNullOrWhiteSpace(OrganizationName)) teile.Add("O=" + Esc(OrganizationName));
        if (!string.IsNullOrWhiteSpace(LocalityName)) teile.Add("L=" + Esc(LocalityName));
        if (!string.IsNullOrWhiteSpace(StateOrProvinceName)) teile.Add("S=" + Esc(StateOrProvinceName));
        if (!string.IsNullOrWhiteSpace(CountryName)) teile.Add("C=" + Esc(CountryName));
        return string.Join(", ", teile);
    }

    private static string Esc(string v) =>
        v.Contains(',') || v.Contains('=') || v.Contains('+') ? "\"" + v.Replace("\"", "\\\"") + "\"" : v;
}

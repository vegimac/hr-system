using System.Security.Cryptography;
using System.Text;

namespace HrSystem.Services;

/// <summary>
/// Einfacher AES-256-CBC-Wrapper für Secrets, die auf dem Server
/// "at rest" verschlüsselt liegen sollen (z.B. SMTP-Passwort in der DB).
///
/// Schlüssel wird aus Jwt:Secret abgeleitet (SHA-256 → 32 Byte).
/// Format: Base64( IV(16 bytes) || Cipher ).
///
/// Dies ist KEIN Key-Management-System. Wer Zugriff auf den App-Server
/// hat (= appsettings.json kann lesen) kann auch entschlüsseln. Das ist
/// bewusst — wir wollen Defense-in-Depth gegen DB-Dumps, nicht gegen
/// einen kompromittierten Server. Für letzteres bräuchte's ein HSM
/// oder einen externen Secret-Store, was hier overkill wäre.
/// </summary>
public class SimpleAesService
{
    private readonly byte[] _key;

    public SimpleAesService(IConfiguration config)
    {
        var secret = config["Jwt:Secret"] ?? "SchaUbHrSyStEmSeCrEtKeY2026!!SuperSecure";
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public string Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return "";
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = enc.TransformFinalBlock(data, 0, data.Length);
        var result = new byte[aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, result, aes.IV.Length, cipher.Length);
        return Convert.ToBase64String(result);
    }

    public string Decrypt(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return "";
        try
        {
            var data = Convert.FromBase64String(ciphertext);
            if (data.Length < 17) return "";
            using var aes = Aes.Create();
            aes.Key = _key;
            var iv = new byte[16];
            Buffer.BlockCopy(data, 0, iv, 0, 16);
            aes.IV = iv;
            using var dec = aes.CreateDecryptor();
            return Encoding.UTF8.GetString(dec.TransformFinalBlock(data, 16, data.Length - 16));
        }
        catch
        {
            // Wenn der Key gewechselt hat oder die Daten kaputt sind,
            // lieber leeres Passwort als Crash. Im Admin-UI muss dann
            // halt neu eingegeben werden.
            return "";
        }
    }
}

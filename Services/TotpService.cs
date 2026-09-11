using System.Security.Cryptography;
using System.Text;
using QRCoder;

namespace HrSystem.Services;

/// <summary>
/// Zweite Prüfung per Authenticator-App (Walter 11.09.2026): TOTP nach
/// RFC 6238 (SHA1, 6 Stellen, 30 Sekunden) — funktioniert mit Microsoft
/// Authenticator, Google Authenticator und jeder anderen otpauth-App.
/// Kein SMS, kein Hardware-Token. Das Secret liegt Base32-codiert in
/// app_user.totp_secret und verlässt den Server nur EINMAL: beim Einrichten
/// an den Benutzer, der gerade sein Passwort richtig eingegeben hat.
///
/// Hinweis Zeit: TOTP rechnet per Definition mit Unix-Sekunden (UTC). Das ist
/// KEIN DB-Zeitstempel — die ACHTUNG-TIME-Regel (DateTime.Now) gilt für die
/// Spalte totp_confirmed_at, nicht für die Code-Berechnung.
/// </summary>
public static class TotpService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int    SchrittSekunden = 30;
    private const int    Stellen         = 6;
    public  const string Issuer          = "OneCrew";

    /// <summary>Neues Secret: 20 Zufallsbytes (160 Bit) als Base32 ohne Padding.</summary>
    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return Base32Encode(bytes);
    }

    /// <summary>
    /// Prüft einen 6-stelligen Code gegen das Secret. Toleranz ±1 Schritt
    /// (30 s), damit eine leicht abweichende Handy-Uhr nicht aussperrt.
    /// Liefert den Zeitschritt zurück, der gepasst hat (für Replay-Schutz),
    /// oder null wenn der Code falsch ist.
    /// </summary>
    public static long? Verify(string? secretBase32, string? code)
    {
        if (string.IsNullOrWhiteSpace(secretBase32) || string.IsNullOrWhiteSpace(code)) return null;
        var eingabe = new string(code.Where(char.IsDigit).ToArray());
        if (eingabe.Length != Stellen) return null;

        byte[] key;
        try { key = Base32Decode(secretBase32); }
        catch { return null; }

        var jetzt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / SchrittSekunden;
        for (long delta = -1; delta <= 1; delta++)
        {
            var schritt = jetzt + delta;
            var soll = ComputeCode(key, schritt);
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(soll), Encoding.ASCII.GetBytes(eingabe)))
                return schritt;
        }
        return null;
    }

    /// <summary>otpauth-URI für Authenticator-Apps: Issuer OneCrew, Konto = E-Mail.</summary>
    public static string BuildOtpAuthUri(string secretBase32, string account)
    {
        var label = Uri.EscapeDataString(Issuer + ":" + account);
        return "otpauth://totp/" + label
             + "?secret=" + secretBase32
             + "&issuer=" + Uri.EscapeDataString(Issuer)
             + "&algorithm=SHA1&digits=" + Stellen
             + "&period=" + SchrittSekunden;
    }

    /// <summary>QR-Code als PNG-Data-URL (wird nur im Setup-Schritt an den pending-User geliefert).</summary>
    public static string BuildQrDataUrl(string otpauthUri)
    {
        using var gen  = new QRCodeGenerator();
        using var data = gen.CreateQrCode(otpauthUri, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(6);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    /// <summary>Secret in 4er-Gruppen zum Abtippen (z.B. «JBSW Y3DP EHPK 3PXP»).</summary>
    public static string FormatSecretForDisplay(string secretBase32)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < secretBase32.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(secretBase32[i]);
        }
        return sb.ToString();
    }

    // ── RFC 4226 HOTP-Kern ────────────────────────────────────────────────
    private static string ComputeCode(byte[] key, long schritt)
    {
        var counter = new byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(schritt & 0xFF);
            schritt >>= 8;
        }
        using var hmac = new HMACSHA1(key);
        var hash   = hmac.ComputeHash(counter);
        var offset = hash[hash.Length - 1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   |  (hash[offset + 3] & 0xFF);
        var otp = binary % 1_000_000;
        return otp.ToString("D6");
    }

    // ── Base32 (RFC 4648, ohne Padding) ───────────────────────────────────
    public static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int bits = 0, value = 0;
        foreach (var b in data)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(value >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
            sb.Append(Base32Alphabet[(value << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Base32Decode(string s)
    {
        var clean = s.Trim().Replace(" ", "").Replace("-", "").TrimEnd('=').ToUpperInvariant();
        var result = new List<byte>(clean.Length * 5 / 8);
        int bits = 0, value = 0;
        foreach (var c in clean)
        {
            var idx = Base32Alphabet.IndexOf(c);
            if (idx < 0) throw new FormatException("Ungültiges Base32-Zeichen.");
            value = (value << 5) | idx;
            bits += 5;
            if (bits >= 8)
            {
                result.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return result.ToArray();
    }
}

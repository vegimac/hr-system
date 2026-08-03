using System.Security.Cryptography;
using System.Text;

namespace HrSystem.Services;

/// <summary>
/// Gemeinsame Token-Erzeugung für öffentliche Download-Links
/// (Vertrags-Share, Lohnausweis-Share, …). Klartext nur im Link, SHA-256 in der DB.
/// </summary>
public static class ShareTokenUtil
{
    public static (string token, string hash) NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        var token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (token, HashToken(token));
    }

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

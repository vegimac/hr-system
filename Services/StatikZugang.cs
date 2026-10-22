using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HrSystem.Services;

/// <summary>
/// Hilfe-Texte und die Lohn-Engine-Skizze liegen in wwwroot, dürfen aber nicht
/// ohne Anmeldung ausgeliefert werden. Mitarbeiter-Postfach (Rolle employee)
/// ist ebenfalls ausgeschlossen — das sind HR-Unterlagen.
/// </summary>
public static class StatikZugang
{
    private static readonly string[] Rollen = ["admin", "superuser", "user", "buchhaltung", "lowuser"];

    public static bool IstGeschuetzt(PathString path)
    {
        var p = path.Value ?? "";
        return p.Equals("/lohn-engine-debugger.html", StringComparison.OrdinalIgnoreCase)
            || p.Equals("/help", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/help/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool RollePasst(ClaimsPrincipal principal) =>
        principal.FindAll(ClaimTypes.Role).Any(c => Rollen.Contains(c.Value));

    /// <summary>Bearer-Token prüfen. Ungültig oder abgelaufen → null.</summary>
    public static ClaimsPrincipal? Principal(string? authorization, string jwtSecret)
    {
        if (string.IsNullOrWhiteSpace(authorization)
            || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var token = authorization["Bearer ".Length..].Trim();
        if (token.Length == 0) return null;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            return handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero
            }, out _);
        }
        catch
        {
            return null;
        }
    }
}

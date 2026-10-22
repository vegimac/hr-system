using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HrSystem.Services;

/// <summary>
/// Dateien in wwwroot, die nicht ohne HR-Anmeldung ausgeliefert werden.
/// Mitarbeiter-Postfach (Rolle employee) ist ausgeschlossen.
/// Die Anmeldung selbst (app-core, WebAuthn) und die kleinen Helfer des
/// Importers bleiben offen, sonst lädt die Login-Seite nicht.
/// </summary>
public static class StatikZugang
{
    /// <summary>Lohnlauf: Admin, HR, Geschäftsführer, Buchhaltung. Nicht lowuser.</summary>
    public const string HrAppRollen = "admin,superuser,user,buchhaltung";

    private static readonly string[] Rollen = ["admin", "superuser", "user", "buchhaltung", "lowuser"];

    private static readonly HashSet<string> JsOffen = new(StringComparer.OrdinalIgnoreCase)
    {
        "/js/app-core.js",
        "/js/webauthn.js",
        "/js/save-blob.js",
        "/js/pdf-progress.js",
        "/js/file-preview.js",
        "/js/date-year-quickpick.js",
        "/js/liquid-select.js",
        "/js/personaldaten.js",
    };

    public static bool IstGeschuetzt(PathString path)
    {
        var p = path.Value ?? "";
        if (p.Equals("/lohn-engine-debugger.html", StringComparison.OrdinalIgnoreCase)
            || p.Equals("/help", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/help/", StringComparison.OrdinalIgnoreCase)
            || p.Equals("/import.html", StringComparison.OrdinalIgnoreCase)
            || p.Equals("/employees.js", StringComparison.OrdinalIgnoreCase))
            return true;
        if (p.StartsWith("/js/", StringComparison.OrdinalIgnoreCase))
            return !JsOffen.Contains(p);
        return false;
    }

    public static bool RollePasst(ClaimsPrincipal principal) =>
        principal.FindAll(ClaimTypes.Role).Any(c => Rollen.Contains(c.Value));

    /// <summary>Bearer-Kopfzeile oder Cookie hr_modul. Ungültig → null.</summary>
    public static ClaimsPrincipal? PrincipalAus(string? authorization, string? cookie, string jwtSecret)
    {
        if (!string.IsNullOrWhiteSpace(authorization)
            && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Principal(authorization, jwtSecret);
        if (string.IsNullOrWhiteSpace(cookie)) return null;
        return Principal("Bearer " + cookie.Trim(), jwtSecret);
    }

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

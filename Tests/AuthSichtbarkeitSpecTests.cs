using System.Text.RegularExpressions;
using HrSystem.Controllers;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Hält Login, Impersonate und GET /me auf derselben Filial-Regel
/// und verhindert den 401-Logout bei Tippfehler im aktuellen Passwort
/// (Walter 01.09.2026, Sichtbarkeits-Nacharbeit).
/// </summary>
public class AuthSichtbarkeitSpecTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "hr-system.csproj")))
                dir = Directory.GetParent(dir)?.FullName;
            if (dir is null) throw new InvalidOperationException("hr-system.csproj nicht gefunden.");
            return dir;
        }
    }

    private static string ReadAllText(string relPath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relPath));

    [Theory]
    [InlineData("admin", true)]
    [InlineData("superuser", true)]
    [InlineData("lowuser", true)]
    [InlineData("user", false)]
    [InlineData("buchhaltung", false)]
    [InlineData("employee", false)]
    [InlineData(null, false)]
    public void SeesAllBranches_MatchesMeRule(string? role, bool expected) =>
        Assert.Equal(expected, AuthController.SeesAllBranches(role));

    [Fact]
    public void Login_Me_Impersonate_UseSameBranchHelper()
    {
        var src = ReadAllText("Controllers/AuthController.cs");
        Assert.Equal(3, Regex.Matches(src, @"SeesAllBranches\((user|target)\.Role\)").Count);
        Assert.DoesNotContain("user.Role == \"admin\" || user.Role == \"lowuser\"", src);
        Assert.DoesNotContain("target.Role == \"admin\" || target.Role == \"lowuser\"", src);
    }

    [Fact]
    public void ChangePassword_WrongCurrentPassword_IsBadRequestNotUnauthorized()
    {
        var src = ReadAllText("Controllers/AuthController.cs");
        var idx = src.IndexOf("public async Task<IActionResult> ChangePassword(", StringComparison.Ordinal);
        Assert.True(idx > 0, "ChangePassword nicht gefunden.");
        var next = src.IndexOf("public ", idx + 1, StringComparison.Ordinal);
        if (next < 0) next = src.Length;
        var block = src.Substring(idx, next - idx);
        Assert.Contains("return BadRequest(new { message = \"Aktuelles Passwort ist falsch.\" });", block);
        Assert.DoesNotContain("return Unauthorized(new { message = \"Aktuelles Passwort ist falsch.\" });", block);
    }

    [Fact]
    public void Auth401Interceptor_SkipsChangePassword()
    {
        var src = ReadAllText("wwwroot/js/app-core.js");
        var idx = src.IndexOf("function installAuth401Interceptor", StringComparison.Ordinal);
        Assert.True(idx > 0, "401-Interceptor nicht gefunden.");
        var block = src.Substring(idx, Math.Min(1800, src.Length - idx));
        Assert.Contains("/api/auth/change-password", block);
        Assert.Contains("/api/auth/login", block);
        Assert.Contains("/api/auth/impersonate", block);
    }

    [Fact]
    public void DoLogin_LoadsCurrentUserFromMe()
    {
        var src = ReadAllText("wwwroot/js/app-core.js");
        var idx = src.IndexOf("async function doLogin()", StringComparison.Ordinal);
        Assert.True(idx > 0, "doLogin nicht gefunden.");
        var next = src.IndexOf("async function faceIdLogin()", idx, StringComparison.Ordinal);
        if (next < 0) next = idx + 2500;
        var block = src.Substring(idx, next - idx);
        Assert.Contains("await checkAuth()", block);
        Assert.DoesNotContain("currentUser = data.user;", block);
    }

    [Fact]
    public void UmSetAreas_Null_DoesNotCheckEntwicklung()
    {
        var src = ReadAllText("wwwroot/js/users.js");
        var idx = src.IndexOf("function umSetAreas(arr)", StringComparison.Ordinal);
        Assert.True(idx > 0, "umSetAreas nicht gefunden.");
        var next = src.IndexOf("function umAreasSetAll", idx, StringComparison.Ordinal);
        if (next < 0) next = idx + 800;
        var block = src.Substring(idx, next - idx);
        Assert.Contains("a !== 'entwicklung'", block);
        Assert.DoesNotContain("cb.checked = (arr == null) ? true", block);
    }
}

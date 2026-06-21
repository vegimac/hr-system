using HrSystem.Controllers;
using HrSystem.Models;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Tests für die benutzerbezogene Session-/Logout-Policy
/// (Walter-Vorgabe 21.06.2026).
///
/// Prüft die Rollen-Defaults, die User-Override-Werte und das Clamping
/// (5–1440) in AuthController.EffectiveIdleTimeout / EffectiveMaxSession —
/// genau diese Werte fliessen in die JWT-Ablaufzeit und in die Token-Claims.
/// </summary>
public class SessionPolicyTests
{
    private static AppUser U(string role, int? idle = null, int? max = null) =>
        new AppUser { Role = role, IdleTimeoutMinutes = idle, MaxSessionMinutes = max };

    // ── Rollen-Defaults (kein User-Wert gesetzt) ──────────────────────────
    [Fact]
    public void EmployeeDefaults_Idle15_Max30()
    {
        var u = U("employee");
        Assert.Equal(15, AuthController.EffectiveIdleTimeout(u));
        Assert.Equal(30, AuthController.EffectiveMaxSession(u));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("superuser")]
    [InlineData("admin")]
    [InlineData("buchhaltung")]
    [InlineData("lowuser")]
    public void NonEmployeeDefaults_Idle30_Max480(string role)
    {
        var u = U(role);
        Assert.Equal(30,  AuthController.EffectiveIdleTimeout(u));
        Assert.Equal(480, AuthController.EffectiveMaxSession(u));
    }

    // ── User-Override gewinnt über den Rollen-Default ─────────────────────
    [Fact]
    public void UserOverride_TakesPrecedence()
    {
        var u = U("employee", idle: 45, max: 120);
        Assert.Equal(45,  AuthController.EffectiveIdleTimeout(u));
        Assert.Equal(120, AuthController.EffectiveMaxSession(u));
    }

    // ── Clamping auf den gültigen Bereich 5–1440 ──────────────────────────
    [Fact]
    public void Override_BelowMinimum_ClampedTo5()
    {
        var u = U("user", idle: 1, max: 0);
        Assert.Equal(5, AuthController.EffectiveIdleTimeout(u));
        Assert.Equal(5, AuthController.EffectiveMaxSession(u));
    }

    [Fact]
    public void Override_AboveMaximum_ClampedTo1440()
    {
        var u = U("user", idle: 5000, max: 99999);
        Assert.Equal(1440, AuthController.EffectiveIdleTimeout(u));
        Assert.Equal(1440, AuthController.EffectiveMaxSession(u));
    }

    [Fact]
    public void Override_AtBounds_PassesThrough()
    {
        var lo = U("user", idle: AuthController.POLICY_MIN, max: AuthController.POLICY_MIN);
        Assert.Equal(5, AuthController.EffectiveIdleTimeout(lo));
        Assert.Equal(5, AuthController.EffectiveMaxSession(lo));

        var hi = U("user", idle: AuthController.POLICY_MAX, max: AuthController.POLICY_MAX);
        Assert.Equal(1440, AuthController.EffectiveIdleTimeout(hi));
        Assert.Equal(1440, AuthController.EffectiveMaxSession(hi));
    }
}

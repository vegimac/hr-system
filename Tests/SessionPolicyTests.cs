using HrSystem.Controllers;
using HrSystem.Models;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Tests für die benutzerbezogene Session-/Logout-Policy
/// (Walter-Vorgabe 21.06.2026, Sperrbildschirm 04.09.2026).
///
/// Inaktivität: 0–30 Minuten, 0 = kein Sperren durch Inaktivität, Standard 15 für alle.
/// Maximale Sitzung: Rollen-Default (MA 30, sonst 480), geklemmt auf 5–1440.
/// Genau diese Werte fliessen in die JWT-Ablaufzeit und in die Token-Claims.
/// </summary>
public class SessionPolicyTests
{
    private static AppUser U(string role, int? idle = null, int? max = null) =>
        new AppUser { Role = role, IdleTimeoutMinutes = idle, MaxSessionMinutes = max };

    // ── Defaults (kein User-Wert gesetzt) ─────────────────────────────────
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
    public void NonEmployeeDefaults_Idle15_Max480(string role)
    {
        var u = U(role);
        Assert.Equal(15,  AuthController.EffectiveIdleTimeout(u));
        Assert.Equal(480, AuthController.EffectiveMaxSession(u));
    }

    // ── User-Wert gewinnt über den Default ────────────────────────────────
    [Fact]
    public void UserOverride_TakesPrecedence()
    {
        var u = U("employee", idle: 25, max: 120);
        Assert.Equal(25,  AuthController.EffectiveIdleTimeout(u));
        Assert.Equal(120, AuthController.EffectiveMaxSession(u));
    }

    [Fact]
    public void Idle_Null_Minuten_heisst_kein_Sperren()
    {
        var u = U("user", idle: 0);
        Assert.Equal(0, AuthController.EffectiveIdleTimeout(u));
    }

    // ── Klemmen: Inaktivität 0–30, Sitzung 5–1440 ─────────────────────────
    [Fact]
    public void Override_BelowMinimum_Clamped()
    {
        var u = U("user", idle: -5, max: 0);
        Assert.Equal(AuthController.IDLE_MIN, AuthController.EffectiveIdleTimeout(u));
        Assert.Equal(AuthController.POLICY_MIN, AuthController.EffectiveMaxSession(u));
    }

    [Fact]
    public void Override_AboveMaximum_Clamped()
    {
        var u = U("user", idle: 5000, max: 99999);
        Assert.Equal(AuthController.IDLE_MAX, AuthController.EffectiveIdleTimeout(u));
        Assert.Equal(AuthController.POLICY_MAX, AuthController.EffectiveMaxSession(u));
    }

    [Fact]
    public void Override_AtBounds_PassesThrough()
    {
        var lo = U("user", idle: AuthController.IDLE_MIN, max: AuthController.POLICY_MIN);
        Assert.Equal(0, AuthController.EffectiveIdleTimeout(lo));
        Assert.Equal(5, AuthController.EffectiveMaxSession(lo));

        var hi = U("user", idle: AuthController.IDLE_MAX, max: AuthController.POLICY_MAX);
        Assert.Equal(30,   AuthController.EffectiveIdleTimeout(hi));
        Assert.Equal(1440, AuthController.EffectiveMaxSession(hi));
    }
}

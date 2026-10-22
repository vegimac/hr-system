using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HrSystem.Services;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace HrSystem.Tests;

public class StatikZugangTests
{
    private const string Secret = "test-geheimnis-mit-mindestens-32-zeichen";

    [Theory]
    [InlineData("/help/lohnlauf.md", true)]
    [InlineData("/help", true)]
    [InlineData("/HELP/rollen.md", true)]
    [InlineData("/lohn-engine-debugger.html", true)]
    [InlineData("/js/payroll.js", true)]
    [InlineData("/js/app-core.js", false)]
    [InlineData("/js/webauthn.js", false)]
    [InlineData("/js/personaldaten.js", false)]
    [InlineData("/employees.js", true)]
    [InlineData("/import.html", true)]
    [InlineData("/lib/xlsx/xlsx.full.min.js", false)]
    [InlineData("/helpful", false)]
    [InlineData("/index.html", false)]
    public void Pfad_nur_hilfe_und_engine_skizze(string pfad, bool geschuetzt)
    {
        Assert.Equal(geschuetzt, StatikZugang.IstGeschuetzt(pfad));
    }

    [Fact]
    public void Cookie_gilt_wie_bearer()
    {
        var token = Token("admin");
        var ausCookie = StatikZugang.PrincipalAus(null, token, Secret);
        Assert.NotNull(ausCookie);
        Assert.True(StatikZugang.RollePasst(ausCookie!));
        Assert.Null(StatikZugang.PrincipalAus(null, null, Secret));
    }

    [Fact]
    public void Ohne_token_kein_zugang()
    {
        Assert.Null(StatikZugang.Principal(null, Secret));
        Assert.Null(StatikZugang.Principal("Bearer ", Secret));
        Assert.Null(StatikZugang.Principal("Bearer kaputt", Secret));
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("superuser", true)]
    [InlineData("user", true)]
    [InlineData("buchhaltung", true)]
    [InlineData("lowuser", true)]
    [InlineData("employee", false)]
    public void Rolle_hr_ja_mitarbeiter_nein(string rolle, bool erlaubt)
    {
        var principal = StatikZugang.Principal("Bearer " + Token(rolle), Secret);
        Assert.NotNull(principal);
        Assert.Equal(erlaubt, StatikZugang.RollePasst(principal!));
    }

    private static string Token(string rolle)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var jwt = new JwtSecurityToken(
            claims: [new Claim(ClaimTypes.Role, rolle)],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}

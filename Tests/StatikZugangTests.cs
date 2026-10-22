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
    [InlineData("/js/payroll.js", false)]
    [InlineData("/helpful", false)]
    [InlineData("/index.html", false)]
    public void Pfad_nur_hilfe_und_engine_skizze(string pfad, bool geschuetzt)
    {
        Assert.Equal(geschuetzt, StatikZugang.IstGeschuetzt(pfad));
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

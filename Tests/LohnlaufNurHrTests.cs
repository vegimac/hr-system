using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class LohnlaufNurHrTests
{
    [Fact]
    public void CompanyProfile_LohnlaufNurHr_DefaultFalse()
    {
        Assert.False(new CompanyProfile().LohnlaufNurHr);
    }

    [Fact]
    public void IstHr_AdminUndBuchhaltung_Ja_User_Nein()
    {
        var admin = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "admin") },
                "test"));
        var gf = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "user") },
                "test"));
        var buch = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[]
                {
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "buchhaltung"),
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "superuser"),
                },
                "test"));
        Assert.True(LohnlaufBestaetigung.IstHr(admin));
        Assert.False(LohnlaufBestaetigung.IstHr(gf));
        Assert.True(LohnlaufBestaetigung.IstHr(buch));
    }
}

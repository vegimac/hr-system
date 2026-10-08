using System.Reflection;
using System.Security.Claims;
using HrSystem.Controllers;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Sicherheitsbefunde 08.10.2026:
/// 1. Buchhaltung (erste Rolle «buchhaltung», zweite «superuser») konnte sich zum Admin machen,
///    weil die Benutzerverwaltung nur die erste Rolle las.
/// 2. GF konnte bei MA anderer Filialen IBAN ändern, Dokumente ansehen, Postfach-Passwort
///    zurücksetzen — der Server prüfte die Filiale des MA nicht.
/// </summary>
public class MaFilialZugriffTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string t = "")
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("MaFilial_" + t + "_" + Guid.NewGuid()).Options);

    private static ClaimsPrincipal P(int id, params string[] rollen)
        => new(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Name, "u" + id) }
                .Concat(rollen.Select(r => new Claim(ClaimTypes.Role, r))), "test"));

    // ── Wer ist eingeschränkt ────────────────────────────────────────────

    [Fact]
    public void Eingeschraenkt_sind_nur_GF_und_Buchhaltung()
    {
        Assert.True(MaFilialZugriff.IstEingeschraenkt(P(1, "user")));
        Assert.True(MaFilialZugriff.IstEingeschraenkt(P(1, "buchhaltung", "superuser")));
        Assert.False(MaFilialZugriff.IstEingeschraenkt(P(1, "admin")));
        Assert.False(MaFilialZugriff.IstEingeschraenkt(P(1, "superuser")));
        Assert.True(MaFilialZugriff.IstEingeschraenkt(P(1, "lowuser")));
        Assert.False(MaFilialZugriff.IstEingeschraenkt(P(1, "employee")));
    }

    // ── MA-Zugriff ───────────────────────────────────────────────────────

    private static async Task<AppDbContext> DbMitMa()
    {
        var db = NewDb();
        db.UserBranchAccesses.Add(new UserBranchAccess { UserId = 7, CompanyProfileId = 1 });
        db.Employments.AddRange(
            new Employment { Id = 10, EmployeeId = 100, CompanyProfileId = 1 },   // MA 100 in Filiale 1
            new Employment { Id = 20, EmployeeId = 200, CompanyProfileId = 2 },   // MA 200 nur in Filiale 2
            new Employment { Id = 30, EmployeeId = 300, CompanyProfileId = 1 },   // MA 300: früher 1, jetzt 2 (Übertritt)
            new Employment { Id = 31, EmployeeId = 300, CompanyProfileId = 2 });
        db.EmployeeBankAccounts.Add(new EmployeeBankAccount { Id = 5, EmployeeId = 200, Iban = "CH00" });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task GF_sieht_nur_MA_seiner_Filialen()
    {
        await using var db = await DbMitMa();
        var z = new MaFilialZugriff(db);
        var gf = P(7, "user");
        Assert.True(await z.DarfMitarbeiterAsync(gf, 100));
        Assert.False(await z.DarfMitarbeiterAsync(gf, 200));
        Assert.True(await z.DarfMitarbeiterAsync(gf, 300));   // Übertritt: alte Filiale sieht ihn weiter
        Assert.True(await z.DarfMitarbeiterAsync(gf, 999));   // noch ohne Vertrag (Import)
        Assert.True(await z.DarfMitarbeiterAsync(P(8, "superuser"), 200));
        Assert.False(await z.DarfMitarbeiterAsync(P(7, "buchhaltung", "superuser"), 200));
    }

    [Fact]
    public async Task Eintrag_wird_auf_seinen_MA_zurueckgefuehrt()
    {
        await using var db = await DbMitMa();
        var z = new MaFilialZugriff(db);
        Assert.Equal(200, await z.MitarbeiterVonEintragAsync(typeof(EmployeeBankAccount), 5));
        Assert.Equal(200, await z.MitarbeiterVonEintragAsync(typeof(Employment), 20));
        Assert.Equal(42, await z.MitarbeiterVonEintragAsync(typeof(Employee), 42));
        Assert.Null(await z.MitarbeiterVonEintragAsync(typeof(EmployeeBankAccount), 999));
    }

    // ── Globaler Filter ──────────────────────────────────────────────────

    [MaEintrag(typeof(EmployeeBankAccount))]
    private sealed class BankDummy
    {
        public void Update(int id, BankDto dto) { }
        public void Reset(int employeeId) { }
        [OhneMaFilialPruefung] public void Frei(int employeeId) { }
    }
    private sealed record BankDto(int EmployeeId);

    private static async Task<bool> LaeuftDurch(AppDbContext db, ClaimsPrincipal u, string methode,
        Dictionary<string, object?> args)
    {
        var mi = typeof(BankDummy).GetMethod(methode)!;
        var ad = new ControllerActionDescriptor
        {
            MethodInfo = mi,
            ControllerTypeInfo = typeof(BankDummy).GetTypeInfo(),
            Parameters = mi.GetParameters()
                .Select(p => new ParameterDescriptor { Name = p.Name!, ParameterType = p.ParameterType })
                .ToList(),
        };
        var actx = new ActionContext(new DefaultHttpContext { User = u }, new RouteData(), ad);
        var ctx = new ActionExecutingContext(actx, new List<IFilterMetadata>(), args, new object());
        var aufgerufen = false;
        await new MaFilialFilter(new MaFilialZugriff(db)).OnActionExecutionAsync(ctx, () =>
        {
            aufgerufen = true;
            return Task.FromResult(new ActionExecutedContext(actx, new List<IFilterMetadata>(), new object()));
        });
        if (!aufgerufen)
            Assert.Equal(403, Assert.IsType<ObjectResult>(ctx.Result).StatusCode);
        return aufgerufen;
    }

    [Fact]
    public async Task Filter_sperrt_fremde_IBAN_und_Passwort_Reset()
    {
        await using var db = await DbMitMa();
        var gf = P(7, "user");

        // fremde Bankverbindung (MA 200) über ihre Id ändern
        Assert.False(await LaeuftDurch(db, gf, "Update", new() { ["id"] = 5, ["dto"] = new BankDto(100) }));
        // eigene Bankverbindung, aber Body zeigt auf fremden MA
        Assert.False(await LaeuftDurch(db, gf, "Reset", new() { ["employeeId"] = 200 }));
        Assert.True(await LaeuftDurch(db, gf, "Reset", new() { ["employeeId"] = 100 }));
        // Admin und Superuser sehen alles; Ausnahme-Attribut überspringt
        Assert.True(await LaeuftDurch(db, P(1, "admin"), "Update", new() { ["id"] = 5, ["dto"] = new BankDto(200) }));
        Assert.True(await LaeuftDurch(db, gf, "Frei", new() { ["employeeId"] = 200 }));
    }

    [Fact]
    public async Task Filter_prueft_EmployeeId_im_Body()
    {
        await using var db = await DbMitMa();
        db.EmployeeBankAccounts.Add(new EmployeeBankAccount { Id = 6, EmployeeId = 100, Iban = "CH01" });
        await db.SaveChangesAsync();
        Assert.False(await LaeuftDurch(db, P(7, "user"), "Update", new() { ["id"] = 6, ["dto"] = new BankDto(200) }));
        Assert.True(await LaeuftDurch(db, P(7, "user"), "Update", new() { ["id"] = 6, ["dto"] = new BankDto(100) }));
    }

    // ── Benutzerverwaltung ───────────────────────────────────────────────

    private static UsersController Users(AppDbContext db, ClaimsPrincipal u)
        => new(db) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = u } } };

    private static UsersController.CreateUserRequest Neu(string rolle, params int[] filialen)
        => new("neu", "N", "U", $"neu{Guid.NewGuid():N}@x.ch", null, "Passwort-123!", rolle, filialen.ToList());

    [Theory]
    [InlineData("admin")]
    [InlineData("Admin ")]
    public async Task Buchhaltung_kann_keinen_Admin_anlegen(string rolle)
    {
        await using var db = NewDb();
        var r = await Users(db, P(7, "buchhaltung", "superuser")).Create(Neu(rolle, 1));
        Assert.IsType<ForbidResult>(r);
    }

    [Fact]
    public async Task Buchhaltung_bleibt_in_ihren_Filialen()
    {
        await using var db = NewDb();
        db.UserBranchAccesses.Add(new UserBranchAccess { UserId = 7, CompanyProfileId = 1 });
        await db.SaveChangesAsync();
        var buch = Users(db, P(7, "buchhaltung", "superuser"));

        Assert.Equal(403, Assert.IsType<ObjectResult>(await buch.Create(Neu("superuser"))).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await buch.Create(Neu("user", 1, 2))).StatusCode);
        Assert.IsType<OkObjectResult>(await buch.Create(Neu("user", 1)));
    }

    [Fact]
    public async Task Buchhaltung_kann_sich_nicht_selbst_befoerdern()
    {
        await using var db = NewDb();
        db.AppUsers.Add(new AppUser { Id = 7, Username = "buch", Email = "b@x.ch", PasswordHash = "x", Role = "buchhaltung", IsActive = true });
        db.UserBranchAccesses.Add(new UserBranchAccess { UserId = 7, CompanyProfileId = 1 });
        await db.SaveChangesAsync();
        var buch = Users(db, P(7, "buchhaltung", "superuser"));

        UsersController.UpdateUserRequest Upd(string rolle, params int[] f)
            => new("buch", null, null, "b@x.ch", null, null, rolle, true, f.ToList());

        Assert.IsType<ForbidResult>(await buch.Update(7, Upd("admin", 1)));
        Assert.Equal(403, Assert.IsType<ObjectResult>(await buch.Update(7, Upd("superuser", 1))).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await buch.Update(7, Upd("buchhaltung", 1, 2))).StatusCode);
        Assert.Equal("buchhaltung", (await db.AppUsers.FindAsync(7))!.Role);
    }

    [Fact]
    public async Task Superuser_bleibt_wie_bisher_Admin_nur_durch_Admin()
    {
        await using var db = NewDb();
        Assert.IsType<ForbidResult>(await Users(db, P(8, "superuser")).Create(Neu("admin")));
        Assert.IsType<OkObjectResult>(await Users(db, P(8, "superuser")).Create(Neu("superuser")));
        Assert.IsType<OkObjectResult>(await Users(db, P(1, "admin")).Create(Neu("admin")));
    }
}

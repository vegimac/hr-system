using System.Security.Claims;
using HrSystem.Controllers;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// HR-Hub → Auswertungen «Stempelzeiten alle Filialen» (Walter 08.10.2026):
/// pro Filiale dieselben Zahlen wie die Einzelberichte, Buchhaltung nur eigene Filialen.
/// </summary>
public class StempelFilialvergleichTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string t = "")
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("StempelFv_" + t + "_" + Guid.NewGuid()).Options);

    private static StempelBerichteController Controller(AppDbContext db, int userId, params string[] rollen)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }
                .Concat(rollen.Select(r => new Claim(ClaimTypes.Role, r))), "test"));
        return new StempelBerichteController(db, new StempelBerichtPdfService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    private static EmployeeTimeEntry Stempel(int id, int ma, int cp, int tag) => new()
    {
        Id = id, EmployeeId = ma, SourceCompanyProfileId = cp, EntryDate = new DateOnly(2026, 9, tag),
        TimeIn = new DateTime(2026, 9, tag, 9, 0, 0), TimeOut = new DateTime(2026, 9, tag, 13, 0, 0)
    };

    private static async Task<AppDbContext> Daten()
    {
        var db = NewDb();
        db.CompanyProfiles.AddRange(
            new CompanyProfile { Id = 1, RestaurantCode = "122", City = "Lenzburg" },
            new CompanyProfile { Id = 2, RestaurantCode = "104", City = "Langenthal" },
            new CompanyProfile { Id = 3, RestaurantCode = "131", City = "Sursee" });
        db.Employees.AddRange(
            new Employee { Id = 100, FirstName = "Anna", LastName = "A" },
            new Employee { Id = 200, FirstName = "Beat", LastName = "B" });
        db.Employments.AddRange(
            new Employment { Id = 10, EmployeeId = 100, CompanyProfileId = 1, ContractStartDate = new DateTime(2026, 1, 1) },
            new Employment { Id = 20, EmployeeId = 200, CompanyProfileId = 2, ContractStartDate = new DateTime(2026, 1, 1) });
        var korrigiert = Stempel(2, 100, 1, 8);
        korrigiert.OriginalTimeIn = new DateTime(2026, 9, 8, 8, 30, 0);
        korrigiert.EditedBy = "Fuat Balci";
        var kommentar = Stempel(3, 100, 1, 9);
        kommentar.Comment = "vergessen auszustempeln";
        db.EmployeeTimeEntries.AddRange(
            Stempel(1, 100, 1, 7), korrigiert, kommentar, Stempel(4, 100, 1, 10),
            Stempel(5, 200, 2, 7), Stempel(6, 200, 2, 8));
        db.UserBranchAccesses.Add(new UserBranchAccess { UserId = 7, CompanyProfileId = 2 });
        await db.SaveChangesAsync();
        return db;
    }

    private static StempelFilialvergleichDaten Lesen(IActionResult r)
        => Assert.IsType<StempelFilialvergleichDaten>(Assert.IsType<OkObjectResult>(r).Value);

    [Fact]
    public async Task Zahlen_pro_Filiale_wie_im_Einzelbericht()
    {
        await using var db = await Daten();
        var c = Controller(db, 1, "superuser");
        var d = Lesen(await c.Filialvergleich("2026-09-01", "2026-09-30"));

        Assert.Equal(new[] { "104 Langenthal", "122 Lenzburg" }, d.Filialen.Select(f => f.Filiale));
        Assert.Equal(new[] { "131 Sursee" }, d.OhneStempel);

        var lenzburg = d.Filialen.Single(f => f.Id == 1);
        Assert.Equal(4, lenzburg.Stempel);
        Assert.Equal(2, lenzburg.Korrigiert);
        Assert.Equal(1, lenzburg.MaMitKorrektur);
        Assert.Equal(1, lenzburg.KorrekturProArt["ZEIT"]);
        Assert.Equal(1, lenzburg.KorrekturProArt["KOMMENTAR"]);
        var sept = Assert.Single(lenzburg.ProMonat);
        Assert.Equal(("2026-09", 4, 2), (sept.Monat, sept.Stempel, sept.Korrigiert));

        var einzelK = Assert.IsType<StempelKorrekturenDaten>(
            Assert.IsType<OkObjectResult>(await c.Korrekturen(1, "2026-09-01", "2026-09-30")).Value);
        var einzelV = Assert.IsType<StempelVerstoesseDaten>(
            Assert.IsType<OkObjectResult>(await c.Verstoesse(1, "2026-09-01", "2026-09-30")).Value);
        Assert.Equal(einzelK.StempelTotal, lenzburg.Stempel);
        Assert.Equal(einzelK.Korrigiert, lenzburg.Korrigiert);
        Assert.Equal(einzelV.ProArt.Sum(a => a.Anzahl), lenzburg.Verstoesse);
        Assert.Equal(0, d.Filialen.Single(f => f.Id == 2).Korrigiert);
    }

    [Fact]
    public async Task Buchhaltung_sieht_nur_eigene_Filialen()
    {
        await using var db = await Daten();
        var d = Lesen(await Controller(db, 7, "buchhaltung", "superuser").Filialvergleich("2026-09-01", "2026-09-30"));
        Assert.Equal(new[] { 2 }, d.Filialen.Select(f => f.Id));
        Assert.Empty(d.OhneStempel);
    }

    [Fact]
    public async Task Zeitraum_hoechstens_ein_Jahr()
    {
        await using var db = await Daten();
        var r = await Controller(db, 1, "admin").Filialvergleich("2025-01-01", "2026-09-30");
        Assert.IsType<BadRequestObjectResult>(r);
    }

    [Fact]
    public async Task Pdf_aus_dem_Controller()
    {
        await using var db = await Daten();
        var r = Assert.IsType<FileContentResult>(await Controller(db, 1, "admin").FilialvergleichPdf("2026-09-01", "2026-09-30"));
        Assert.Equal("application/pdf", r.ContentType);
        Assert.Equal("Stempelzeiten-Filialvergleich_2026-09-01_2026-09-30.pdf", r.FileDownloadName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(r.FileContents, 0, 4));
    }

    [Fact]
    public void Pdf_mit_vielen_Filialen_Monaten_und_allen_Arten()
    {
        var arten = ArbeitszeitVerstoesse.Reihenfolge
            .Select(a => new StempelFilialArt(a, ArbeitszeitVerstoesse.Titel(a), ArbeitszeitVerstoesse.Beschreibung(a))).ToList();
        var monate = new[] { "2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10", "2026-11", "2026-12" };
        var filialen = Enumerable.Range(1, 8).Select(i => new StempelFilialZeile(i, $"1{i}0 Filiale mit langem Namen {i}", 3000 + i * 100,
            40 + i * 15, 20 + i, new Dictionary<string, int> { ["ZEIT"] = 20 + i, ["MANUELL"] = 10, ["BEARBEITET"] = 5 + i, ["KOMMENTAR"] = 5 + i * 13 },
            60, 30 + i * 5, 10 + i,
            arten.ToDictionary(a => a.Art, a => (i * 7 + a.Art.Length) % 9),
            i == 3 ? new List<string> { ArbeitszeitVerstoesse.Jugend } : new List<string>(),
            monate.Select((m, mi) => new StempelFilialMonat(m, mi == 4 && i == 2 ? 0 : 250 + i, 5 + mi % 4 + i, 3)).ToList())).ToList();
        var d = new StempelFilialvergleichDaten(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), arten, filialen, new List<string> { "999 Neu" });

        var bytes = new StempelBerichtPdfService().Filialvergleich(d);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));

        var leer = new StempelBerichtPdfService().Filialvergleich(d with { Filialen = new() });
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(leer, 0, 4));
    }
}

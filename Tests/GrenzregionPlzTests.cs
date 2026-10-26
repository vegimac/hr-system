using HrSystem.Controllers;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// PLZ-Suche liest Schweizer Ortschaften und die Grenzregion.
/// Eine CH-PLZ bleibt ein Schweizer Treffer, eine ausländische PLZ kommt
/// aus der eigenen Tabelle.
/// </summary>
public class GrenzregionPlzTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string t = "")
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("Grenz_" + t + "_" + Guid.NewGuid()).Options);

    [Fact]
    public async Task ByPlz_liefert_Schweiz_und_Grenzregion()
    {
        await using var db = NewDb();
        db.SwissLocations.Add(new SwissLocation
        {
            Plz4 = "4665",
            Ortschaftsname = "Oftringen",
            Gemeindename = "Oftringen",
            BfsNr = 4280,
            Kantonskuerzel = "AG",
        });
        db.Grenzregionen.Add(new Grenzregion
        {
            Plz = "22100",
            Ort = "Como",
            Region = "Lombardei",
            LandIso = "IT",
        });
        await db.SaveChangesAsync();
        var api = new SwissLocationsController(db);

        var ch = Treffer(await api.GetByPlz("4665"));
        Assert.Single(ch);
        Assert.Equal("ch", ch[0].Quelle);
        Assert.Equal("CH", ch[0].Land);
        Assert.Equal("AG", ch[0].Region);
        Assert.Equal("Oftringen", ch[0].Ortschaftsname);

        var it = Treffer(await api.GetByPlz("22100"));
        Assert.Single(it);
        Assert.Equal("grenz", it[0].Quelle);
        Assert.Equal("IT", it[0].Land);
        Assert.Equal("Lombardei", it[0].Region);
        Assert.Equal("Como", it[0].Ortschaftsname);
        Assert.Null(it[0].BfsNr);
    }

    [Fact]
    public async Task ByName_findet_Grenzort()
    {
        await using var db = NewDb();
        db.Grenzregionen.Add(new Grenzregion
        {
            Plz = "21100",
            Ort = "Varese",
            Region = "Lombardei",
            LandIso = "IT",
        });
        await db.SaveChangesAsync();
        var api = new SwissLocationsController(db);
        var list = Treffer(await api.GetByName("Var"));
        Assert.Single(list);
        Assert.Equal("21100", list[0].Plz4);
        Assert.Equal("IT", list[0].Land);
    }

    private static List<PlzTreffer> Treffer(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsAssignableFrom<List<PlzTreffer>>(ok.Value);
    }
}

using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// «ledig» ist immer eine Korrektur (Walter 25.09.2026, Fall Pavikjevikj):
/// verheiratet → ledig darf keine Historie «verheiratet bis gestern» hinterlassen,
/// sonst verlangt der Lohn vergangener Monate einen Ehepartner.
/// </summary>
public class ZivilstandLedigKorrekturTests
{
    private static AppDbContext NeueDb(string t)
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("ZivLedig_" + t + "_" + Guid.NewGuid()).Options);

    [Fact]
    public async Task Verheiratet_auf_ledig_ohne_Historie_gilt_ab_jeher()
    {
        using var db = NeueDb(nameof(Verheiratet_auf_ledig_ohne_Historie_gilt_ab_jeher));
        db.Employees.Add(new Employee { Id = 1, FirstName = "Elena", LastName = "Test", EmployeeNumber = "1" });
        await db.SaveChangesAsync();

        var svc = new ZivilstandHistorieService(db);
        await svc.NachfuehrenAsync(1, "verheiratet", "Ledig", null, "aus MA-Maske");
        await db.SaveChangesAsync();

        var hist = await db.EmployeeZivilstandHistories.ToListAsync();
        var eintrag = Assert.Single(hist);
        Assert.Equal("ledig", eintrag.Zivilstand);
        Assert.Null(eintrag.GueltigAb);
        var (ziv, _, _) = await svc.AmAsync(1, new DateOnly(2026, 7, 1));
        Assert.Equal("ledig", ziv);
    }

    [Fact]
    public async Task Bestehende_Historie_mit_Heirat_wird_bei_ledig_ersetzt()
    {
        using var db = NeueDb(nameof(Bestehende_Historie_mit_Heirat_wird_bei_ledig_ersetzt));
        db.Employees.Add(new Employee { Id = 1, FirstName = "Elena", LastName = "Test", EmployeeNumber = "1" });
        db.EmployeeZivilstandHistories.Add(new EmployeeZivilstandHistory { EmployeeId = 1, Zivilstand = "ledig" });
        db.EmployeeZivilstandHistories.Add(new EmployeeZivilstandHistory { EmployeeId = 1, Zivilstand = "verheiratet", GueltigAb = new DateOnly(2025, 5, 1) });
        await db.SaveChangesAsync();

        var svc = new ZivilstandHistorieService(db);
        await svc.NachfuehrenAsync(1, "verheiratet", "ledig", null, "aus easy@work übernommen");
        await db.SaveChangesAsync();

        Assert.Equal("ledig", Assert.Single(await db.EmployeeZivilstandHistories.ToListAsync()).Zivilstand);
    }

    [Fact]
    public async Task Echte_Heirat_bleibt_datiert()
    {
        using var db = NeueDb(nameof(Echte_Heirat_bleibt_datiert));
        db.Employees.Add(new Employee { Id = 1, FirstName = "Anna", LastName = "Test", EmployeeNumber = "1" });
        await db.SaveChangesAsync();

        // Heirat heute erfasst: zählt ab «Erfahren am» (= heute), vorher bleibt ledig.
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var svc = new ZivilstandHistorieService(db);
        await svc.NachfuehrenAsync(1, "ledig", "verheiratet", heute, "aus MA-Maske");
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.EmployeeZivilstandHistories.CountAsync());
        Assert.Equal("ledig", (await svc.AmAsync(1, heute.AddDays(-1))).Zivilstand);
        Assert.Equal("verheiratet", (await svc.AmAsync(1, heute.AddDays(30))).Zivilstand);
    }
}

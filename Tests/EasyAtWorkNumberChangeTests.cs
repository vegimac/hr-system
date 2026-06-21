using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.EasyAtWork;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Tests für den Personalnummern-Wechsel beim easy@work-MA-Sync (Walter-Vorgabe
/// 21.06.2026). Die bisherige Nummer wird in der Tabelle employee_number_alias
/// gesichert, die neue als employee_number gesetzt. Guards verhindern Dubletten.
/// </summary>
public class EasyAtWorkNumberChangeTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
        => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("NumChange_" + testName + "_" + System.Guid.NewGuid()).Options);

    // ───────────────────── ShouldSaveNumberChange (Guards) ─────────────────────

    [Fact]
    public void NeueNummer_AndersAlsAlle_WirdGespeichert()
    {
        Assert.True(EasyAtWorkEmployeeSyncService.ShouldSaveNumberChange("1040025", "1220062", null));
    }

    [Fact]
    public void LeereNeueNummer_WirdNichtGespeichert()
    {
        Assert.False(EasyAtWorkEmployeeSyncService.ShouldSaveNumberChange("1040025", "  ", null));
    }

    [Fact]
    public void GleicheNummer_WirdNichtGespeichert()
    {
        Assert.False(EasyAtWorkEmployeeSyncService.ShouldSaveNumberChange("1040025", "1040025", null));
    }

    [Fact]
    public void NeueNummerSchonAlias_WirdNichtGespeichert()
    {
        var aliases = new[] { "1030011", "1040025" };
        Assert.False(EasyAtWorkEmployeeSyncService.ShouldSaveNumberChange("1220062", "1040025", aliases));
    }

    // ───────────────────── SaveNumberChange (Alias-Insert) ─────────────────────

    [Fact]
    public async Task SaveNumberChange_LegtAliasAn_UndSetztNeueNummer()
    {
        using var db = NewDb();
        var emp = new Employee { EmployeeNumber = "1040025", FirstName = "Max", LastName = "Muster" };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        EasyAtWorkEmployeeSyncService.SaveNumberChange(db, emp, "1220062");
        await db.SaveChangesAsync();

        Assert.Equal("1220062", emp.EmployeeNumber);
        var alias = Assert.Single(await db.EmployeeNumberAliases.Where(a => a.EmployeeId == emp.Id).ToListAsync());
        Assert.Equal("1040025", alias.Number);
        Assert.Equal("easyatwork_sync", alias.Source);
        Assert.NotNull(alias.ValidTo);
    }

    [Fact]
    public async Task ZweiterWechsel_BehaeltBeideAlteNummern()
    {
        using var db = NewDb();
        var emp = new Employee { EmployeeNumber = "1030011", FirstName = "Max", LastName = "Muster" };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        EasyAtWorkEmployeeSyncService.SaveNumberChange(db, emp, "1040025");
        await db.SaveChangesAsync();
        EasyAtWorkEmployeeSyncService.SaveNumberChange(db, emp, "1220062");
        await db.SaveChangesAsync();

        Assert.Equal("1220062", emp.EmployeeNumber);
        var nums = await db.EmployeeNumberAliases.Where(a => a.EmployeeId == emp.Id)
            .Select(a => a.Number).OrderBy(n => n).ToListAsync();
        Assert.Equal(new[] { "1030011", "1040025" }, nums);
    }
}

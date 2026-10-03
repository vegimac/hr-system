using HrSystem.Controllers;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Periode löschen (Walter 03.10.2026): Nach «Zurück an GF» stehen die Lohnzettel auf
/// BERECHNET — im Lohnlauf offen, ohne «Wieder öffnen». Die dürfen das Löschen nicht
/// blockieren; bestätigte (FREIGEGEBEN_GF / HR_BESTAETIGT / final) schon.
/// </summary>
public class PeriodeLoeschenBerechnetTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("PerDel_" + testName + "_" + Guid.NewGuid()).Options);

    private static PayrollPeriode Periode(AppDbContext db)
    {
        var p = new PayrollPeriode
        {
            CompanyProfileId = 58, Year = 2026, Month = 7, Label = "Juli 2026",
            PeriodFrom = new DateOnly(2026, 7, 1), PeriodTo = new DateOnly(2026, 7, 31),
            Status = "offen"
        };
        db.PayrollPerioden.Add(p);
        db.SaveChanges();
        return p;
    }

    private static void Snapshot(AppDbContext db, int periodeId, int employeeId, string status)
    {
        db.PayrollSnapshots.Add(new PayrollSnapshot
        {
            PayrollPeriodeId = periodeId, EmployeeId = employeeId, CompanyProfileId = 58,
            SlipJson = "{}", Status = status
        });
        db.SaveChanges();
    }

    private static PayrollPeriodeController Controller(AppDbContext db) => new(db, null!, null!, null!);

    [Fact]
    public async Task NurBerechnete_PeriodeWirdMitLohnzettelnGeloescht()
    {
        using var db = NewDb();
        var p = Periode(db);
        Snapshot(db, p.Id, 1, "BERECHNET");
        Snapshot(db, p.Id, 2, "BERECHNET");

        var res = await Controller(db).DeletePeriode(p.Id);

        Assert.IsType<OkObjectResult>(res);
        Assert.Empty(await db.PayrollPerioden.ToListAsync());
        Assert.Empty(await db.PayrollSnapshots.ToListAsync());
    }

    [Fact]
    public async Task EinBestaetigter_BlockiertWeiterhin()
    {
        using var db = NewDb();
        var p = Periode(db);
        Snapshot(db, p.Id, 1, "BERECHNET");
        Snapshot(db, p.Id, 2, "FREIGEGEBEN_GF");

        var res = await Controller(db).DeletePeriode(p.Id);

        Assert.IsType<ConflictObjectResult>(res);
        Assert.Single(await db.PayrollPerioden.ToListAsync());
        Assert.Equal(2, await db.PayrollSnapshots.CountAsync());
    }
}

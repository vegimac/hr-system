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
/// «Änderung ab Datum» im Fenster «Zulage bearbeiten» (Walter 11.10.2026): die bisherige
/// Zulage endet am Vortag, die neue beginnt am gewählten Datum — die alte bleibt als Verlauf.
/// </summary>
public class ZulageAenderungAbDatumTests
{
    private static (AppDbContext db, FamilyMemberAllowancesController ctl, int kindId) Aufbau()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("ZulageAenderung_" + Guid.NewGuid()).Options);
        db.Employments.Add(new Employment
        {
            EmployeeId = 7, CompanyProfileId = 58, IsActive = true,
            ContractStartDate = new DateTime(2025, 1, 1)
        });
        var kind = new EmployeeFamilyMember { EmployeeId = 7, MemberType = "Kind", FirstName = "Test", LastName = "Kind" };
        db.EmployeeFamilyMembers.Add(kind);
        db.SaveChanges();
        var ctl = new FamilyMemberAllowancesController(db, new LohnEditLockService(db), new FamzKorrekturService(db))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (db, ctl, kind.Id);
    }

    private static AllowanceDto Dto(DateOnly ab, DateOnly? bis = null, string art = "AZ", int satz = 1) =>
        new(ab, bis, 310m, art, satz, null, null, ab);

    [Fact]
    public async Task Offene_Zulage_endet_am_Vortag_neue_beginnt()
    {
        var (db, ctl, kindId) = Aufbau();
        var alt = new FamilyMemberAllowance
        {
            FamilyMemberId = kindId, AllowanceType = "KZ", TarifSatzNr = 1, MonthlyAmount = 215m,
            ValidFrom = new DateOnly(2025, 8, 1)
        };
        db.FamilyMemberAllowances.Add(alt);
        await db.SaveChangesAsync();

        var res = await ctl.AenderungAbDatum(kindId, alt.Id, Dto(new DateOnly(2026, 11, 1)));

        Assert.IsType<OkObjectResult>(res);
        var alle = await db.FamilyMemberAllowances.OrderBy(a => a.ValidFrom).ToListAsync();
        Assert.Equal(2, alle.Count);
        Assert.Equal(new DateOnly(2026, 10, 31), alle[0].ValidTo);
        Assert.Equal("KZ", alle[0].AllowanceType);
        Assert.Equal(new DateOnly(2026, 11, 1), alle[1].ValidFrom);
        Assert.Null(alle[1].ValidTo);
        Assert.Equal("AZ", alle[1].AllowanceType);
    }

    [Fact]
    public async Task Bereits_frueher_beendete_Zulage_bleibt_unveraendert()
    {
        var (db, ctl, kindId) = Aufbau();
        var alt = new FamilyMemberAllowance
        {
            FamilyMemberId = kindId, AllowanceType = "AZ", TarifSatzNr = 1, MonthlyAmount = 310m,
            ValidFrom = new DateOnly(2025, 8, 1), ValidTo = new DateOnly(2026, 7, 31)
        };
        db.FamilyMemberAllowances.Add(alt);
        await db.SaveChangesAsync();

        var res = await ctl.AenderungAbDatum(kindId, alt.Id, Dto(new DateOnly(2026, 9, 1)));

        Assert.IsType<OkObjectResult>(res);
        var reload = await db.FamilyMemberAllowances.FindAsync(alt.Id);
        Assert.Equal(new DateOnly(2026, 7, 31), reload!.ValidTo);
        Assert.Equal(2, await db.FamilyMemberAllowances.CountAsync());
    }

    [Fact]
    public async Task Aenderung_vor_bisherigem_Beginn_wird_abgelehnt()
    {
        var (db, ctl, kindId) = Aufbau();
        var alt = new FamilyMemberAllowance
        {
            FamilyMemberId = kindId, AllowanceType = "KZ", TarifSatzNr = 1, MonthlyAmount = 215m,
            ValidFrom = new DateOnly(2026, 3, 1)
        };
        db.FamilyMemberAllowances.Add(alt);
        await db.SaveChangesAsync();

        var res = await ctl.AenderungAbDatum(kindId, alt.Id, Dto(new DateOnly(2026, 3, 1)));

        Assert.IsType<BadRequestObjectResult>(res);
        Assert.Equal(1, await db.FamilyMemberAllowances.CountAsync());
        Assert.Null((await db.FamilyMemberAllowances.FindAsync(alt.Id))!.ValidTo);
    }
}

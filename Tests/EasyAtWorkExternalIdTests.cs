using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.EasyAtWork;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Tests für die externen easy@work-Referenzen am Employment (Walter-Vorgabe
/// 23.06.2026): Cowork bleibt intern führend (employment.id), speichert aber
/// zusätzlich easyatwork_contract_id / easyatwork_pay_rate_id / easyatwork_updated_at.
/// Der Re-Sync matcht primär über diese IDs (idempotent), Fallback = Natural Key.
/// </summary>
public class EasyAtWorkExternalIdTests
{
    private static readonly DateOnly AsOf = new(2026, 6, 23);

    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
        => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("ExtId_" + testName + "_" + Guid.NewGuid()).Options);

    private static async Task<Employee> SeedAsync(AppDbContext db, string number = "2300004")
    {
        var e = new Employee { EmployeeNumber = number, FirstName = "Amire", LastName = "Mehmeti", IsActive = true };
        db.Employees.Add(e);
        await db.SaveChangesAsync();
        return e;
    }

    // ───────── Test 1: Segment speichert Contract-/PayRate-ID + UpdatedAt ─────────
    [Fact]
    public async Task Test1_SegmentSpeichertExterneIds()
    {
        using var db = NewDb();
        var emp = await SeedAsync(db);

        var contracts = new List<EawContract>
        {
            new() { Id = 30712, AmountType = "percent", Amount = 60m, Percentage = 60m,
                    FromRaw = "2025-12-31 23:00:00", ToRaw = null, UpdatedAtRaw = "2026-04-19 16:37:58" },
        };
        var rates = new List<EawPayRate>
        {
            new() { Id = 43326, Type = "month", Rate = 2760m, FromRaw = "2025-12-31 23:00:00", ToRaw = null,
                    UpdatedAtRaw = "2026-05-01 10:00:00" },
        };
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf, isKader: true);

        Assert.Equal(30712, tl[0].EasyAtWorkContractId);
        Assert.Equal(43326, tl[0].EasyAtWorkPayRateId);
        Assert.Equal(new DateTime(2026, 5, 1, 10, 0, 0), tl[0].EasyAtWorkUpdatedAt);  // max(contract, rate)

        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(db, emp, 1, tl, 5, "SHIFT_LEADER_7_PLUS", null);
        await db.SaveChangesAsync();

        var one = Assert.Single(await db.Employments.Where(x => x.EmployeeId == emp.Id).ToListAsync());
        Assert.Equal(30712, one.EasyAtWorkContractId);
        Assert.Equal(43326, one.EasyAtWorkPayRateId);
        Assert.Equal(new DateTime(2026, 5, 1, 10, 0, 0), one.EasyAtWorkUpdatedAt);
    }

    // ───────── Test 2: Re-Sync mit gleichen IDs aktualisiert dieselbe Zeile ─────────
    [Fact]
    public async Task Test2_ReSync_KeineDubletten()
    {
        using var db = NewDb();
        var emp = await SeedAsync(db);
        var (contracts, rates) = AmireData();

        for (int i = 0; i < 3; i++)
        {
            var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf, isKader: true);
            await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(db, emp, 1, tl, 5, "SHIFT_LEADER_7_PLUS", null);
            await db.SaveChangesAsync();
        }

        // Trotz 3 Läufen genau 2 Versionen (alt + neu), keine Dubletten.
        Assert.Equal(2, await db.Employments.CountAsync(x => x.EmployeeId == emp.Id));
    }

    // ───── Test 3: PayRate-Wechsel im selben Contract → neue Version, gleiche Contract-ID ─────
    [Fact]
    public async Task Test3_PayRateWechsel_NeueVersionGleicheContractId()
    {
        using var db = NewDb();
        var emp = await SeedAsync(db);

        var contracts = new List<EawContract>
        {
            new() { Id = 10, AmountType = "percent", Amount = 60m, Percentage = 60m, FromRaw = "2024-11-06 23:00:00", ToRaw = null },
        };
        var rates = new List<EawPayRate>
        {
            new() { Id = 20, Type = "month", Rate = 4000m, FromRaw = "2024-11-06 23:00:00", ToRaw = "2025-06-30 22:59:59" },
            new() { Id = 21, Type = "month", Rate = 4200m, FromRaw = "2025-06-30 23:00:00", ToRaw = null },
        };
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf);
        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(db, emp, 1, tl, null, null, null);
        await db.SaveChangesAsync();

        var rows = await db.Employments.Where(x => x.EmployeeId == emp.Id).OrderBy(x => x.ContractStartDate).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(10, r.EasyAtWorkContractId));   // gleiche Contract-ID
        Assert.Equal(20, rows[0].EasyAtWorkPayRateId);                      // alte PayRate
        Assert.Equal(21, rows[1].EasyAtWorkPayRateId);                      // neue PayRate
        Assert.Equal(new DateTime(2025, 6, 30), rows[0].ContractEndDate);  // alt auf Vortag beendet
        Assert.Equal(new DateTime(2025, 7, 1), rows[1].ContractStartDate);
        Assert.Equal(4000m, rows[0].MonthlySalary);
        Assert.Equal(4200m, rows[1].MonthlySalary);
    }

    // ───── Test 4: Historische Alt-Zeile ohne IDs bekommt nach Sync die IDs ─────
    [Fact]
    public async Task Test4_AltZeileOhneIds_BekommtIds()
    {
        using var db = NewDb();
        var emp = await SeedAsync(db);

        // Bestehende Zeile (aus altem Sync) OHNE easy@work-IDs, Start = Segmentstart.
        db.Employments.Add(new Employment
        {
            EmployeeId = emp.Id, CompanyProfileId = 1,
            ContractStartDate = new DateTime(2026, 1, 1), ContractEndDate = null, IsActive = true,
            EmploymentModel = "UTP", SalaryType = "hourly",
            EasyAtWorkContractId = null, EasyAtWorkPayRateId = null,
        });
        await db.SaveChangesAsync();

        var contracts = new List<EawContract>
        {
            new() { Id = 45583, AmountType = "percent", Amount = 60m, Percentage = 60m, FromRaw = "2025-12-31 23:00:00", ToRaw = null },
        };
        var rates = new List<EawPayRate>
        {
            new() { Id = 72192, Type = "month", Rate = 2760m, FromRaw = "2025-12-31 23:00:00", ToRaw = null },
        };
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf, isKader: true);
        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(db, emp, 1, tl, 5, "SHIFT_LEADER_7_PLUS", null);
        await db.SaveChangesAsync();

        // Fallback-Match über Start → dieselbe Zeile, jetzt mit IDs + korrigiert.
        var one = Assert.Single(await db.Employments.Where(x => x.EmployeeId == emp.Id).ToListAsync());
        Assert.Equal(45583, one.EasyAtWorkContractId);
        Assert.Equal(72192, one.EasyAtWorkPayRateId);
        Assert.Equal("FIX-M", one.EmploymentModel);
        Assert.Equal(2760m, one.MonthlySalary);
        Assert.Equal(4600m, one.MonthlySalaryFte);
    }

    // ───── Test 5: Amire — konkrete IDs, zwei Versionen, korrekte Löhne ─────
    [Fact]
    public async Task Test5_Amire_ZweiVersionenMitIds()
    {
        using var db = NewDb();
        var emp = await SeedAsync(db);
        var (contracts, rates) = AmireData();

        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf, isKader: true);
        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(db, emp, 1, tl, 5, "SHIFT_LEADER_7_PLUS", null);
        await db.SaveChangesAsync();

        var rows = await db.Employments.Where(x => x.EmployeeId == emp.Id).OrderBy(x => x.ContractStartDate).ToListAsync();
        Assert.Equal(2, rows.Count);

        Assert.Equal(30712, rows[0].EasyAtWorkContractId);
        Assert.Equal(43326, rows[0].EasyAtWorkPayRateId);
        Assert.Equal("FIX-M", rows[0].EmploymentModel);
        Assert.Equal(3436m, rows[0].MonthlySalary);
        Assert.Equal(4295m, rows[0].MonthlySalaryFte);
        Assert.False(rows[0].IsActive);

        Assert.Equal(45583, rows[1].EasyAtWorkContractId);
        Assert.Equal(72192, rows[1].EasyAtWorkPayRateId);
        Assert.Equal("FIX-M", rows[1].EmploymentModel);
        Assert.Equal(2760m, rows[1].MonthlySalary);
        Assert.Equal(4600m, rows[1].MonthlySalaryFte);
        Assert.True(rows[1].IsActive);
    }

    [Fact]
    public async Task Test6_PayRateOne_NeueZeile_SetztOverrideOhneLohn()
    {
        using var db = NewDb();
        var emp = await SeedAsync(db, "2300003");
        var contracts = new List<EawContract>
        {
            new() { Id = 46093, AmountType = "percent", Amount = 100m, Percentage = 100m, FromRaw = "2024-10-31 23:00:00" },
        };
        var rates = new List<EawPayRate>
        {
            new() { Id = 78034, Type = "month", Rate = 1m, FromRaw = "2023-12-31 23:00:00" },
        };

        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf, isKader: true);
        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(db, emp, 1, tl, 6, "REST_MANAGER", null);
        await db.SaveChangesAsync();

        var one = Assert.Single(await db.Employments.Where(x => x.EmployeeId == emp.Id).ToListAsync());
        Assert.Equal(46093, one.EasyAtWorkContractId);
        Assert.Equal(78034, one.EasyAtWorkPayRateId);
        Assert.True(one.EasyAtWorkManualOverride);
        Assert.Null(one.MonthlySalary);
        Assert.Null(one.MonthlySalaryFte);
        Assert.Null(one.HourlyRate);
    }

    [Fact]
    public async Task Test7_PayRateOne_BestehenderLohn_BleibtErhalten()
    {
        using var db = NewDb();
        var emp = await SeedAsync(db, "2300003");
        db.Employments.Add(new Employment
        {
            EmployeeId = emp.Id,
            CompanyProfileId = 1,
            ContractStartDate = new DateTime(2024, 11, 1),
            ContractEndDate = null,
            IsActive = true,
            EmploymentModel = "FIX-M",
            SalaryType = "monthly",
            EmploymentPercentage = 100m,
            MonthlySalary = 6100m,
            MonthlySalaryFte = 6100m
        });
        await db.SaveChangesAsync();

        var contracts = new List<EawContract>
        {
            new() { Id = 46093, AmountType = "percent", Amount = 100m, Percentage = 100m, FromRaw = "2024-10-31 23:00:00" },
        };
        var rates = new List<EawPayRate>
        {
            new() { Id = 78034, Type = "month", Rate = 1m, FromRaw = "2023-12-31 23:00:00" },
        };

        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf, isKader: true);
        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(db, emp, 1, tl, 6, "REST_MANAGER", null);
        await db.SaveChangesAsync();

        var one = Assert.Single(await db.Employments.Where(x => x.EmployeeId == emp.Id).ToListAsync());
        Assert.True(one.EasyAtWorkManualOverride);
        Assert.Equal(6100m, one.MonthlySalary);
        Assert.Equal(6100m, one.MonthlySalaryFte);
        Assert.Equal(46093, one.EasyAtWorkContractId);
        Assert.Equal(78034, one.EasyAtWorkPayRateId);
    }

    [Fact]
    public async Task Test8_ManualOverride_SchuetztVorEchtemEasyLohn()
    {
        using var db = NewDb();
        var emp = await SeedAsync(db, "2300003");
        db.Employments.Add(new Employment
        {
            EmployeeId = emp.Id,
            CompanyProfileId = 1,
            ContractStartDate = new DateTime(2024, 11, 1),
            ContractEndDate = null,
            IsActive = true,
            EmploymentModel = "FIX-M",
            SalaryType = "monthly",
            EmploymentPercentage = 100m,
            MonthlySalary = 6100m,
            MonthlySalaryFte = 6100m,
            EasyAtWorkManualOverride = true
        });
        await db.SaveChangesAsync();

        var contracts = new List<EawContract>
        {
            new() { Id = 46093, AmountType = "percent", Amount = 100m, Percentage = 100m, FromRaw = "2024-10-31 23:00:00" },
        };
        var rates = new List<EawPayRate>
        {
            new() { Id = 78035, Type = "month", Rate = 5000m, FromRaw = "2023-12-31 23:00:00" },
        };

        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf, isKader: true);
        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(db, emp, 1, tl, 6, "REST_MANAGER", null);
        await db.SaveChangesAsync();

        var one = Assert.Single(await db.Employments.Where(x => x.EmployeeId == emp.Id).ToListAsync());
        Assert.True(one.EasyAtWorkManualOverride);
        Assert.Equal(6100m, one.MonthlySalary);
        Assert.Equal(6100m, one.MonthlySalaryFte);
        Assert.Equal(46093, one.EasyAtWorkContractId);
        Assert.Equal(78035, one.EasyAtWorkPayRateId);
    }

    // Amire-Rohdaten: alter Contract 30712 / PayRate 43326, neuer 45583 / 72192.
    private static (List<EawContract>, List<EawPayRate>) AmireData() => (
        new List<EawContract>
        {
            new() { Id = 30712, AmountType = "percent", Amount = 80m, Percentage = 80m, FromRaw = "2024-11-06 23:00:00", ToRaw = "2024-12-31 22:59:59" },
            new() { Id = 45583, AmountType = "percent", Amount = 60m, Percentage = 60m, FromRaw = "2025-12-31 23:00:00", ToRaw = null },
        },
        new List<EawPayRate>
        {
            new() { Id = 43326, Type = "month", Rate = 3436m, FromRaw = "2024-11-06 23:00:00", ToRaw = "2024-12-31 22:59:59" },
            new() { Id = 72192, Type = "month", Rate = 2760m, FromRaw = "2025-12-31 23:00:00", ToRaw = null },
        });
}

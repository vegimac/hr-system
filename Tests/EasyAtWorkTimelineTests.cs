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
/// Tests für die easy@work-Employment-Timeline (Walter-Vorgabe 23.06.2026): die
/// komplette Vertrags-/Lohnhistorie (alt + aktuell + zukünftig) wird als
/// Employment-Versionen gespiegelt. <see cref="EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline"/>
/// ist API-frei + statisch; <see cref="EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync"/>
/// upsertet nach Natural Key (employee_id + company_profile_id + contract_start_date).
/// </summary>
public class EasyAtWorkTimelineTests
{
    private static readonly DateOnly AsOf = new(2026, 6, 23);

    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
        => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("Timeline_" + testName + "_" + Guid.NewGuid()).Options);

    // ───────────────────── Test 1: Amire-Historie ─────────────────────
    [Fact]
    public void Test1_AmireHistorie_ZweiVersionen()
    {
        var contracts = new List<EawContract>
        {
            new() { AmountType = "percent", Amount = 80m, Percentage = 80m, FromRaw = "2024-11-06 23:00:00", ToRaw = "2024-12-31 22:59:59" },
            new() { AmountType = "percent", Amount = 60m, Percentage = 60m, FromRaw = "2025-12-31 23:00:00", ToRaw = null },
        };
        var rates = new List<EawPayRate>
        {
            new() { Type = "month", Rate = 3436m, FromRaw = "2024-11-06 23:00:00", ToRaw = "2024-12-31 22:59:59" },
            new() { Type = "month", Rate = 2760m, FromRaw = "2025-12-31 23:00:00", ToRaw = null },
        };

        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf, isKader: true);

        Assert.Equal(2, tl.Count);

        var alt = tl[0];
        Assert.Equal(new DateOnly(2024, 11, 7), alt.Start);
        Assert.Equal(new DateOnly(2024, 12, 31), alt.End);
        Assert.Equal("FIX-M", alt.Info.EmploymentModel);
        Assert.Equal("monthly", alt.Info.SalaryType);
        Assert.Equal(80m, alt.Info.EmploymentPercentage);
        Assert.Equal(3436m, alt.Info.MonthlySalary);
        Assert.Equal(4295m, alt.Info.MonthlySalaryFte);   // 3436 / 80 × 100

        var neu = tl[1];
        Assert.Equal(new DateOnly(2026, 1, 1), neu.Start);
        Assert.Null(neu.End);
        Assert.Equal("FIX-M", neu.Info.EmploymentModel);
        Assert.Equal(60m, neu.Info.EmploymentPercentage);
        Assert.Equal(2760m, neu.Info.MonthlySalary);
        Assert.Equal(4600m, neu.Info.MonthlySalaryFte);   // 2760 / 60 × 100
    }

    // ───────── Test 2: PayRate-Wechsel innerhalb eines offenen Contracts ─────────
    [Fact]
    public void Test2_PayRateWechsel_ZweiVersionen()
    {
        var contracts = new List<EawContract>
        {
            new() { AmountType = "percent", Amount = 60m, Percentage = 60m, FromRaw = "2024-11-06 23:00:00", ToRaw = null },
        };
        var rates = new List<EawPayRate>
        {
            new() { Type = "month", Rate = 4000m, FromRaw = "2024-11-06 23:00:00", ToRaw = "2025-06-30 22:59:59" },
            new() { Type = "month", Rate = 4200m, FromRaw = "2025-06-30 23:00:00", ToRaw = null },
        };

        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf);

        Assert.Equal(2, tl.Count);
        Assert.Equal(new DateOnly(2024, 11, 7), tl[0].Start);
        Assert.Equal(new DateOnly(2025, 6, 30), tl[0].End);
        Assert.Equal(4000m, tl[0].Info.MonthlySalary);
        Assert.Equal(new DateOnly(2025, 7, 1), tl[1].Start);
        Assert.Null(tl[1].End);
        Assert.Equal(4200m, tl[1].Info.MonthlySalary);
    }

    // ───────── Test 3: Historischer falscher UTP-Vertrag wird korrigiert ─────────
    [Fact]
    public async Task Test3_FalscherUtp_WirdZuFixKorrigiert()
    {
        using var db = NewDb();
        var emp = new Employee { EmployeeNumber = "2300099", FirstName = "Amire", LastName = "Muster", IsActive = true };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        // Bestehender, FALSCHER Vertrag: UTP ohne Lohn, Start = 01.01.2026.
        db.Employments.Add(new Employment
        {
            EmployeeId = emp.Id, CompanyProfileId = 1,
            ContractStartDate = new DateTime(2026, 1, 1), ContractEndDate = null, IsActive = true,
            EmploymentModel = "UTP", SalaryType = "hourly", EmploymentPercentage = 40m,
        });
        await db.SaveChangesAsync();

        // easy@work liefert percent + month → FIX-M (Kader).
        var contracts = new List<EawContract>
        {
            new() { AmountType = "percent", Amount = 60m, Percentage = 60m, FromRaw = "2025-12-31 23:00:00", ToRaw = null },
        };
        var rates = new List<EawPayRate>
        {
            new() { Type = "month", Rate = 2760m, FromRaw = "2025-12-31 23:00:00", ToRaw = null },
        };
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf, isKader: true);

        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(
            db, emp, companyProfileId: 1, timeline: tl,
            jobGroupId: 5, jobGroupCode: "SHIFT_LEADER_7_PLUS", eawTo: null);
        await db.SaveChangesAsync();

        var one = Assert.Single(await db.Employments.Where(x => x.EmployeeId == emp.Id).ToListAsync());
        Assert.Equal("FIX-M", one.EmploymentModel);
        Assert.Equal("monthly", one.SalaryType);
        Assert.Equal(60m, one.EmploymentPercentage);
        Assert.Equal(2760m, one.MonthlySalary);
        Assert.Equal(4600m, one.MonthlySalaryFte);
        Assert.Null(one.HourlyRate);
        Assert.Null(one.GuaranteedHoursPerWeek);
        Assert.True(one.IsActive);
        Assert.Equal("SHIFT_LEADER_7_PLUS", one.JobTitle);
    }

    // ───────── Test 4: Volle Historie via Sync (alt geschlossen, neu offen) ─────────
    [Fact]
    public async Task Test4_VolleHistorie_ZweiEmployments()
    {
        using var db = NewDb();
        var emp = new Employee { EmployeeNumber = "2300100", FirstName = "Amire", LastName = "Test", IsActive = true };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var contracts = new List<EawContract>
        {
            new() { AmountType = "percent", Amount = 80m, Percentage = 80m, FromRaw = "2024-11-06 23:00:00", ToRaw = "2024-12-31 22:59:59" },
            new() { AmountType = "percent", Amount = 60m, Percentage = 60m, FromRaw = "2025-12-31 23:00:00", ToRaw = null },
        };
        var rates = new List<EawPayRate>
        {
            new() { Type = "month", Rate = 3436m, FromRaw = "2024-11-06 23:00:00", ToRaw = "2024-12-31 22:59:59" },
            new() { Type = "month", Rate = 2760m, FromRaw = "2025-12-31 23:00:00", ToRaw = null },
        };
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf, isKader: true);

        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(
            db, emp, 1, tl, jobGroupId: 5, jobGroupCode: "SHIFT_LEADER_7_PLUS", eawTo: null);
        await db.SaveChangesAsync();

        var rows = await db.Employments.Where(x => x.EmployeeId == emp.Id)
            .OrderBy(x => x.ContractStartDate).ToListAsync();
        Assert.Equal(2, rows.Count);

        Assert.Equal(new DateTime(2024, 11, 7), rows[0].ContractStartDate);
        Assert.Equal(new DateTime(2024, 12, 31), rows[0].ContractEndDate);
        Assert.False(rows[0].IsActive);
        Assert.Equal(3436m, rows[0].MonthlySalary);
        Assert.Equal(4295m, rows[0].MonthlySalaryFte);

        Assert.Equal(new DateTime(2026, 1, 1), rows[1].ContractStartDate);
        Assert.Null(rows[1].ContractEndDate);
        Assert.True(rows[1].IsActive);
        Assert.Equal(2760m, rows[1].MonthlySalary);
        Assert.Equal(4600m, rows[1].MonthlySalaryFte);
    }
}

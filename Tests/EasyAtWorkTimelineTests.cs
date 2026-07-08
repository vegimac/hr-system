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
            EmploymentModel = "FLEX", SalaryType = "hourly", EmploymentPercentage = 40m,
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

    // ───── Test 5: Bestehende falsche UTP-Zeile überlappt → kappen + korrekte Zeile ─────
    [Fact]
    public async Task Test5_FalscheUtpUeberlappt_WirdGekapptUndKorrigiert()
    {
        using var db = NewDb();
        var emp = new Employee { EmployeeNumber = "2300004", FirstName = "Amire", LastName = "Mehmeti", IsActive = true };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        // ALTE falsche Zeile: UTP ohne Lohn, 10.08.2021 – 08.12.2024 (überlappt easy@work).
        db.Employments.Add(new Employment
        {
            EmployeeId = emp.Id, CompanyProfileId = 1,
            ContractStartDate = new DateTime(2021, 8, 10), ContractEndDate = new DateTime(2024, 12, 8), IsActive = true,
            EmploymentModel = "FLEX", SalaryType = "hourly",
        });
        await db.SaveChangesAsync();

        var contracts = new List<EawContract>
        {
            new() { AmountType = "percent", Amount = 80m, Percentage = 80m, FromRaw = "2024-11-06 23:00:00", ToRaw = "2024-12-31 22:59:59" },
        };
        var rates = new List<EawPayRate>
        {
            new() { Type = "month", Rate = 3436m, FromRaw = "2024-11-06 23:00:00", ToRaw = "2024-12-31 22:59:59" },
        };
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf, isKader: false);

        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(
            db, emp, 1, tl, jobGroupId: null, jobGroupCode: null, eawTo: null);
        await db.SaveChangesAsync();

        var rows = await db.Employments.Where(x => x.EmployeeId == emp.Id)
            .OrderBy(x => x.ContractStartDate).ToListAsync();
        Assert.Equal(2, rows.Count);

        // Alte UTP-Zeile: hinten gekappt (spätestens 06.11.2024), inaktiv, NICHT gelöscht.
        var alt = rows[0];
        Assert.Equal(new DateTime(2021, 8, 10), alt.ContractStartDate);
        Assert.NotNull(alt.ContractEndDate);
        Assert.True(alt.ContractEndDate <= new DateTime(2024, 11, 6));
        Assert.False(alt.IsActive);

        // Neue korrekte Zeile 07.11.2024 – 31.12.2024, FIX, Monatslohn gesetzt.
        var neu = rows[1];
        Assert.Equal(new DateTime(2024, 11, 7), neu.ContractStartDate);
        Assert.Equal(new DateTime(2024, 12, 31), neu.ContractEndDate);
        Assert.Equal("FIX", neu.EmploymentModel);
        Assert.Equal(80m, neu.EmploymentPercentage);
        Assert.Equal(3436m, neu.MonthlySalary);
        Assert.Equal(4295m, neu.MonthlySalaryFte);
        Assert.Null(neu.HourlyRate);
    }

    // ───── Test 6: Unsortierte Roh-Listen (neu zuerst) → korrekte Timeline ─────
    [Fact]
    public void Test6_UnsortierteListen_KorrekteTimeline()
    {
        // Reihenfolge BEWUSST verkehrt: neuer Vertrag/Rate zuerst, alter danach.
        var contracts = new List<EawContract>
        {
            new() { AmountType = "percent", Amount = 60m, Percentage = 60m, FromRaw = "2025-12-31 23:00:00", ToRaw = null },
            new() { AmountType = "percent", Amount = 80m, Percentage = 80m, FromRaw = "2024-11-06 23:00:00", ToRaw = "2024-12-31 22:59:59" },
        };
        var rates = new List<EawPayRate>
        {
            new() { Type = "month", Rate = 2760m, FromRaw = "2025-12-31 23:00:00", ToRaw = null },
            new() { Type = "month", Rate = 3436m, FromRaw = "2024-11-06 23:00:00", ToRaw = "2024-12-31 22:59:59" },
        };

        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf, isKader: true);

        Assert.Equal(2, tl.Count);
        Assert.Equal(new DateOnly(2024, 11, 7), tl[0].Start);
        Assert.Equal(80m, tl[0].Info.EmploymentPercentage);
        Assert.Equal(3436m, tl[0].Info.MonthlySalary);
        Assert.Equal(4295m, tl[0].Info.MonthlySalaryFte);
        Assert.Equal(new DateOnly(2026, 1, 1), tl[1].Start);
        Assert.Null(tl[1].End);
        Assert.Equal(60m, tl[1].Info.EmploymentPercentage);
        Assert.Equal(2760m, tl[1].Info.MonthlySalary);
        Assert.Equal(4600m, tl[1].Info.MonthlySalaryFte);
    }

    // ───── Test 6: Sync-erzeugte Vertrags-Leiche → GELÖSCHT statt gekappt ─────
    // (Walter-Vorgabe 08.07.2026, Fall Beza 750080: Splitter aus Fehl-Importen.)
    // Bedingungen: EasyAtWorkContractId gesetzt + kein ManualOverride + nie im
    // Lohn verwendet + von keinem Timeline-Segment abgedeckt.
    [Fact]
    public async Task Test6_SyncErzeugteLeiche_WirdGeloescht()
    {
        using var db = NewDb();
        var emp = new Employee { EmployeeNumber = "750080", FirstName = "Beza", LastName = "Mamo", IsActive = true };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        // Leiche aus altem Fehl-Import: Eintages-FIX, traegt eine easy@work-ID,
        // die im aktuellen Timeline-Ergebnis nicht mehr vorkommt.
        db.Employments.Add(new Employment
        {
            EmployeeId = emp.Id, CompanyProfileId = 1,
            ContractStartDate = new DateTime(2026, 4, 1), ContractEndDate = new DateTime(2026, 4, 1),
            IsActive = false, EmploymentModel = "FIX", SalaryType = "monthly",
            EasyAtWorkContractId = 999888,
        });
        // Manuell gepflegte Zeile (Override) — darf NIE geloescht werden.
        db.Employments.Add(new Employment
        {
            EmployeeId = emp.Id, CompanyProfileId = 1,
            ContractStartDate = new DateTime(2020, 1, 1), ContractEndDate = new DateTime(2020, 12, 31),
            IsActive = false, EmploymentModel = "FIX", SalaryType = "monthly",
            EasyAtWorkContractId = 777666, EasyAtWorkManualOverride = true,
        });
        await db.SaveChangesAsync();

        var contracts = new List<EawContract>
        {
            new() { Id = 1, Type = "Flex", AmountType = "week", Amount = 17m, FromRaw = "2026-04-02" },
        };
        var rates = new List<EawPayRate>
        {
            new() { Id = 2, Type = "hour", Rate = 20.40m, FromRaw = "2026-04-02" },
        };
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf, isKader: false);
        var notes = new List<string>();

        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(
            db, emp, 1, tl, jobGroupId: null, jobGroupCode: null, eawTo: null,
            firstAllowedDate: null, skippedContracts: null, cleanupNotes: notes);
        await db.SaveChangesAsync();

        var rows = await db.Employments.Where(x => x.EmployeeId == emp.Id)
            .OrderBy(x => x.ContractStartDate).ToListAsync();

        // Leiche geloescht, Override-Zeile + korrekte neue FLEX-Zeile bleiben.
        Assert.DoesNotContain(rows, r => r.EasyAtWorkContractId == 999888);
        Assert.Contains(rows, r => r.EasyAtWorkContractId == 777666);
        Assert.Contains(rows, r => r.EmploymentModel == "FLEX" && r.HourlyRate == 20.40m);
        Assert.Contains(notes, n => n.Contains("gelöscht"));
    }

    // ───── Test 7: Override wird aufgelöst, sobald easy@work wieder echten Lohn liefert ─────
    // (Walter 08.07.2026, Fall Beza: Zeilen aus der Fehl-Import-Aera trugen den
    // Override-Stempel und blieben dadurch fuer immer eingefroren.)
    [Fact]
    public async Task Test7_OverrideWirdAufgeloest_WennEasyWiederLohnLiefert()
    {
        using var db = NewDb();
        var emp = new Employee { EmployeeNumber = "750080", FirstName = "Beza", LastName = "Mamo", IsActive = true };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        // Eingefrorene Zeile aus der Fehl-Import-Aera: FIX 1.4.-1.4., Override.
        db.Employments.Add(new Employment
        {
            EmployeeId = emp.Id, CompanyProfileId = 1,
            ContractStartDate = new DateTime(2026, 4, 1), ContractEndDate = new DateTime(2026, 4, 1),
            IsActive = false, EmploymentModel = "FIX", SalaryType = "monthly",
            EasyAtWorkManualOverride = true,
        });
        await db.SaveChangesAsync();

        // easy@work liefert jetzt korrekt: Flex/Woche ab 1.4. mit echtem Stundenlohn.
        var contracts = new List<EawContract>
        {
            new() { Id = 11, Type = "Flex", AmountType = "week", Amount = 17m, FromRaw = "2026-04-01" },
        };
        var rates = new List<EawPayRate>
        {
            new() { Id = 22, Type = "hour", Rate = 20.40m, FromRaw = "2026-04-01" },
        };
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(contracts, rates, AsOf, isKader: false);
        var notes = new List<string>();

        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(
            db, emp, 1, tl, jobGroupId: null, jobGroupCode: null, eawTo: null,
            firstAllowedDate: null, skippedContracts: null, cleanupNotes: notes);
        await db.SaveChangesAsync();

        var one = Assert.Single(await db.Employments.Where(x => x.EmployeeId == emp.Id).ToListAsync());
        Assert.False(one.EasyAtWorkManualOverride);            // Sperre geloest
        Assert.Equal("FLEX", one.EmploymentModel);             // korrigiert
        Assert.Equal(20.40m, one.HourlyRate);                  // echter Lohn uebernommen
        Assert.Null(one.ContractEndDate);                      // offen
        Assert.True(one.IsActive);
        Assert.Contains(notes, n => n.Contains("aufgelöst"));
    }
}



using System;
using System.Linq;
using System.Threading.Tasks;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.EasyAtWork;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Tests für den Filialwechsel beim easy@work-Sync (Walter-Vorgabe 23.06.2026).
/// Derselbe employee.id bleibt bestehen; nur die employment-Zeilen bilden die
/// Filial-Historie ab. Beim Wechsel wird der alte offene Filialvertrag sauber
/// beendet (Ende = Tag vor dem neuen Start, is_active=false) — NICHTS gelöscht,
/// kein Employee dupliziert. Geprüft wird der API-freie Schreiber
/// <see cref="EasyAtWorkEmployeeSyncService.CloseOtherBranchOpenEmploymentsAsync"/>
/// sowie die Vertrags-Aktiv-Regel der Frontend-Anzeige.
/// </summary>
public class EasyAtWorkBranchSwitchTests
{
    private const int BranchOld = 104;   // alte Filiale
    private const int BranchNew = 230;   // neue Filiale

    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
        => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("BranchSwitch_" + testName + "_" + Guid.NewGuid()).Options);

    private static async Task<Employee> SeedEmployeeAsync(AppDbContext db)
    {
        var e = new Employee { EmployeeNumber = "2300005", FirstName = "Anastasiia", LastName = "Muster", IsActive = true };
        db.Employees.Add(e);
        await db.SaveChangesAsync();
        return e;
    }

    private static Employment Emp(int empId, int branch, DateTime start, DateTime? end, bool active)
        => new Employment
        {
            EmployeeId = empId, CompanyProfileId = branch,
            ContractStartDate = start, ContractEndDate = end, IsActive = active,
            EmploymentModel = "UTP", SalaryType = "hourly",
        };

    // ───────────────────── B) Alten Filialvertrag schließen ─────────────────────

    [Fact]
    public async Task Filialwechsel_SchliesstAltenOffenenVertrag()
    {
        using var db = NewDb();
        var emp = await SeedEmployeeAsync(db);
        // Alte Filiale 104: offener Vertrag (kein Ende, is_active fälschlich true).
        db.Employments.Add(Emp(emp.Id, BranchOld, new DateTime(2024, 11, 7), null, active: true));
        // Neue Filiale 230: aktiver Vertrag ab 1.3.2026.
        var newStart = new DateTime(2026, 3, 1);
        db.Employments.Add(Emp(emp.Id, BranchNew, newStart, null, active: true));
        await db.SaveChangesAsync();

        // Neuer Vertrag in 230 ist aktiv (eaw.To leer) → 104 muss geschlossen werden.
        await EasyAtWorkEmployeeSyncService.CloseOtherBranchOpenEmploymentsAsync(
            db, emp.Id, currentBranchId: BranchNew, newStart: newStart, eawTo: null);
        await db.SaveChangesAsync();

        var alt = await db.Employments.SingleAsync(x => x.EmployeeId == emp.Id && x.CompanyProfileId == BranchOld);
        var neu = await db.Employments.SingleAsync(x => x.EmployeeId == emp.Id && x.CompanyProfileId == BranchNew);

        Assert.Equal(new DateTime(2026, 2, 28), alt.ContractEndDate);  // Tag vor 230-Start
        Assert.False(alt.IsActive);                                     // 104 inaktiv
        Assert.Null(neu.ContractEndDate);                               // 230 offen
        Assert.True(neu.IsActive);                                      // 230 aktiv
    }

    [Fact]
    public async Task Filialwechsel_LoeschtNichts_HistorieBleibt()
    {
        using var db = NewDb();
        var emp = await SeedEmployeeAsync(db);
        db.Employments.Add(Emp(emp.Id, BranchOld, new DateTime(2024, 11, 7), null, true));
        var newStart = new DateTime(2026, 3, 1);
        db.Employments.Add(Emp(emp.Id, BranchNew, newStart, null, true));
        await db.SaveChangesAsync();

        await EasyAtWorkEmployeeSyncService.CloseOtherBranchOpenEmploymentsAsync(
            db, emp.Id, BranchNew, newStart, eawTo: null);
        await db.SaveChangesAsync();

        // Beide Verträge bleiben erhalten — nur abgeschlossen, nicht gelöscht.
        Assert.Equal(2, await db.Employments.CountAsync(x => x.EmployeeId == emp.Id));
        // Employee bleibt EINMAL vorhanden (kein Duplikat).
        Assert.Equal(1, await db.Employees.CountAsync(e => e.EmployeeNumber == "2300005"));
    }

    [Fact]
    public async Task Filialwechsel_EawAusgetreten_SchliesstNichts()
    {
        using var db = NewDb();
        var emp = await SeedEmployeeAsync(db);
        db.Employments.Add(Emp(emp.Id, BranchOld, new DateTime(2024, 11, 7), null, true));
        await db.SaveChangesAsync();

        // eaw.To liegt in der Vergangenheit → neuer Vertrag NICHT aktiv → 104 unangetastet.
        await EasyAtWorkEmployeeSyncService.CloseOtherBranchOpenEmploymentsAsync(
            db, emp.Id, BranchNew, newStart: new DateTime(2026, 3, 1),
            eawTo: new DateOnly(2025, 1, 1));
        await db.SaveChangesAsync();

        var alt = await db.Employments.SingleAsync(x => x.CompanyProfileId == BranchOld);
        Assert.Null(alt.ContractEndDate);
        Assert.True(alt.IsActive);
    }

    [Fact]
    public async Task Filialwechsel_BereitsBeendeterVertrag_BleibtUnberuehrt()
    {
        using var db = NewDb();
        var emp = await SeedEmployeeAsync(db);
        // 104 endete schon vor dem neuen Start (kein Überlapp) → nicht erneut anfassen.
        var altEnde = new DateTime(2025, 1, 31);
        db.Employments.Add(Emp(emp.Id, BranchOld, new DateTime(2024, 11, 7), altEnde, false));
        await db.SaveChangesAsync();

        await EasyAtWorkEmployeeSyncService.CloseOtherBranchOpenEmploymentsAsync(
            db, emp.Id, BranchNew, newStart: new DateTime(2026, 3, 1), eawTo: null);
        await db.SaveChangesAsync();

        var alt = await db.Employments.SingleAsync(x => x.CompanyProfileId == BranchOld);
        Assert.Equal(altEnde, alt.ContractEndDate);   // unverändert
    }

    // ───────────────────── Frontend: Vertrags-Aktiv-Regel ─────────────────────
    // Spiegelt _empContractActiveOn(v, refDate) aus wwwroot/employees.js:
    // Beginn ≤ Stichtag UND (kein Ende ODER Ende ≥ Stichtag).

    private static bool ContractActiveOn(DateTime? start, DateTime? end, DateTime refDate)
    {
        if (start == null) return false;
        if (start.Value > refDate) return false;
        if (end == null) return true;
        return end.Value >= refDate;
    }

    [Fact]
    public void FrontendRegel_104BisGestern_HeuteNichtAktiv()
    {
        var today  = new DateTime(2026, 6, 23);
        var gestern = today.AddDays(-1);
        Assert.False(ContractActiveOn(new DateTime(2024, 11, 7), gestern, today));
    }

    [Fact]
    public void FrontendRegel_230Offen_HeuteAktiv()
    {
        var today = new DateTime(2026, 6, 23);
        Assert.True(ContractActiveOn(new DateTime(2026, 3, 1), null, today));
    }

    [Fact]
    public void FrontendRegel_ZukuenftigerBeginn_NichtAktiv()
    {
        var today = new DateTime(2026, 6, 23);
        Assert.False(ContractActiveOn(new DateTime(2026, 7, 1), null, today));
    }

    [Fact]
    public void FrontendRegel_EndeGenauHeute_NochAktiv()
    {
        var today = new DateTime(2026, 6, 23);
        Assert.True(ContractActiveOn(new DateTime(2026, 1, 1), today, today));
    }
}
